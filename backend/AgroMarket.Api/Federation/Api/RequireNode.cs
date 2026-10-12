using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Application;
using AgroMarket.Api.Federation.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AgroMarket.Api.Federation.Api;

/// <summary>标记该端点需要节点令牌 (节点间接口), 与用户登录鉴权无关。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireNodeAttribute : Attribute { }

/// <summary>进入 Action 前校验节点令牌; 通过后把 NodeIdentity 放入 HttpContext.Items["NodeIdentity"]。</summary>
public class RequireNodeFilter : IAsyncAuthorizationFilter
{
    private readonly FederationTokenService _tokens;
    public RequireNodeFilter(FederationTokenService tokens) => _tokens = tokens;

    public Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var need = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<RequireNodeAttribute>() != null;
        if (!need) return Task.CompletedTask;

        var identity = _tokens.ParseFromHeader(ctx.HttpContext.Request);
        if (identity == null)
        {
            ctx.Result = new ObjectResult(ApiResponse<object>.Fail(ErrorCodes.Unauthorized, "节点令牌无效或缺失"))
            { StatusCode = 401 };
            return Task.CompletedTask;
        }
        ctx.HttpContext.Items[NodeContext.Key] = identity;
        return Task.CompletedTask;
    }
}

/// <summary>节点身份访问器。</summary>
public static class NodeContext
{
    public const string Key = "NodeIdentity";

    public static NodeIdentity Node(this HttpContext ctx) =>
        ctx.Items[Key] as NodeIdentity
        ?? throw new BizException("缺少节点身份", ErrorCodes.Unauthorized);
}
