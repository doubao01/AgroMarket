namespace AgroMarket.Api.Common;

/// <summary>
/// 分页偏移计算。(page-1)*pageSize 在 page 极大时会整数溢出为负数, 拼进 SQL 得到
/// "OFFSET -xxx" 会让 MySQL 直接报错(对外表现为 500)。这里统一用 long 计算并夹取页号上限。
/// </summary>
public static class Paging
{
    /// <summary>页号上限: 200 页 × 50 条 = 1 万条, 已远超实际业务需要。原先 10000 页等于给匿名抓取留了 50 万条的翻页空间, 再深只会退化成全表扫描。</summary>
    public const int MaxPage = 200;

    /// <summary>把页号夹到合法区间, 让响应里的 page 与实际取数一致(而不是回显调用方传的超大值)。</summary>
    public static int Normalize(int page) => Math.Clamp(page, 1, MaxPage);

    public static long Offset(int page, int pageSize)
    {
        var p = Normalize(page);
        return (long)(p - 1) * Math.Max(pageSize, 0);
    }
}
