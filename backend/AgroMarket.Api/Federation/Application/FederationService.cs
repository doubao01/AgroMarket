using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Dtos;
using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Application;

public record IngestResult(int MetricCount, int RecordCount, long ReportId);

/// <summary>
/// 联动服务: 组织本节点 → 对端的推送/拉取, 以及接收对端上报后的落库。
/// 所有写库都幂等 (指标按 来源+日期, 明细按 来源+类型+外部ID)。
/// </summary>
public class FederationService
{
    private readonly FederationRepository _repo;
    private readonly FederationSnapshotBuilder _snapshot;
    private readonly FedHttpClient _http;
    private readonly FederationTokenService _tokens;
    private readonly FederationOptions _opt;
    private readonly ILogger<FederationService> _logger;

    public FederationService(FederationRepository repo, FederationSnapshotBuilder snapshot,
        FedHttpClient http, FederationTokenService tokens, IOptions<FederationOptions> opt,
        ILogger<FederationService> logger)
    {
        _repo = repo;
        _snapshot = snapshot;
        _http = http;
        _tokens = tokens;
        _opt = opt.Value;
        _logger = logger;
    }

    // ---------------- 接收并落库 (被上报端点调用) ----------------

    public async Task<IngestResult> IngestAsync(string sourceNodeCode, string batchNo, string periodDate,
        string? rawPayloadJson, IReadOnlyList<FedMetricDto> metrics, IReadOnlyList<FedRecordDto> records)
    {
        var cleanMetrics = metrics.Where(m => DateOnly.TryParse(m.StatDate, out _)).ToList();
        var cleanRecords = records
            .Where(r => FedRecordTypes.IsValid(r.Type) && !string.IsNullOrWhiteSpace(r.ExternalId))
            .Select(r => new FedRecordDto
            {
                Type = r.Type,
                ExternalId = Truncate(r.ExternalId, 80)!,
                RegionCode = Truncate(r.RegionCode, 20),
                OccurredAt = r.OccurredAt == default ? DateTime.Now : r.OccurredAt,
                Data = r.Data,
            })
            .ToList();

        var reportId = await _repo.UpsertReportAsync(sourceNodeCode, batchNo,
            string.IsNullOrWhiteSpace(periodDate) ? DateTime.Today.ToString("yyyy-MM-dd") : periodDate,
            cleanMetrics.Count, cleanRecords.Count, rawPayloadJson);
        await _repo.UpsertMetricsAsync(sourceNodeCode, cleanMetrics);
        await _repo.UpsertRecordsAsync(sourceNodeCode, cleanRecords);

        _logger.LogInformation("联动接收 {Source} 批次 {Batch}: {Metrics} 指标 / {Records} 明细",
            sourceNodeCode, batchNo, cleanMetrics.Count, cleanRecords.Count);
        return new IngestResult(cleanMetrics.Count, cleanRecords.Count, reportId);
    }

    // ---------------- 推送 ----------------

