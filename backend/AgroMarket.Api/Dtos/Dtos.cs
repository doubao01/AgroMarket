namespace AgroMarket.Api.Dtos;

// ---------- 区域 ----------
public record RegionDto
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public long? ParentId { get; set; }
}
public record RegionNode(long Id, string Code, string Name, int Level, List<RegionNode> Children);

// ---------- 认证 ----------
public record SendSmsRequest(string Phone);
public record LoginByCodeRequest(string Phone, string Code);
public record LoginByPasswordRequest(string Phone, string Password);
public record RegisterRequest(string Phone, string Code, string Password, string Nickname, string Role, long? HomeRegionId);
public record AuthResponse(long Id, string Phone, string Nickname, string Role, string Token, long? HomeRegionId, bool ContactDisplay, string? ContactPhone, string? AvatarUrl);

// ---------- 用户 ----------
public record UpdateProfileRequest(string? Nickname, string? AvatarUrl, long? HomeRegionId, bool? ContactDisplay, string? ContactPhone, string? Role);
public record ChangePasswordRequest(string OldPassword, string NewPassword);
public record MerchantProfileDto
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string SubjectType { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string? IdNumber { get; set; }
    public string? LicenseUrl { get; set; }
    public string AuditStatus { get; set; } = "";
    public string? AuditRemark { get; set; }
    public DateTime CreatedAt { get; set; }
}
public record MerchantProfileRequest(string SubjectType, string SubjectName, string? IdNumber, string? LicenseUrl);

// ---------- 供应 ----------
public record SupplyImageDto(long Id, string Url);
public record SupplyDto(
    long Id, long PublisherId, string PublisherName, string Title, long CategoryId, string? CategoryName,
    string? Description, decimal Quantity, string Unit, decimal? Price, string PriceUnit, bool Negotiable,
    decimal? MinOrderQty, long? CountyId, string? CountyName, long? TownId, string? TownName, long? VillageId, string? VillageName,
    string? Spec, string? HarvestTime, string? DeliveryMode, string? ContactPhone, string? TradeNote,
    string Status, DateTime CreatedAt, List<SupplyImageDto> Images, decimal SoldQuantity);
public record SupplyQuery(
    string? Keyword, long? CategoryId, decimal? PriceMin, decimal? PriceMax,
    long? CountyId, long? TownId, long? VillageId, bool TownIncludeVillages,
    long? PublisherId, string? Status, string? OrderBy, int Page = 1, int PageSize = 10)
{
    public int Page { get; set; } = Page;
    public int PageSize { get; set; } = PageSize;
    public long? PublisherId { get; set; } = PublisherId;
    /// <summary>用户常驻乡镇: 置顶优先展示, 不做硬过滤。</summary>
    public long? PreferTownId { get; set; }
}
public record SupplyCreateRequest(
    string Title, long CategoryId, string? Description, decimal Quantity, string Unit,
    decimal? Price, string PriceUnit, bool Negotiable, decimal? MinOrderQty,
    long? CountyId, long? TownId, long? VillageId, string? Spec, string? HarvestTime,
    string? DeliveryMode, string? ContactPhone, string? TradeNote, int EffectiveDays, List<string>? Images);

// ---------- 采购 ----------
public record DemandImageDto(long Id, string Url);
public record DemandDto(
    long Id, long PublisherId, string PublisherName, string Title, long CategoryId, string? CategoryName,
    string? Description, decimal Quantity, string Unit, decimal? PriceMin, decimal? PriceMax, string PriceUnit,
    string? SpecRequired, long? CountyId, string? CountyName, long? TownId, string? TownName, long? VillageId,
    DateTime? ExpectDate, string? PackagingNote, string? ContactName, string? ContactPhone,
    string Status, DateTime CreatedAt, bool Negotiable, int QuoteCount, List<DemandImageDto> Images);
public record DemandQuery(
    string? Keyword, long? CategoryId, decimal? PriceMin, decimal? PriceMax,
    long? CountyId, long? TownId, long? VillageId, bool TownIncludeVillages,
    long? PublisherId, string? Status, string? OrderBy, int Page = 1, int PageSize = 10)
{
    public int Page { get; set; } = Page;
    public int PageSize { get; set; } = PageSize;
    public long? PublisherId { get; set; } = PublisherId;
    /// <summary>用户常驻乡镇: 置顶优先展示, 不做硬过滤。</summary>
    public long? PreferTownId { get; set; }
}
public record DemandCreateRequest(
    string Title, long CategoryId, string? Description, decimal Quantity, string Unit,
    decimal? PriceMin, decimal? PriceMax, string PriceUnit, string? SpecRequired,
    long? CountyId, long? TownId, long? VillageId, DateTime? ExpectDate, string? PackagingNote,
    string? ContactName, string? ContactPhone, int EffectiveDays, bool Negotiable, List<string>? Images);

