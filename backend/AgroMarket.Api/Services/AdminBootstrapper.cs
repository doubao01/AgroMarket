using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

/// <summary>
/// 首个管理员账号初始化。
///
/// 背景: 早期把 4 个公开口令(Abc123456)的测试账号写在 02_seed.sql 里, 容器首次启动即写入生产库,
/// 等于给线上留了一个人人知道口令的管理员后门。现在演示账号移到了 99_demo_seed.sql(默认不执行),
/// 生产环境改由部署方通过环境变量初始化唯一的管理员:
///
///   Bootstrap__AdminPhone=138xxxxxxxx
///   Bootstrap__AdminPassword=&lt;强口令&gt;
///
/// 规则:
///  - 库里已有任何 ADMIN 就什么都不做(可重复启动)
///  - 没配环境变量时只记一条警告, 不创建任何账号(宁可不建, 也不建默认口令)
///  - 口令强度不达标直接拒绝启动该步骤并记错误日志
/// </summary>
public class AdminBootstrapper
{
    private const int MinPasswordLength = 10;

    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AdminBootstrapper> _logger;

    public AdminBootstrapper(IDbConnectionFactory db, IConfiguration cfg, ILogger<AdminBootstrapper> logger)
    { _db = db; _cfg = cfg; _logger = logger; }

    public async Task RunAsync(CancellationToken ct = default)
    {
        using var conn = _db.Create();
        var hasAdmin = await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM sys_user WHERE role='ADMIN' AND status='ACTIVE')");
        if (hasAdmin) return;

        var phone = _cfg["Bootstrap:AdminPhone"]?.Trim();
        var password = _cfg["Bootstrap:AdminPassword"];
        if (string.IsNullOrWhiteSpace(phone) || string.IsNullOrEmpty(password))
        {
            _logger.LogWarning(
                "系统内没有任何管理员账号, 且未配置 Bootstrap__AdminPhone / Bootstrap__AdminPassword; " +
                "管理后台将无人可登录。生产部署请通过环境变量初始化管理员。");
            return;
        }

        if (password.Length < MinPasswordLength)
        {
            _logger.LogError("Bootstrap:AdminPassword 长度不足 {Min} 位, 拒绝创建管理员", MinPasswordLength);
            return;
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\d{6,20}$"))
        {
            _logger.LogError("Bootstrap:AdminPhone 格式非法, 拒绝创建管理员");
            return;
        }

        // 唯一键冲突说明并发启动的另一个实例已经建好了, 忽略
        try
        {
            var id = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO sys_user (phone, password_hash, nickname, role, contact_display, contact_phone, status)
                VALUES (@p, @h, '平台管理员', 'ADMIN', 0, NULL, 'ACTIVE');
                SELECT LAST_INSERT_ID();",
                new { p = phone, h = Security.HashPassword(password) });
            await conn.ExecuteAsync("INSERT IGNORE INTO sys_user_role (user_id, role) VALUES (@id, 'ADMIN')",
                new { id });
            _logger.LogInformation("已按环境变量创建初始管理员 {Phone} (id={Id})", phone, id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "创建初始管理员失败(可能已由其它实例创建)");
        }
    }
}
