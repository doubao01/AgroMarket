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

    /// <summary>节点令牌签名密钥 (HS256, 建议 >=32 字节, 全联邦共享)。</summary>
    public string SharedKey { get; set; } = "";

    /// <summary>是否开启后台自动同步。</summary>
    public bool AutoSync { get; set; } = true;

    /// <summary>自动同步间隔 (分钟)。</summary>
    public int SyncIntervalMinutes { get; set; } = 30;

    /// <summary>每次上报回补的历史天数 (含当天)。</summary>
    public int BackfillDays { get; set; } = 1;

    /// <summary>明细记录上报时间窗 (小时)。</summary>
    public int RecordWindowHours { get; set; } = 24;

    /// <summary>单批明细记录上限。</summary>
    public int MaxRecordsPerPush { get; set; } = 500;

    /// <summary>签发的节点令牌有效期 (天)。</summary>
    public int TokenDays { get; set; } = 365;

    /// <summary>跨节点汇聚是否把本节点自身指标也纳入"全景" (默认开启)。</summary>
    public bool IncludeSelfInAggregate { get; set; } = true;

    /// <summary>令牌/身份强校验需要的字段是否齐备。</summary>
    public bool HasIdentity => !string.IsNullOrWhiteSpace(NodeCode) && !string.IsNullOrWhiteSpace(NodeName)
        && System.Text.Encoding.UTF8.GetByteCount(SharedKey ?? "") >= 32;

    /// <summary>后台同步是否应运行。</summary>
    public bool IsSyncActive => Enabled && AutoSync && HasIdentity;
}