// ---------- 报价 ----------
public record QuoteDto(
    long Id, string QuoteNo, long? SupplyId, string? SupplyTitle, long? DemandId, string? DemandTitle,
    long BuyerId, string BuyerName, long SellerId, string SellerName, long InitiatorId,
    decimal Price, string PriceUnit, decimal Quantity, string? Remark, string Status,
    long? OrderId, DateTime CreatedAt);
public record QuoteCreateRequest(
    long? SupplyId, long? DemandId, decimal Price, string PriceUnit, decimal Quantity, string? Remark);
public record QuoteCounterRequest(decimal Price, decimal? Quantity, string? Remark);

// ---------- 订单 ----------
public record OrderDto(
    long Id, string OrderNo, long QuoteId, long BuyerId, string BuyerName, long SellerId, string SellerName,
    decimal TotalAmount, string Status, string? PayChannel, DateTime? PaidAt, DateTime? ShippedAt,
    DateTime? CompletedAt, DateTime? CancelledAt, string? CancelReason, string? DeliveryMode,
    string? AddressSnapshot, string? Remark, DateTime CreatedAt, List<OrderItemDto> Items, List<StatusLogDto>? StatusLogs);
public record OrderItemDto
{
    public long Id { get; set; }
    public long? SupplyId { get; set; }
    public string ProductName { get; set; } = "";
    public string? Spec { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal Price { get; set; }
    public string PriceUnit { get; set; } = "";
    public decimal Amount { get; set; }
}
public record StatusLogDto
{
    public long Id { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public long? OperatorId { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
}
public record OrderCreateFromQuoteRequest(string? DeliveryMode, string? AddressSnapshot, string? Remark, string? ClientOrderId = null);
public record OrderStatusRequest(string? Status, string? Reason, string? Note);

// ---------- 收藏 / 消息 / 举报 / 公告 / 地址 ----------
public record FavoriteDto(long Id, string TargetType, long TargetId, string? Title, decimal? Price, DateTime CreatedAt);
public record MessageDto
{
    public long Id { get; set; }
    public long? SenderId { get; set; }
    public string MsgType { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Content { get; set; }
    public long? BizId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}
public record ReportCreateRequest(string TargetType, long TargetId, string Reason);
public record ReportDto
{
    public long Id { get; set; }
    public long ReporterId { get; set; }
    public string TargetType { get; set; } = "";
    public long TargetId { get; set; }
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "";
    public string? HandleRemark { get; set; }
    public DateTime CreatedAt { get; set; }
}
public record NoticeDto
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public bool IsPublished { get; set; }
    public DateTime CreatedAt { get; set; }
}
public record AddressDto(long Id, string ContactName, string Phone, long? ProvinceId, long? CityId, long? CountyId, long? TownId, long? VillageId, string Detail, bool IsDefault);
public record AddressRequest(string ContactName, string Phone, long? CountyId, long? TownId, long? VillageId, string Detail, bool IsDefault);

// ---------- 文件分片上传 ----------
public record UploadInitRequest(string FileName, long FileSize);
public record UploadCompleteRequest(string UploadId);

// ---------- 支付 ----------
/// <summary>发起支付。ClientToken 为客户端幂等键: 同一次点击重试应复用同一个值, 避免刷出多张可支付单。</summary>
public record PaymentCreateRequest(long OrderId, string? Channel, string? ClientToken = null);
public record PaymentDto
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string Channel { get; set; } = "";
    public string? TradeNo { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "";
    public bool PaidNow { get; set; }
    public string? PayUrl { get; set; }
    public DateTime CreatedAt { get; set; }
}
public record PaymentChannelDto(string Channel, string Name, bool Available, bool Mock);

// ---------- 通用 ----------
public record PagedResult<T>(List<T> Items, long Total, int Page, int PageSize);
public record IdRequest(long Id);
public record StatsDto(long Users, long ActiveUsers, long Supplies, long Demands, long Orders, decimal DealAmount, List<TownStat> TownStats, long DemoOrders, decimal DemoDealAmount);
public record TownStat
{
    public long TownId { get; set; }
    public string TownName { get; set; } = "";
    public long SupplyCount { get; set; }
    public long DemandCount { get; set; }
}
