using System.Text;
using AgroMarket.Api.Common;
using AgroMarket.Api.Data;
using AgroMarket.Api.Services;
using AgroMarket.Api.Services.Payments;
using AgroMarket.Api.Federation.Api;
using AgroMarket.Api.Federation.Application;
using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
var jwtSecret = builder.Configuration["Jwt:SecretKey"];
if (string.IsNullOrWhiteSpace(jwtSecret) || Encoding.UTF8.GetByteCount(jwtSecret) < 32 ||
    (!builder.Environment.IsDevelopment() && jwtSecret.StartsWith("dev-", StringComparison.OrdinalIgnoreCase)))
    throw new InvalidOperationException("Configure Jwt:SecretKey with a unique secret of at least 32 bytes.");
if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")))
    throw new InvalidOperationException("Configure ConnectionStrings:Default.");
if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Sms:DevMode"))
    throw new InvalidOperationException("Sms:DevMode is allowed only in Development.");
if (!builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Payment:MockEnabled"))
    throw new InvalidOperationException("Payment:MockEnabled is allowed only in Development.");
if (!builder.Environment.IsDevelopment()
    && builder.Configuration.GetValue<bool>("Ai:Enabled")
    && string.IsNullOrWhiteSpace(builder.Configuration["Ai:ApiKey"]))
    throw new InvalidOperationException("Ai:ApiKey is required when Ai:Enabled is true outside Development.");

// 联动配置校验: 一旦启用, 身份与共享密钥(或非对称密钥对模式)必须齐备
var fedOptions = builder.Configuration.GetSection(FederationOptions.SectionName).Get<FederationOptions>() ?? new FederationOptions();
if (!TokenModes.IsValid(fedOptions.TokenMode))
    throw new InvalidOperationException("Federation:TokenMode 只能是 SYMMETRIC/ASYMMETRIC。");
if (!FedRoles.IsValid(fedOptions.Role))
    throw new InvalidOperationException("Federation:Role 只能是 BUSINESS/AGGREGATOR。");
if (fedOptions.Enabled && !fedOptions.HasIdentity)
    throw new InvalidOperationException("Federation:Enabled 为 true 时必须配置 Federation:NodeCode/NodeName 及 Federation:SharedKey(>=32 字节, SYMMETRIC 模式) 或 Federation:TokenMode=ASYMMETRIC。");

// Services
builder.Services.AddSingleton<IDbConnectionFactory, MySqlConnectionFactory>();
builder.Services.AddScoped<RegionService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<SupplyService>();
builder.Services.AddScoped<DemandService>();
builder.Services.AddScoped<TradeService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<MessageService>();
builder.Services.AddScoped<FavoriteService>();
builder.Services.AddScoped<AddressService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<AgroMarket.Api.Services.AdminBootstrapper>();

// 短信渠道: Dev(仅开发) / Aliyun(生产) / 未配置(明确失败)。
// 早期实现在生产直接抛占位异常, 导致注册与短信登录 100% 不可用。
builder.Services.AddHttpClient();
if (builder.Configuration.GetValue<bool>("Sms:DevMode"))
    builder.Services.AddScoped<AgroMarket.Api.Services.Sms.ISmsSender, AgroMarket.Api.Services.Sms.DevSmsSender>();
else if (string.Equals(builder.Configuration["Sms:Provider"], "Aliyun", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<AgroMarket.Api.Services.Sms.ISmsSender>(sp =>
        new AgroMarket.Api.Services.Sms.AliyunSmsSender(
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(),
            sp.GetRequiredService<ILogger<AgroMarket.Api.Services.Sms.AliyunSmsSender>>()));
else
    builder.Services.AddScoped<AgroMarket.Api.Services.Sms.ISmsSender, AgroMarket.Api.Services.Sms.UnconfiguredSmsSender>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<CommunityService>();
builder.Services.AddSingleton<StorageService>();
// 站内聊天模块
builder.Services.AddScoped<AgroMarket.Api.Chat.Infrastructure.ChatRepository>();
builder.Services.AddScoped<AgroMarket.Api.Chat.Application.ChatService>();
builder.Services.AddScoped<AgroMarket.Api.Chat.Application.ChatTradeService>();
builder.Services.AddSingleton<AgroMarket.Api.Chat.Realtime.ChatConnectionManager>();
builder.Services.AddSingleton<AgroMarket.Api.Common.StripedLock>();
builder.Services.AddSingleton<AnonymousReadThrottle>();
builder.Services.AddScoped<AgroMarket.Api.Chat.Realtime.IRealtimeNotifier, AgroMarket.Api.Chat.Realtime.ChatRealtimeNotifier>();
// 支付渠道(可插拔): 真实渠道未配置时不可用, 不伪造支付
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
builder.Services.AddSingleton<IPaymentProvider, WechatPayProvider>();
builder.Services.AddSingleton<IPaymentProvider, AlipayProvider>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddHttpContextAccessor();
// AI 能力 (OpenAI 兼容 LLM): 未配置时相关接口返回 501, 不影响业务
builder.Services.Configure<AgroMarket.Api.Ai.Infrastructure.AiOptions>(
    builder.Configuration.GetSection(AgroMarket.Api.Ai.Infrastructure.AiOptions.SectionName));
builder.Services.AddHttpClient<AgroMarket.Api.Ai.Infrastructure.ILlmClient, AgroMarket.Api.Ai.Infrastructure.OpenAiCompatClient>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Infrastructure.AiChatRepository>();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<AgroMarket.Api.Ai.Tools.IAiTool, AgroMarket.Api.Ai.Tools.PlatformStatsTool>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Tools.IAiTool, AgroMarket.Api.Ai.Tools.TradeTrendTool>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Tools.IAiTool, AgroMarket.Api.Ai.Tools.SearchSuppliesTool>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Tools.IAiTool, AgroMarket.Api.Ai.Tools.SearchDemandsTool>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Tools.IAiTool, AgroMarket.Api.Ai.Tools.MyOrdersTool>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Tools.IAiTool, AgroMarket.Api.Ai.Tools.RegionDistributionTool>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Tools.AiToolRegistry>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Application.AiRateLimiter>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Application.AiChatService>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Application.AiAssistService>();
builder.Services.AddScoped<AgroMarket.Api.Ai.Application.AiInsightService>();
// 日志凭据兜底: ASP.NET 默认的 Console 输出走这里, 统一把 token/password 等打码
builder.Logging.AddConsoleFormatter<AgroMarket.Api.Common.SecretScrubbingConsoleFormatter, Microsoft.Extensions.Logging.Console.ConsoleFormatterOptions>();
builder.Logging.AddConsole(o => o.FormatterName = AgroMarket.Api.Common.SecretScrubbingConsoleFormatter.FormatterName);
// 请求行日志含完整 URL(含 query), 是 token 进日志的主通道; 降到 Warning 后不再逐请求打印。
// 需要排查请求时临时把该类别调回 Information 即可。
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);


// 节点联动 (多节点数据上报 / 同步): 未配置 SharedKey 时节点接口返回 401/400, 不影响业务
builder.Services.Configure<FederationOptions>(builder.Configuration.GetSection(FederationOptions.SectionName));
builder.Services.AddSingleton<FederationTokenService>();
builder.Services.AddSingleton<FederationKeyStore>();
builder.Services.AddSingleton<FederationSettings>();
builder.Services.AddScoped<FederationRepository>();
builder.Services.AddScoped<FederationSnapshotBuilder>();
builder.Services.AddScoped<FederationService>();
builder.Services.AddScoped<FederationAdminService>();
builder.Services.AddHttpClient<FedHttpClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHostedService<FederationSyncWorker>();

// 消息推送与报警: WeChat 未配置时自动降级, 不影响业务
builder.Services.AddHttpClient("wechat", c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddSingleton<AgroMarket.Api.Common.LoginThrottle>();
builder.Services.AddSingleton<AgroMarket.Api.Notifications.WeChatPushService>();
builder.Services.AddSingleton<AgroMarket.Api.Notifications.NotificationPushService>();
builder.Services.AddSingleton<AgroMarket.Api.Notifications.AlertService>();
builder.Services.AddHostedService<AgroMarket.Api.Notifications.AlertScanWorker>();
// 订单超时关闭: 下单即扣库存, 没有这个任务弃单会永久占住库存。
// AddHostedService<T>() 只注册 IHostedService, 不注册 T 本身; 管理端要手动触发,
// 所以先以具体类型注册单例, 再把同一个实例挂成 HostedService。
builder.Services.AddSingleton<AgroMarket.Api.Notifications.OrderTimeoutWorker>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AgroMarket.Api.Notifications.OrderTimeoutWorker>());

// Authentication (custom lightweight JWT handler)
builder.Services.AddAuthentication("Token")
    .AddScheme<TokenAuthOptions, TokenAuthHandler>("Token", o =>
        o.SecretKey = builder.Configuration["Jwt:SecretKey"]);
builder.Services.AddAuthorization();

// Controllers & Swagger
builder.Services.AddControllers(o =>
    {
        o.Filters.Add<RequireAuthFilter>();
        o.Filters.Add<RequireNodeFilter>();
    })
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DictionaryKeyPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(o =>
    o.InvalidModelStateResponseFactory = _ => new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(
        ApiResponse<object>.Fail(ErrorCodes.BadRequest, "请求参数格式不正确")));
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "AgroMarket 息县农产品交易平台 API",
        Version = "v1",
        Description = "县域农产品自由交易平台后端接口文档",
    });
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "粘贴登录返回的 Token",
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// CORS (H5 联调)
var corsOrigins = builder.Configuration.GetSection("CorsOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(o => o.AddPolicy("web", p => p
    .WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod()));


