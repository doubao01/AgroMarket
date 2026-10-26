namespace AgroMarket.Api.Common;

/// <summary>
/// 敏感字段脱敏。
///
/// 身份证号在列表页只需要"能对上人", 不需要完整值: 全量下发意味着一次截图、一次
/// 日志落盘就把用户证件号带出去了。完整值留在确需核对时的单独接口里, 且有审计。
/// </summary>
public static class Mask
{
    /// <summary>身份证: 保留前 4 位与后 4 位。太短或为空时整体打码。</summary>
    public static string? IdNumber(string? id)
        => string.IsNullOrEmpty(id) ? id
         : id.Length < 9 ? new string('*', id.Length)
         : $"{id[..4]}{new string('*', id.Length - 8)}{id[^4..]}";

    /// <summary>手机号: 保留前 3 位与后 4 位。</summary>
    public static string? Phone(string? phone)
        => string.IsNullOrEmpty(phone) ? phone
         : phone.Length < 8 ? new string('*', phone.Length)
         : $"{phone[..3]}{new string('*', phone.Length - 7)}{phone[^4..]}";
}
