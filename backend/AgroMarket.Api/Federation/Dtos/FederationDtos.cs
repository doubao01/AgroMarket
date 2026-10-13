using System.Text.Json;

namespace AgroMarket.Api.Federation.Dtos;

/// <summary>节点层级。</summary>
public static class FedLevels
{
    public const string Province = "PROVINCE";
    public const string City = "CITY";
    public const string County = "COUNTY";
    public static bool IsValid(string? v) => v is Province or City or County;
}

/// <summary>同步方向。</summary>
public static class FedDirections
{
    public const string Push = "PUSH";
    public const string Pull = "PULL";
    public const string Both = "BOTH";
    public static bool IsValid(string? v) => v is Push or Pull or Both;
}

/// <summary>业务明细类型。</summary>
public static class FedRecordTypes
{
    public const string Order = "ORDER";
    public const string Supply = "SUPPLY";
    public const string Demand = "DEMAND";
    public static readonly string[] All = { Order, Supply, Demand };
    public static bool IsValid(string? v) => v is Order or Supply or Demand;
}

/// <summary>节点身份 (由节点令牌解析)。</summary>
public record NodeIdentity(string NodeCode, string NodeName, string Level, string? RegionCode);

// ---------------- 请求 ----------------

/// <summary>注册/更新对端节点。</summary>
public record FedNodeUpsertRequest(
    string NodeCode, string NodeName, string Level, string? RegionCode,
    string BaseUrl, string? NodeToken, string? Direction, string? Status);

/// <summary>签发节点令牌 (nodeCode 为空则签发本节点)。mode 为空用配置的 TokenMode。</summary>
public record FedIssueTokenRequest(string? NodeCode, string? NodeName, string? Level, string? RegionCode, int? Days,
    string? Mode = null);

/// <summary>按天汇总指标。</summary>
public class FedMetricDto
{
    /// <summary>原始来源节点编码 (多跳转发时由中间节点回填; 空表示载荷发送方即来源)。</summary>
    public string? SourceNodeCode { get; set; }
    public string StatDate { get; set; } = "";   // yyyy-MM-dd
    public long UserCount { get; set; }
    public long ActiveUserCount { get; set; }
    public long NewUserCount { get; set; }
    public long SupplyCount { get; set; }
    public long DemandCount { get; set; }
    public long OrderCount { get; set; }
    public decimal TradeAmount { get; set; }
    public long MessageCount { get; set; }
    public long ConversationCount { get; set; }
}

