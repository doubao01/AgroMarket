namespace AgroMarket.Api.Federation.Infrastructure;

/// <summary>
/// 节点联动配置。同一套代码可通过本节点身份扮演省/市/县任一角色 (扁平多节点)。
/// 未配置 SharedKey / NodeCode 时联动为"未启用", 相关接口返回 400, 不影响业务功能。
/// </summary>
public class FederationOptions
{
    public const string SectionName = "Federation";

    /// <summary>是否启用联动 (后台定时同步总开关)。</summary>
    public bool Enabled { get; set; }

    /// <summary>本节点编码 (全局唯一)。</summary>
    public string NodeCode { get; set; } = "";

    /// <summary>本节点名称。</summary>
    public string NodeName { get; set; } = "";

    /// <summary>本节点层级: PROVINCE/CITY/COUNTY。</summary>
    public string Level { get; set; } = "COUNTY";

    /// <summary>本节点行政区划编码 (sys_region.region_code)。</summary>
    public string? RegionCode { get; set; }

    /// <summary>节点令牌签名密钥 (HS256, 建议 >=32 字节, 全联邦共享)。SYMMETRIC 模式必填。</summary>
    public string SharedKey { get; set; } = "";

    /// <summary>
    /// 节点令牌签名模式: SYMMETRIC(默认, HS256 共享密钥) / ASYMMETRIC(RS256 每节点密钥对)。
    /// ASYMMETRIC 下各节点独立私钥, 对端仅存公钥, 单点泄露不再可伪造全网令牌。
    /// </summary>
    public string TokenMode { get; set; } = TokenModes.Symmetric;

    /// <summary>是否开启后台自动同步。</summary>
    public bool AutoSync { get; set; } = true;

    /// <summary>自动同步间隔 (分钟)。</summary>
    public int SyncIntervalMinutes { get; set; } = 30;

    /// <summary>每次上报回补的历史天数 (含当天)。默认 7, 防止节点中断 >1 天导致指标永久缺失。</summary>
    public int BackfillDays { get; set; } = 7;

    /// <summary>明细记录上报时间窗 (小时)。</summary>
    public int RecordWindowHours { get; set; } = 24;

    /// <summary>单批明细记录上限。</summary>
    public int MaxRecordsPerPush { get; set; } = 500;

    /// <summary>签发的节点令牌有效期 (天)。</summary>
    public int TokenDays { get; set; } = 365;

    /// <summary>跨节点汇聚是否把本节点自身指标也纳入"全景" (默认开启)。</summary>
    public bool IncludeSelfInAggregate { get; set; } = true;

    /// <summary>
    /// 是否把"已接收的下级数据"再向上级转发 (多跳 县→市→省)。
    /// 仅中间/汇聚节点开启; 开启后上行载荷会附带原始来源节点编码 (sourceNodeCode)。
    /// </summary>
    public bool ForwardReceived { get; set; }

    /// <summary>
    /// 是否接收"非直接对端来源"的转发数据 (即载荷中 sourceNodeCode 不等于令牌身份的行使)。
    /// 仅上级/汇聚节点开启; 关闭时来源与令牌身份不一致一律 403 (默认关闭, 防越权伪造)。
    /// </summary>
    public bool AcceptForwarded { get; set; }

    /// <summary>
    /// 本节点角色: BUSINESS(默认, 村级经营节点, 业务读写正常) / AGGREGATOR(汇聚只读节点, 禁业务写接口)。
    /// </summary>
    public string Role { get; set; } = FedRoles.Business;

    /// <summary>对端同步"过期"判定阈值 (分钟); &lt;=0 时取 max(2*SyncIntervalMinutes, 60)。</summary>
    public int HealthStaleMinutes { get; set; }

    /// <summary>令牌/身份强校验需要的字段是否齐备。</summary>
    public bool HasIdentity => !string.IsNullOrWhiteSpace(NodeCode) && !string.IsNullOrWhiteSpace(NodeName)
        && (IsAsymmetric || System.Text.Encoding.UTF8.GetByteCount(SharedKey ?? "") >= 32);

    /// <summary>是否使用非对称 (RS256) 令牌签名。</summary>
    public bool IsAsymmetric => string.Equals(TokenMode, TokenModes.Asymmetric, StringComparison.OrdinalIgnoreCase);

    /// <summary>是否为汇聚只读节点。</summary>
    public bool IsAggregator => string.Equals(Role, FedRoles.Aggregator, StringComparison.OrdinalIgnoreCase);
}

/// <summary>节点令牌签名模式。</summary>
public static class TokenModes
{
    public const string Symmetric = "SYMMETRIC";
    public const string Asymmetric = "ASYMMETRIC";
    public static bool IsValid(string? v) =>
        string.Equals(v, Symmetric, StringComparison.OrdinalIgnoreCase)
        || string.Equals(v, Asymmetric, StringComparison.OrdinalIgnoreCase);
}

/// <summary>节点角色。</summary>
public static class FedRoles
{
    public const string Business = "BUSINESS";
    public const string Aggregator = "AGGREGATOR";
    public static bool IsValid(string? v) =>
        string.Equals(v, Business, StringComparison.OrdinalIgnoreCase)
        || string.Equals(v, Aggregator, StringComparison.OrdinalIgnoreCase);
}
