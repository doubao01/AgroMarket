using System.Text.Json;
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
    private readonly FederationSettings _settings;
    private readonly ILogger<FederationService> _logger;

    public FederationService(FederationRepository repo, FederationSnapshotBuilder snapshot,
        FedHttpClient http, FederationTokenService tokens, IOptions<FederationOptions> opt,
        FederationSettings settings, ILogger<FederationService> logger)
    {
        _repo = repo;
        _snapshot = snapshot;
        _http = http;
        _tokens = tokens;
        _opt = opt.Value;
        _settings = settings;
        _logger = logger;
    }

    // ---------------- 接收并落库 (被上报端点调用) ----------------

    public async Task<IngestResult> IngestAsync(string sourceNodeCode, string batchNo, string periodDate,
        string? rawPayloadJson, IReadOnlyList<FedMetricDto> metrics, IReadOnlyList<FedRecordDto> records)
    {
        var cleanMetrics = metrics.Where(m => DateOnly.TryParse(m.StatDate, out _))
            .Select(m => new FedMetricDto
            {
                SourceNodeCode = NormalizeSource(m.SourceNodeCode, sourceNodeCode),
                StatDate = m.StatDate,
                UserCount = m.UserCount, ActiveUserCount = m.ActiveUserCount, NewUserCount = m.NewUserCount,
                SupplyCount = m.SupplyCount, DemandCount = m.DemandCount, OrderCount = m.OrderCount,
                TradeAmount = m.TradeAmount, MessageCount = m.MessageCount, ConversationCount = m.ConversationCount,
            })
            .ToList();
        var cleanRecords = records
            .Where(r => FedRecordTypes.IsValid(r.Type) && !string.IsNullOrWhiteSpace(r.ExternalId))
            .Select(r => new FedRecordDto
            {
                SourceNodeCode = NormalizeSource(r.SourceNodeCode, sourceNodeCode),
                Type = r.Type,
                ExternalId = Truncate(r.ExternalId, 80)!,
                RegionCode = Truncate(r.RegionCode, 20),
                OccurredAt = r.OccurredAt == default ? DateTime.Now : r.OccurredAt,
                Data = PiiMasker.MaskData(r.Data),
            })
            .ToList();

        // 逐行按"原始来源"归组落库: 直接上报 → 发送方即来源; 转发 → 保留原始来源编码 (多跳 县→市→省)
        foreach (var group in cleanMetrics.GroupBy(m => m.SourceNodeCode!))
            await _repo.UpsertMetricsAsync(group.Key, group);
        foreach (var group in cleanRecords.GroupBy(r => r.SourceNodeCode!))
            await _repo.UpsertRecordsAsync(group.Key, group);

        var reportId = await _repo.UpsertReportAsync(sourceNodeCode, batchNo,
            string.IsNullOrWhiteSpace(periodDate) ? DateTime.Today.ToString("yyyy-MM-dd") : periodDate,
            cleanMetrics.Count, cleanRecords.Count, PiiMasker.MaskJson(rawPayloadJson));

        var forwardedSources = cleanMetrics.Select(m => m.SourceNodeCode!)
            .Concat(cleanRecords.Select(r => r.SourceNodeCode!))
            .Where(c => !string.Equals(c, sourceNodeCode, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _logger.LogInformation("联动接收 {Source} 批次 {Batch}: {Metrics} 指标 / {Records} 明细 (转发来源 {Forwarded} 个)",
            sourceNodeCode, batchNo, cleanMetrics.Count, cleanRecords.Count, forwardedSources);
        return new IngestResult(cleanMetrics.Count, cleanRecords.Count, reportId);
    }

    /// <summary>
    /// 归一行的真实来源: 空 → 令牌身份 (发送方即来源); 与令牌身份一致 → 同一; 否则视为转发,
    /// 仅在接收端开启 AcceptForwarded 时接受, 否则 403 (防止伪造他节点身份)。
    /// </summary>
    private string NormalizeSource(string? rowSource, string tokenSource)
    {
        if (string.IsNullOrWhiteSpace(rowSource)) return tokenSource;
        var s = rowSource.Trim();
        if (string.Equals(s, tokenSource, StringComparison.OrdinalIgnoreCase)) return tokenSource;
        if (!_settings.AcceptForwarded)
            throw new BizException("上报来源与令牌身份不一致, 且本节点未开启转发接收", ErrorCodes.Forbidden);
        return Truncate(s, 64)!;
    }

    // ---------------- 推送 ----------------

    public async Task<FedSyncResultDto> PushAsync(string? onlyNodeCode, CancellationToken ct = default)
    {
        var nodes = (await _repo.ListNodesAsync())
            .Where(n => n.Status == "ACTIVE"
                        && (n.Direction is FedDirections.Push or FedDirections.Both)
                        && (onlyNodeCode == null || n.NodeCode == onlyNodeCode))
            .ToList();

        var metrics = await _snapshot.BuildMetricsAsync(_settings.BackfillDays);
        var records = await _snapshot.BuildRecordsAsync(
            DateTime.Now.AddHours(-_settings.RecordWindowHours), DateTime.Now, _opt.MaxRecordsPerPush);
        if (_settings.ForwardReceived)
        {
            metrics = metrics.Concat(await BuildForwardMetricsAsync()).ToList();
            records = records.Concat(await BuildForwardRecordsAsync(DateTime.Now.AddHours(-_settings.RecordWindowHours))).ToList();
        }

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
                            ?? DateTime.Now.AddDays(-_settings.BackfillDays);

                // 增量分页: 对端按 HasMore/NextSince 逐页返回, 直至取完或无法推进, 避免超限明细被静默丢弃。
                var totalMetrics = 0;
                var totalRecords = 0;
                var page = 0;
                var cursor = DateTime.Now;
                while (true)
                {
                    var export = await _http.PullAsync(node.BaseUrl, token, since, _opt.MaxRecordsPerPush, ct)
                                 ?? new FedExportDto();
                    // 以对端响应中的自身节点编码为准 (而非本地登记的别名)
                    var source = string.IsNullOrWhiteSpace(export.NodeCode) ? node.NodeCode : export.NodeCode;
                    var batchNo = Truncate($"pull-{source}-{DateTime.Now:yyyyMMddHHmmssfff}-{page}", 64)!;
                    var ingest = await IngestAsync(source, batchNo,
                        DateTime.Today.ToString("yyyy-MM-dd"), null, export.Metrics, export.Records);
                    totalMetrics += ingest.MetricCount;
                    totalRecords += ingest.RecordCount;
                    page++;

                    var next = export.NextSince;
                    if (!export.HasMore)
                    {
                        // 对端已无更多: 可安全推进到对端水位(优先 NextSince, 否则其导出时刻)
                        cursor = next ?? (export.GeneratedAt == default ? DateTime.Now : export.GeneratedAt);
                        break;
                    }
                    if (!next.HasValue || next.Value <= since)
                    {
                        // 游标无法推进: 保留已消费水位, 不跳到墙钟, 否则中间明细会被永久跳过
                        break;
                    }
                    since = next.Value;
                    cursor = next.Value; // 本页消费成功, 水位单调前移到该页的继续点
                    if (page >= 100)
                    {
                        // 触顶: 只推进到已消费水位, 下一轮从该点继续, 不丢数据
                        _logger.LogInformation("联动拉取 {Node} 达单轮页数上限, 游标保留在 {Cursor:o} 待下轮续拉", node.NodeCode, cursor);
                        break;
                    }
                }

                await _repo.SetCursorAsync(node.NodeCode, "RECORD", cursor);
                await _repo.TouchNodeSyncAsync(node.Id, push: false, null);

                result.Succeeded++;
                result.Details.Add(new FedSyncTargetResultDto
                {
                    NodeCode = node.NodeCode, Direction = FedDirections.Pull, Ok = true,
                    MetricCount = totalMetrics, RecordCount = totalRecords,
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

    /// <summary>供对端拉取: 生成本节点增量导出载荷 (明细按 occurred_at 升序, 供游标分页)。</summary>
    public async Task<FedExportDto> ExportAsync(DateTime since, int limit)
    {
        if (limit <= 0 || limit > 2000) limit = _opt.MaxRecordsPerPush;
        var metrics = await _snapshot.BuildMetricsAsync(_settings.BackfillDays);
        var local = await _snapshot.BuildRecordsAsync(since, DateTime.Now, limit, oldestFirst: true);
        var records = local;
        var hasMore = local.Count >= limit;
        if (_settings.ForwardReceived)
        {
            metrics = metrics.Concat(await BuildForwardMetricsAsync()).ToList();
            var fwd = await BuildForwardRecordsAsync(since);
            records = records.Concat(fwd).ToList();
            if (fwd.Count >= _opt.MaxRecordsPerPush) hasMore = true;
        }
        return new FedExportDto
        {
            NodeCode = _opt.NodeCode,
            GeneratedAt = DateTime.Now,
            HasMore = hasMore,
            NextSince = local.Count > 0 ? local.Max(r => r.OccurredAt) : null,
            Metrics = metrics,
            Records = records,
        };
    }

    private static string? Truncate(string? s, int len) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= len ? s : s[..len]);

    // ---------------- 多跳转发构建 (把已接收的下级数据再向上层附带) ----------------

    /// <summary>把已接收的非本节点指标 (BackfillDays 窗口内) 转成带原始来源的载荷。</summary>
    private async Task<List<FedMetricDto>> BuildForwardMetricsAsync()
    {
        var to = DateTime.Today;
        var from = to.AddDays(-(_settings.BackfillDays - 1));
        var rows = await _repo.ExportForwardMetricsAsync(from, to, _opt.NodeCode);
        return rows.Select(r => new FedMetricDto
        {
            SourceNodeCode = r.SourceNodeCode,
            StatDate = r.StatDate,
            UserCount = r.UserCount, ActiveUserCount = r.ActiveUserCount, NewUserCount = r.NewUserCount,
            SupplyCount = r.SupplyCount, DemandCount = r.DemandCount, OrderCount = r.OrderCount,
            TradeAmount = r.TradeAmount, MessageCount = r.MessageCount, ConversationCount = r.ConversationCount,
        }).ToList();
    }

    /// <summary>把在 sinceReceived 之后收到、且非本节点来源的明细转成带原始来源的载荷。</summary>
    private async Task<List<FedRecordDto>> BuildForwardRecordsAsync(DateTime sinceReceived)
    {
        var rows = await _repo.ExportForwardRecordsAsync(sinceReceived, _opt.MaxRecordsPerPush, _opt.NodeCode);
        return rows.Select(ToRecordDto).ToList();
    }

    private static FedRecordDto ToRecordDto(FedRecordRow r) => new()
    {
        SourceNodeCode = r.SourceNodeCode,
        Type = r.Type,
        ExternalId = r.ExternalId,
        RegionCode = r.RegionCode,
        OccurredAt = r.OccurredAt,
        // 转发前再脱敏一次: 库里可能存着本次修复之前入站的明文行, 不能假设对方已经脱敏
        Data = PiiMasker.MaskData(ParseData(r.Data)),
    };

    private static JsonElement ParseData(string? json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
    }
}