    public async Task<FedSyncResultDto> PushAsync(string? onlyNodeCode, CancellationToken ct = default)
    {
        var nodes = (await _repo.ListNodesAsync())
            .Where(n => n.Status == "ACTIVE"
                        && (n.Direction is FedDirections.Push or FedDirections.Both)
                        && (onlyNodeCode == null || n.NodeCode == onlyNodeCode))
            .ToList();

        var metrics = await _snapshot.BuildMetricsAsync(_opt.BackfillDays);
        var records = await _snapshot.BuildRecordsAsync(
            DateTime.Now.AddHours(-_opt.RecordWindowHours), DateTime.Now, _opt.MaxRecordsPerPush);

        var result = new FedSyncResultDto { Targets = nodes.Count };
        foreach (var node in nodes)
        {
            var payload = new FedReportPayload
            {
                NodeCode = _opt.NodeCode,
                BatchNo = $"{_opt.NodeCode}-{DateTime.Now:yyyyMMddHHmmss}",
                PeriodDate = DateTime.Today.ToString("yyyy-MM-dd"),
                GeneratedAt = DateTime.Now,
                Metrics = metrics,
                Records = records,
            };
            try
            {
                var token = string.IsNullOrWhiteSpace(node.NodeToken) ? _tokens.IssueSelf() : node.NodeToken!;
                await _http.PushAsync(node.BaseUrl, token, payload, ct);
                await _repo.TouchNodeSyncAsync(node.Id, push: true, null);
                result.Succeeded++;
                result.Details.Add(new FedSyncTargetResultDto
                {
                    NodeCode = node.NodeCode, Direction = FedDirections.Push, Ok = true,
                    MetricCount = metrics.Count, RecordCount = records.Count,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "联动推送 {Node} 失败", node.NodeCode);
                await _repo.TouchNodeSyncAsync(node.Id, push: true, Truncate(ex.Message, 300));
                result.Failed++;
                result.Details.Add(new FedSyncTargetResultDto
                {
                    NodeCode = node.NodeCode, Direction = FedDirections.Push, Ok = false,
                    Error = Truncate(ex.Message, 300),
                });
            }
        }
        return result;
    }

    // ---------------- 拉取 ----------------

    public async Task<FedSyncResultDto> PullAsync(string? onlyNodeCode, CancellationToken ct = default)
    {
        var nodes = (await _repo.ListNodesAsync())
            .Where(n => n.Status == "ACTIVE"
                        && (n.Direction is FedDirections.Pull or FedDirections.Both)
                        && (onlyNodeCode == null || n.NodeCode == onlyNodeCode))
            .ToList();

        var result = new FedSyncResultDto { Targets = nodes.Count };
        foreach (var node in nodes)
        {
            try
            {
                var token = string.IsNullOrWhiteSpace(node.NodeToken) ? _tokens.IssueSelf() : node.NodeToken!;
                var since = await _repo.GetCursorAsync(node.NodeCode, "RECORD")
                            ?? DateTime.Now.AddDays(-_opt.BackfillDays);
                var export = await _http.PullAsync(node.BaseUrl, token, since, _opt.MaxRecordsPerPush, ct)
                             ?? new FedExportDto();
                // 以对端响应中的自身节点编码为准 (而非本地登记的别名)
                var source = string.IsNullOrWhiteSpace(export.NodeCode) ? node.NodeCode : export.NodeCode;
                var batchNo = $"pull-{source}-{DateTime.Now:yyyyMMddHHmmss}";
                var ingest = await IngestAsync(source, batchNo,
                    DateTime.Today.ToString("yyyy-MM-dd"), null, export.Metrics, export.Records);

                // 游标推进到本次导出生成时间 (对端已覆盖该时刻之前的数据)
                var cursor = export.GeneratedAt == default ? DateTime.Now : export.GeneratedAt;
                await _repo.SetCursorAsync(node.NodeCode, "RECORD", cursor);
                await _repo.TouchNodeSyncAsync(node.Id, push: false, null);

                result.Succeeded++;
                result.Details.Add(new FedSyncTargetResultDto
                {
                    NodeCode = node.NodeCode, Direction = FedDirections.Pull, Ok = true,
                    MetricCount = ingest.MetricCount, RecordCount = ingest.RecordCount,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "联动拉取 {Node} 失败", node.NodeCode);
                await _repo.TouchNodeSyncAsync(node.Id, push: false, Truncate(ex.Message, 300));
                result.Failed++;
                result.Details.Add(new FedSyncTargetResultDto
                {
                    NodeCode = node.NodeCode, Direction = FedDirections.Pull, Ok = false,
                    Error = Truncate(ex.Message, 300),
                });
            }
        }
        return result;
    }

    /// <summary>供对端拉取: 生成本节点增量导出载荷。</summary>
    public async Task<FedExportDto> ExportAsync(DateTime since, int limit)
    {
        if (limit <= 0 || limit > 2000) limit = _opt.MaxRecordsPerPush;
        var metrics = await _snapshot.BuildMetricsAsync(_opt.BackfillDays);
        var records = await _snapshot.BuildRecordsAsync(since, DateTime.Now, limit);
        return new FedExportDto
        {
            NodeCode = _opt.NodeCode,
            GeneratedAt = DateTime.Now,
            HasMore = records.Count >= limit,
            Metrics = metrics,
            Records = records,
        };
    }

    private static string? Truncate(string? s, int len) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= len ? s : s[..len]);
}