// 请求体上限: 上传接口按声明大小限制, 但 multipart 的"声明"在解析阶段才生效,
// 若不在这里兜底, 一个超大请求会先把内存/磁盘写满再被业务层拒绝。
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 12L * 1024 * 1024;
    o.ValueLengthLimit = 1024 * 1024;
    o.MultipartHeadersLengthLimit = 16 * 1024;
});
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 14L * 1024 * 1024);

var app = builder.Build();

// 静态文件: 上传的图片
var uploadRoot = Path.Combine(app.Environment.ContentRootPath, builder.Configuration["Storage:LocalRoot"] ?? "uploads");
Directory.CreateDirectory(uploadRoot);
app.UseStaticFiles(new StaticFileOptions
{
    // 受控 provider: .tmp(分片会话) 与 .private(证件材料) 一律判不存在,
    // 否则静态服务会把分片会话的 meta.json 和商家证件按路径直接吐出去。
    FileProvider = new AgroMarket.Api.Common.UploadsFileProvider(uploadRoot),
    RequestPath = "/uploads",
    ContentTypeProvider = new UploadContentTypeProvider(),
    // 只允许按扩展名映射的类型被返回, 且补 nosniff:
    // 没有 nosniff 时, 浏览器可能把 image/* 嗅探成 text/html 并按页面渲染,
    // 于是一个"JPEG 头 + HTML 正文"的 polyglot 文件就能在站点同源下执行脚本。
    OnPrepareResponse = ctx =>
    {
        var h = ctx.Context.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Content-Security-Policy"] = "default-src 'none'; sandbox";
        h["Cache-Control"] = "public, max-age=86400";
    },
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
app.UseCors("web");
app.UseWebSockets();
app.UseMiddleware<BizExceptionMiddleware>();
app.UseMiddleware<AgroMarket.Api.Federation.Api.AggregatorReadOnlyMiddleware>();

// 匿名公开读的频次限制: 供需列表允许未登录浏览, 但不能成为无限翻页抓取的数据源
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    var isPublicRead = HttpMethods.IsGet(ctx.Request.Method)
        && (path.StartsWith("/api/supplies", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/api/demands", StringComparison.OrdinalIgnoreCase));
    var hasToken = ctx.Request.Headers.Authorization.Count > 0 || ctx.Request.Headers["X-Token"].Count > 0;
    if (isPublicRead && !hasToken
        && ctx.RequestServices.GetRequiredService<AnonymousReadThrottle>().ShouldReject(ctx.Connection.RemoteIpAddress?.ToString()))
    {
        ctx.Response.StatusCode = ErrorCodes.TooManyRequests;
        await ctx.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(ErrorCodes.TooManyRequests, "访问过于频繁, 请稍后再试或登录后浏览"));
        return;
    }
    await next();
});
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    await response.WriteAsJsonAsync(ApiResponse<object>.Fail(response.StatusCode, "请求未能完成"));
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// 首个管理员初始化: 库里没有 ADMIN 且配了 Bootstrap__AdminPhone/Password 时创建。
// 测试账号已从 02_seed.sql 移到 99_demo_seed.sql(默认不执行), 生产不再有公开口令账号。
using (var scope = app.Services.CreateScope())
{
    try { await scope.ServiceProvider.GetRequiredService<AgroMarket.Api.Services.AdminBootstrapper>().RunAsync(); }
    catch (Exception ex) { app.Logger.LogError(ex, "admin bootstrap failed"); }
}

// 健康检查
app.MapGet("/health", () => Results.Text("ok"));

// 站内聊天 WebSocket 通道
app.Map("/ws/chat", AgroMarket.Api.Chat.Realtime.ChatRealtimeHub.HandleAsync);

app.Run();