/// <summary>脱敏业务明细。</summary>
public class FedRecordDto
{
    /// <summary>原始来源节点编码 (多跳转发时由中间节点回填; 空表示载荷发送方即来源)。</summary>
    public string? SourceNodeCode { get; set; }
    public string Type { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? RegionCode { get; set; }
    public DateTime OccurredAt { get; set; }
    public JsonElement Data { get; set; }
}

/// <summary>上报/导出的批次载荷。</summary>
public class FedReportPayload
{
    public string NodeCode { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public string PeriodDate { get; set; } = "";  // yyyy-MM-dd
    public DateTime GeneratedAt { get; set; }
    public List<FedMetricDto> Metrics { get; set; } = new();
    public List<FedRecordDto> Records { get; set; } = new();
}

// ---------------- 响应 ----------------

/// <summary>节点公钥 (对外分发, 供对端校验 RS256 令牌)。</summary>
public class FedKeyDto
{
    public string NodeCode { get; set; } = "";
    public string TokenMode { get; set; } = "";
    public string Alg { get; set; } = "";
    public string PublicKey { get; set; } = "";
}

/// <summary>本节点信息。</summary>
public class FedSelfDto
{
    public bool Enabled { get; set; }
    public bool HasIdentity { get; set; }
    public string NodeCode { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string Level { get; set; } = "";
    public string? RegionCode { get; set; }
    public bool AutoSync { get; set; }
    public int SyncIntervalMinutes { get; set; }
    public string TokenMode { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IncludeSelfInAggregate { get; set; }
    public bool ForwardReceived { get; set; }
    public bool AcceptForwarded { get; set; }
}

/// <summary>同步健康总览。</summary>
public class FedHealthDto
{
    public bool Enabled { get; set; }
    public string NodeCode { get; set; } = "";
    public bool AutoSync { get; set; }
    public int SyncIntervalMinutes { get; set; }
    public int StaleMinutes { get; set; }
    public DateTime CheckedAt { get; set; }
    public bool Healthy { get; set; }
    public int ActiveNodes { get; set; }
    public int UnhealthyNodes { get; set; }
    public List<FedPeerHealthDto> Peers { get; set; } = new();
}

/// <summary>单个对端的同步健康。</summary>
public class FedPeerHealthDto
{
    public string NodeCode { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string Level { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? LastPushAt { get; set; }
    public DateTime? LastPullAt { get; set; }
    public DateTime? LastSyncAt { get; set; }
    public double? LastSyncMinutes { get; set; }
    public bool Stale { get; set; }
    public bool Healthy { get; set; }
    public string? LastError { get; set; }
}

/// <summary>对端节点。</summary>
public class FedNodeDto
{
    public long Id { get; set; }
    public string NodeCode { get; set; } = "";
    public string NodeName { get; set; } = "";
    public string Level { get; set; } = "";
    public string? RegionCode { get; set; }
    public string BaseUrl { get; set; } = "";
    public bool HasToken { get; set; }
    public bool HasPublicKey { get; set; }
    public string Direction { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime? LastPushAt { get; set; }
    public DateTime? LastPullAt { get; set; }
    public string? LastError { get; set; }
}

/// <summary>上报批次摘要。</summary>
public class FedReportSummaryDto
{
    public long Id { get; set; }
    public string SourceNodeCode { get; set; } = "";
    public string BatchNo { get; set; } = "";
    public string PeriodDate { get; set; } = "";
    public int MetricCount { get; set; }
    public int RecordCount { get; set; }
    public DateTime ReceivedAt { get; set; }
}

/// <summary>存储的明细记录视图。</summary>
public class FedRecordViewDto
{
    public long Id { get; set; }
    public string SourceNodeCode { get; set; } = "";
    public string Type { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? RegionCode { get; set; }
    public DateTime OccurredAt { get; set; }
    public JsonElement Data { get; set; }
    public DateTime ReceivedAt { get; set; }
}

/// <summary>汇聚指标点。</summary>
public class FedAggregatePointDto
{
    public string StatDate { get; set; } = "";
    public long OrderCount { get; set; }
    public decimal TradeAmount { get; set; }
    public long UserCount { get; set; }
    public long SupplyCount { get; set; }
    public long DemandCount { get; set; }
}

/// <summary>汇聚结果。</summary>
public class FedAggregateDto
{
    public string Range { get; set; } = "";
    public string? RegionPrefix { get; set; }
    public int NodeCount { get; set; }
    public long TotalOrders { get; set; }
    public decimal TotalTradeAmount { get; set; }
    public List<FedAggregatePointDto> Points { get; set; } = new();
    public List<FedNodeOrderStatDto> ByNode { get; set; } = new();
}

/// <summary>按来源节点聚合。</summary>
public class FedNodeOrderStatDto
{
    public string SourceNodeCode { get; set; } = "";
    public long OrderCount { get; set; }
    public decimal TradeAmount { get; set; }
}

/// <summary>推送/拉取结果。</summary>
public class FedSyncResultDto
{
    public int Targets { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public List<FedSyncTargetResultDto> Details { get; set; } = new();
}

public class FedSyncTargetResultDto
{
    public string NodeCode { get; set; } = "";
    public string Direction { get; set; } = "";
    public bool Ok { get; set; }
    public int MetricCount { get; set; }
    public int RecordCount { get; set; }
    public string? Error { get; set; }
}

/// <summary>导出载荷 (拉取方消费)。</summary>
public class FedExportDto
{
    public string NodeCode { get; set; } = "";
    public DateTime GeneratedAt { get; set; }
    public bool HasMore { get; set; }
    /// <summary>本页记录中最新的 occurred_at; 有更多数据时拉取方以此为下一页游标 (增量分页)。</summary>
    public DateTime? NextSince { get; set; }
    public List<FedMetricDto> Metrics { get; set; } = new();
    public List<FedRecordDto> Records { get; set; } = new();
}
