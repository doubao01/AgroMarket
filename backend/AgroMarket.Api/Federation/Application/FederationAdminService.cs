using System.Text.Json;
using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Dtos;
using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Application;

/// <summary>管理员侧的联动管理: 节点注册/令牌签发、上报审计、指标/明细查询、跨节点汇聚。</summary>
public class FederationAdminService
{
    private readonly FederationRepository _repo;
    private readonly FederationTokenService _tokens;
    private readonly FederationOptions _opt;

    public FederationAdminService(FederationRepository repo, FederationTokenService tokens, IOptions<FederationOptions> opt)
    {
        _repo = repo;
        _tokens = tokens;
        _opt = opt.Value;
    }

    public FedSelfDto GetSelf() => new()
    {
        Enabled = _opt.Enabled,
        HasIdentity = _opt.HasIdentity,
        NodeCode = _opt.NodeCode,
        NodeName = _opt.NodeName,
        Level = _opt.Level,
        RegionCode = _opt.RegionCode,
        AutoSync = _opt.AutoSync,
        SyncIntervalMinutes = _opt.SyncIntervalMinutes,
    };

    public async Task<List<FedNodeDto>> ListNodesAsync()
    {
        var rows = await _repo.ListNodesAsync();
        return rows.Select(n => new FedNodeDto
        {
            Id = n.Id, NodeCode = n.NodeCode, NodeName = n.NodeName, Level = n.Level,
            RegionCode = n.RegionCode, BaseUrl = n.BaseUrl, HasToken = !string.IsNullOrWhiteSpace(n.NodeToken),
            Direction = n.Direction, Status = n.Status,
            LastPushAt = n.LastPushAt, LastPullAt = n.LastPullAt, LastError = n.LastError,
        }).ToList();
    }

    public async Task<long> UpsertNodeAsync(FedNodeUpsertRequest req)
    {
        ValidateNode(req.NodeCode, req.NodeName, req.Level, req.BaseUrl);
        if (req.NodeCode == _opt.NodeCode)
            throw new BizException("不能把本节点注册为对端", ErrorCodes.BadRequest);
        var direction = FedDirections.IsValid(req.Direction) ? req.Direction! : FedDirections.Both;
        var status = req.Status is "ACTIVE" or "DISABLED" ? req.Status : "ACTIVE";
        return await _repo.UpsertNodeAsync(req.NodeCode.Trim(), req.NodeName.Trim(), req.Level,
            EmptyToNull(req.RegionCode), req.BaseUrl.Trim(), EmptyToNull(req.NodeToken), direction!, status);
    }

    public async Task UpdateNodeAsync(long id, FedNodeUpsertRequest req)
    {
        ValidateNode(req.NodeCode, req.NodeName, req.Level, req.BaseUrl);
        var direction = FedDirections.IsValid(req.Direction) ? req.Direction! : FedDirections.Both;
        var status = req.Status is "ACTIVE" or "DISABLED" ? req.Status : "ACTIVE";
        var n = await _repo.UpdateNodeAsync(id, req.NodeName.Trim(), req.Level,
            EmptyToNull(req.RegionCode), req.BaseUrl.Trim(), EmptyToNull(req.NodeToken), direction!, status);
        if (n == 0) throw new BizException("节点不存在", ErrorCodes.NotFound);
    }

    public async Task SetNodeStatusAsync(long id, string status)
    {
        if (status is not ("ACTIVE" or "DISABLED")) throw new BizException("状态只能是 ACTIVE/DISABLED", ErrorCodes.BadRequest);
        var n = await _repo.SetNodeStatusAsync(id, status);
        if (n == 0) throw new BizException("节点不存在", ErrorCodes.NotFound);
    }

    public async Task DeleteNodeAsync(long id)
    {
        var n = await _repo.DeleteNodeAsync(id);
        if (n == 0) throw new BizException("节点不存在", ErrorCodes.NotFound);
    }

    /// <summary>签发节点令牌。nodeCode 为空时签发本节点令牌。</summary>
    public object IssueToken(FedIssueTokenRequest req)
    {
        var nodeCode = string.IsNullOrWhiteSpace(req.NodeCode) ? _opt.NodeCode : req.NodeCode!.Trim();
        var nodeName = string.IsNullOrWhiteSpace(req.NodeName) ? (nodeCode == _opt.NodeCode ? _opt.NodeName : nodeCode) : req.NodeName!.Trim();
        var level = FedLevels.IsValid(req.Level) ? req.Level! : (nodeCode == _opt.NodeCode ? _opt.Level : FedLevels.County);
        var regionCode = EmptyToNull(req.RegionCode) ?? (nodeCode == _opt.NodeCode ? _opt.RegionCode : null);
        var token = _tokens.Issue(nodeCode, nodeName, level, regionCode, req.Days ?? 0);
        return new { nodeCode, nodeName, level, regionCode, token };
    }

