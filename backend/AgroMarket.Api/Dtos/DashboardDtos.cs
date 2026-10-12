namespace AgroMarket.Api.Dtos;

// ---------- 数据大屏 ----------

/// <summary>大屏总览指标。</summary>
public record DashboardOverviewDto(
    long Users, long ActiveUsers, long Supplies, long Demands,
    long Orders, decimal DealAmount,
    long TodayOrders, decimal TodayDealAmount,
    long WeekOrders, decimal WeekDealAmount,
    long DemoOrders, decimal DemoDealAmount);

/// <summary>成交趋势中的一天。</summary>
public record TrendPointDto(string Date, long OrderCount, decimal DealAmount);

/// <summary>品类分布一项。</summary>
public record CategoryStatDto(string CategoryName, long SupplyCount, long DemandCount, decimal DealAmount);

/// <summary>地区分布一项。</summary>
public record RegionStatDto(long TownId, string TownName, long SupplyCount, long DemandCount, decimal DealAmount);

/// <summary>用户结构一项。</summary>
public record RoleStatDto(string Role, long Count);
public record UserTrendPointDto(string Date, long NewUsers, long ActiveUsers);

/// <summary>消息活跃一项。</summary>
public record ChatActivityDto(string Date, long MessageCount, long ConversationCount);
