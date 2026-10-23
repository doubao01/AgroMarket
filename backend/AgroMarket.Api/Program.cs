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
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<CommunityService>();
builder.Services.AddSingleton<StorageService>();
// 站内聊天模块
builder.Services.AddScoped<AgroMarket.Api.Chat.Infrastructure.ChatRepository>();
builder.Services.AddScoped<AgroMarket.Api.Chat.Application.ChatService>();
builder.Services.AddScoped<AgroMarket.Api.Chat.Application.ChatTradeService>();
builder.Services.AddSingleton<AgroMarket.Api.Chat.Realtime.ChatConnectionManager>();
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

var app = builder.Build();

// 静态文件: 上传的图片
var uploadRoot = Path.Combine(app.Environment.ContentRootPath, builder.Configuration["Storage:LocalRoot"] ?? "uploads");
Directory.CreateDirectory(uploadRoot);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadRoot),
    RequestPath = "/uploads",
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
app.UseStatusCodePages(async context =>
{
    var response = context.HttpContext.Response;
    await response.WriteAsJsonAsync(ApiResponse<object>.Fail(response.StatusCode, "请求未能完成"));
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// 健康检查
app.MapGet("/health", () => Results.Text("ok"));

// 站内聊天 WebSocket 通道
app.Map("/ws/chat", AgroMarket.Api.Chat.Realtime.ChatRealtimeHub.HandleAsync);

app.Run();