    public async Task<(List<FedReportSummaryDto> Items, long Total)> ListReportsAsync(string? source, int page, int size)
    {
        if (page < 1) page = 1;
        if (size is < 1 or > 200) size = 20;
        var (rows, total) = await _repo.QueryReportsAsync(source, page, size);
        return (rows.Select(r => new FedReportSummaryDto
        {
            Id = r.Id, SourceNodeCode = r.SourceNodeCode, BatchNo = r.BatchNo, PeriodDate = r.PeriodDate,
            MetricCount = r.MetricCount, RecordCount = r.RecordCount, ReceivedAt = r.ReceivedAt,
        }).ToList(), total);
    }

    public async Task<List<FedMetricDto>> ListMetricsAsync(string? source, string? range, string? fromDate = null, string? toDate = null)
    {
        var (defFrom, defTo) = ParseRange(range);
        var from = DateTime.TryParse(fromDate, out var f) ? f : defFrom;
        var to = DateTime.TryParse(toDate, out var t) ? t : defTo;
        var rows = await _repo.QueryMetricsAsync(source, from, to);
        return rows.Select(r => new FedMetricDto
        {
            StatDate = r.StatDate, UserCount = r.UserCount, ActiveUserCount = r.ActiveUserCount,
            NewUserCount = r.NewUserCount, SupplyCount = r.SupplyCount, DemandCount = r.DemandCount,
            OrderCount = r.OrderCount, TradeAmount = r.TradeAmount, MessageCount = r.MessageCount,
            ConversationCount = r.ConversationCount,
        }).ToList();
    }

    public async Task<List<FedRecordViewDto>> ListRecordsAsync(string? source, string? type, int days, int limit, int offset)
    {
        if (type != null && !FedRecordTypes.IsValid(type)) throw new BizException("明细类型不合法", ErrorCodes.BadRequest);
        if (days is < 1 or > 365) days = 7;
        if (limit is < 1 or > 200) limit = 50;
        if (offset < 0) offset = 0;
        var from = DateTime.Today.AddDays(-(days - 1));
        var rows = await _repo.QueryRecordsAsync(source, type, from, null, limit, offset);
        return rows.Select(ToView).ToList();
    }

    public async Task<FedAggregateDto> AggregateAsync(string? range)
    {
        var (from, to) = ParseRange(range);
        var byNode = await _repo.AggregateByNodeAsync(from, to);
        var byDay = await _repo.AggregateByDayAsync(from, to);
        return new FedAggregateDto
        {
            Range = range ?? "30d",
            NodeCount = byNode.Count,
            TotalOrders = byNode.Sum(x => x.OrderCount),
            TotalTradeAmount = byNode.Sum(x => x.TradeAmount),
            Points = byDay.Select(p => new FedAggregatePointDto
            {
                StatDate = p.StatDate, OrderCount = p.OrderCount, TradeAmount = p.TradeAmount,
                UserCount = p.UserCount, SupplyCount = p.SupplyCount, DemandCount = p.DemandCount,
            }).ToList(),
            ByNode = byNode.Select(n => new FedNodeOrderStatDto
            {
                SourceNodeCode = n.SourceNodeCode, OrderCount = n.OrderCount, TradeAmount = n.TradeAmount,
            }).ToList(),
        };
    }

    private static FedRecordViewDto ToView(FedRecordRow r) => new()
    {
        Id = r.Id, SourceNodeCode = r.SourceNodeCode, Type = r.Type, ExternalId = r.ExternalId,
        RegionCode = r.RegionCode, OccurredAt = r.OccurredAt,
        Data = ParseJson(r.Data), ReceivedAt = r.ReceivedAt,
    };

    private static JsonElement ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        try { return JsonSerializer.Deserialize<JsonElement>(json); }
        catch { return default; }
    }

    private static (DateTime From, DateTime To) ParseRange(string? range)
    {
        var days = range switch { "7d" => 7, "90d" => 90, "365d" => 365, _ => 30 };
        var to = DateTime.Today;
        return (to.AddDays(-(days - 1)), to);
    }

    private static void ValidateNode(string nodeCode, string nodeName, string level, string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(nodeCode)) throw new BizException("节点编码不能为空", ErrorCodes.BadRequest);
        if (nodeCode.Length > 64) throw new BizException("节点编码过长", ErrorCodes.BadRequest);
        if (string.IsNullOrWhiteSpace(nodeName)) throw new BizException("节点名称不能为空", ErrorCodes.BadRequest);
        if (!FedLevels.IsValid(level)) throw new BizException("节点层级只能是 PROVINCE/CITY/COUNTY", ErrorCodes.BadRequest);
        if (string.IsNullOrWhiteSpace(baseUrl) || !(baseUrl.StartsWith("http://") || baseUrl.StartsWith("https://")))
            throw new BizException("对端地址需以 http:// 或 https:// 开头", ErrorCodes.BadRequest);
    }

    private static string? EmptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
