namespace AgroMarket.Api.Common;

/// <summary>
/// 分页偏移计算。(page-1)*pageSize 在 page 极大时会整数溢出为负数, 拼进 SQL 得到
/// "OFFSET -xxx" 会让 MySQL 直接报错(对外表现为 500)。这里统一用 long 计算并夹取页号上限。
/// </summary>
public static class Paging
{
    /// <summary>页号上限: 10000 页 × 200 条/页 已远超业务需要, 再大只会全表扫描。</summary>
    public const int MaxPage = 10000;

    public static long Offset(int page, int pageSize)
    {
        var p = Math.Clamp(page, 1, MaxPage);
        return (long)(p - 1) * Math.Max(pageSize, 0);
    }
}
