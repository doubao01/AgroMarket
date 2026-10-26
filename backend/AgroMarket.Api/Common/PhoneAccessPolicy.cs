using System.Data;
using Dapper;

namespace AgroMarket.Api.Common;

/// <summary>
/// 联系电话的可见性口径 —— 全仓唯一一处。
///
/// 之前这条规则散在各个 Service 里, 且彼此不一致: 采购详情给了"本人或管理员",
/// 供应详情只给"本人"(管理员反而看不到)。同一类数据两套规则, 改一处很容易漏另一处,
/// 前面 contact_display 变成死开关就是同类问题。
///
/// 口径:
///  - 列表: 一律不返回电话(避免翻页批量抓取, 见 V13);
///  - 详情: 本人与在职管理员可见, 其他人一律 null。
/// </summary>
public static class PhoneAccessPolicy
{
    /// <summary>判断 viewer 能否看到 owner 的联系电话。</summary>
    public static async Task<bool> CanSeeAsync(IDbConnection conn, long? viewerId, long ownerId)
    {
        if (viewerId == null) return false;
        if (viewerId == ownerId) return true;
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT role='ADMIN' AND status='ACTIVE' FROM sys_user WHERE id=@id", new { id = viewerId });
    }
}
