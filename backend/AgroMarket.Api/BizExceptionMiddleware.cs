namespace AgroMarket.Api.Common;

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgroMarket.Api.Common;

public class BizExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BizExceptionMiddleware> _logger;

    public BizExceptionMiddleware(RequestDelegate next, ILogger<BizExceptionMiddleware> logger)
    { _next = next; _logger = logger; }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx);
        }
        catch (BizException ex)
        {
            await WriteJson(ctx, ApiResponse<object>.Fail(ex.Code, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "未处理异常 {Path}", ctx.Request.Path);
            await WriteJson(ctx, ApiResponse<object>.Fail(ErrorCodes.ServerError, "服务器繁忙, 请稍后重试"));
        }
    }

    private static async Task WriteJson(HttpContext ctx, ApiResponse<object> resp)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.StatusCode = resp.Code;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(resp, JsonOpts), Encoding.UTF8);
    }
}
