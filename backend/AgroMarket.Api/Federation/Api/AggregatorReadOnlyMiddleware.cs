using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Api;

/// <summary>
/// 汇聚只读节点拦截: 当 Federation:Role=AGGREGATOR 时, 拒绝本节点的业务写接口
/// (仅放行登录、节点间接口与联动管理), 保持汇聚节点仅上收/查看、不产生本地业务数据。
/// </summary>
public class AggregatorReadOnlyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<FederationOptions> _opt;

    public AggregatorReadOnlyMiddleware(RequestDelegate next, IOptionsMonitor<FederationOptions> opt)
    {
        _next = next;
        _opt = opt;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (_opt.CurrentValue.IsAggregator && IsBlockedWrite(ctx.Request.Method, ctx.Request.Path.Value ?? ""))
            throw new BizException("本节点为汇聚(只读)节点, 不提供业务写操作", ErrorCodes.Forbidden);
        await _next(ctx);
    }

    /// <summary>业务写请求判定: 允许登录/节点间接口/联动管理, 其余 /api 的写操作一律拦截。</summary>
    internal static bool IsBlockedWrite(string method, string path)
    {
        if (method is not ("POST" or "PUT" or "PATCH" or "DELETE")) return false;
        if (path.StartsWith("/api/admin/federation", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/federation", StringComparison.OrdinalIgnoreCase)) return false;
        if (path.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)) return false;
        return path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
    }
}
