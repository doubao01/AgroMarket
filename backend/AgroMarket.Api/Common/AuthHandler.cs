using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgroMarket.Api.Common;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Dapper;

namespace AgroMarket.Api.Common;

/// <summary>
/// 轻量 JWT 鉴权 Handler: 校验 Token 并填充 User 上下文。
/// 标记 [RequireAuth] 的端点未登录时返回 401。
/// </summary>
public class TokenAuthOptions : AuthenticationSchemeOptions
{
    public string? SecretKey { get; set; }
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAuthAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireAdminAttribute : Attribute { }

public class CurrentUser
{
    public long Id { get; set; }
    public string Phone { get; set; } = "";
    public string Role { get; set; } = "";
}

/// <summary>进入 Action 前强制校验 [RequireAuth]/[RequireAdmin], 未认证直接返回业务错误 JSON。</summary>
public class RequireAuthFilter : IAsyncAuthorizationFilter
{
    private readonly IDbConnectionFactory _db;
    public RequireAuthFilter(IDbConnectionFactory db) => _db = db;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext ctx)
    {
        var metadata = ctx.HttpContext.GetEndpoint()?.Metadata;
        var requireAdmin = metadata?.GetMetadata<RequireAdminAttribute>() != null;
        var requireAuth = requireAdmin || metadata?.GetMetadata<RequireAuthAttribute>() != null;
        var user = ctx.HttpContext.Items["CurrentUser"] as CurrentUser;
        if (user != null)
        {
            using var conn = _db.Create();
            var current = await conn.QuerySingleOrDefaultAsync<AuthService.UserRow>(
                "SELECT id, phone, role, status FROM sys_user WHERE id=@id", new { id = user.Id });
            if (current == null || current.Status != "ACTIVE")
                throw new BizException("账号已冻结或不可用", ErrorCodes.Forbidden);
            user.Role = current.Role;
            user.Phone = current.Phone;
        }
        if (!requireAuth) return;
        if (user == null || user.Id == 0)
        {
            ctx.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(
                ApiResponse<object>.Fail(ErrorCodes.Unauthorized, "请先登录")) { StatusCode = 401 };
            return;
        }
        if (requireAdmin && user.Role != "ADMIN")
        {
            ctx.Result = new Microsoft.AspNetCore.Mvc.ObjectResult(
                ApiResponse<object>.Fail(ErrorCodes.Forbidden, "需要管理员权限")) { StatusCode = 403 };
        }
    }
}

public static class HttpContextExtensions
{
    public static CurrentUser User(this HttpContext ctx) =>
        ctx.Items["CurrentUser"] as CurrentUser ?? new CurrentUser();
    /// <summary>
    /// 取调用方 IP 用于审计留痕。Program.cs 已启用 UseForwardedHeaders(XForwardedFor),
    /// 因此 RemoteIpAddress 在代理后也是真实客户端地址; 不要手工解析 X-Forwarded-For,
    /// 那会让调用方可以伪造头来嫁祸他人。
    /// </summary>
    public static string? ClientIp(this HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
}

public class TokenAuthHandler : AuthenticationHandler<TokenAuthOptions>
{
    private readonly IDbConnectionFactory _db;

    public TokenAuthHandler(IOptionsMonitor<TokenAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder,
        IDbConnectionFactory db)
        : base(options, logger, encoder) => _db = db;

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // 命中匿名端点直接跳过
        var endpoint = Context.GetEndpoint();
        var requireAuth = endpoint?.Metadata.GetMetadata<RequireAuthAttribute>() != null;
        var requireAdmin = endpoint?.Metadata.GetMetadata<RequireAdminAttribute>() != null;

        string? token = null;
        var authHeader = Request.Headers.Authorization.ToString();
        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            token = authHeader["Bearer ".Length..].Trim();
        // 小程序端也支持自定义头
        if (string.IsNullOrEmpty(token))
            token = Request.Headers["X-Token"].ToString();

        if (string.IsNullOrEmpty(token))
            return requireAuth || requireAdmin
                ? AuthenticateResult.Fail("未登录")
                : AuthenticateResult.NoResult();

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(Options.SecretKey!);
            var principal = handler.ValidateToken(token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = "AgroMarket",
                ValidateAudience = true,
                ValidAudience = "AgroMarketWeb",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);
            var id = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (id == null) return AuthenticateResult.Fail("Token 无效");

            // 每次请求核对 token_version 与账号状态: 这是 JWT 唯一的吊销手段。
            // 没有这一步, 退出登录只清了客户端本地副本, 7 天内旧 token 仍可继续使用;
            // 冻结/注销账号同理 —— 之前这里完全不看库, 冻结后 token 依然通行。
            var claimVersion = principal.FindFirst(Security.TokenVersionClaim)?.Value;
            if (!int.TryParse(claimVersion, out var tokenVersion))
                return AuthenticateResult.Fail("登录已失效, 请重新登录");
            using (var conn = _db.Create())
            {
                var row = await conn.QueryFirstOrDefaultAsync<(int Version, string Status)?>(
                    "SELECT token_version Version, status Status FROM sys_user WHERE id=@id", new { id = long.Parse(id) });
                if (row is null || row.Value.Status != "ACTIVE")
                    return AuthenticateResult.Fail("账号不可用, 请重新登录");
                if (row.Value.Version != tokenVersion)
                    return AuthenticateResult.Fail("登录已失效, 请重新登录");
            }
            var user = new CurrentUser
            {
                Id = long.Parse(id),
                Phone = principal.FindFirst(ClaimTypes.MobilePhone)?.Value ?? "",
                Role = principal.FindFirst(ClaimTypes.Role)?.Value ?? "",
            };
            Context.Items["CurrentUser"] = user;
            return AuthenticateResult.Success(new AuthenticationTicket(principal, "Token"));
        }
        catch (Microsoft.IdentityModel.Tokens.SecurityTokenExpiredException)
        {
            return AuthenticateResult.Fail("登录已过期");
        }
        catch
        {
            return AuthenticateResult.Fail("Token 无效");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(ApiResponse<object>.Fail(ErrorCodes.Unauthorized, "请先登录"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Response.WriteAsync(json);
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 403;
        Response.ContentType = "application/json; charset=utf-8";
        var json = JsonSerializer.Serialize(ApiResponse<object>.Fail(ErrorCodes.Forbidden, "无权访问"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return Response.WriteAsync(json);
    }
}
