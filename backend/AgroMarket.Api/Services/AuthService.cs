using System.Data;
using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

public class AuthService
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AuthService> _logger;

    public AuthService(IDbConnectionFactory db, IConfiguration cfg, ILogger<AuthService> logger)
    {
        _db = db; _cfg = cfg; _logger = logger;
    }

    public class UserRow
    {
        public long Id { get; set; }
        public string Phone { get; set; } = "";
        public string? PasswordHash { get; set; }
        public string Nickname { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public string Role { get; set; } = "";
        public long? HomeRegionId { get; set; }
        public bool ContactDisplay { get; set; }
        public string? ContactPhone { get; set; }
        public string Status { get; set; } = "";
    }

    public async Task<UserRow?> GetUserByPhoneAsync(string phone)
    {
        using var conn = _db.Create();
        return await conn.QueryFirstOrDefaultAsync<UserRow>(
            @"SELECT id, phone, password_hash PasswordHash, nickname, avatar_url AvatarUrl, role,
                     home_region_id HomeRegionId, contact_display ContactDisplay, contact_phone ContactPhone, status
              FROM sys_user WHERE phone=@p", new { p = phone });
    }

    public async Task<UserRow?> GetUserByIdAsync(long id)
    {
        using var conn = _db.Create();
        return await conn.QueryFirstOrDefaultAsync<UserRow>(
            @"SELECT id, phone, password_hash PasswordHash, nickname, avatar_url AvatarUrl, role,
                     home_region_id HomeRegionId, contact_display ContactDisplay, contact_phone ContactPhone, status
              FROM sys_user WHERE id=@id", new { id });
    }

    public async Task SendSmsCodeAsync(string phone)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^1[3-9]\d{9}$"))
            throw new BizException("手机号格式不正确");
        var code = Random.Shared.Next(100000, 999999).ToString();
        using var conn = _db.Create();
        await conn.ExecuteAsync(
            @"INSERT INTO sys_sms_code (phone, code, purpose, expired_at) VALUES (@p, @c, 'LOGIN', DATE_ADD(NOW(3), INTERVAL 10 MINUTE))",
            new { p = phone, c = code });
        var devMode = _cfg.GetValue<bool>("Sms:DevMode");
        if (devMode)
        {
            _logger.LogInformation("[DEV-SMS] 验证码已生成 phone={Phone} code={Code}", phone, code);
            Console.WriteLine($"[DEV-SMS] {phone} 的登录验证码: {code}");
        }
        else
        {
            // 生产环境接入真实短信服务商, 此处预留
            throw new BizException("未配置真实短信服务商, 请在 appsettings 中开启 Sms:DevMode", ErrorCodes.ServerError);
        }
    }

    public async Task VerifySmsCodeAsync(string phone, string code)
    {
        using var conn = _db.Create();
        var row = await conn.QueryFirstOrDefaultAsync<(long Id, string Code)>(
            @"SELECT id, code FROM sys_sms_code
              WHERE phone=@p AND purpose='LOGIN' AND is_used=0 AND expired_at > NOW(3)
              ORDER BY id DESC LIMIT 1", new { p = phone });
        if (row.Id == 0 || row.Code != code)
            throw new BizException("验证码错误或已过期");
        await conn.ExecuteAsync("UPDATE sys_sms_code SET is_used=1 WHERE id=@id", new { row.Id });
    }

    public async Task<Dtos.AuthResponse> LoginByCodeAsync(string phone, string code)
    {
        await VerifySmsCodeAsync(phone, code);
        var user = await GetUserByPhoneAsync(phone)
            ?? throw new BizException("用户不存在, 请先注册", ErrorCodes.NotFound);
        return await IssueTokenAsync(user);
    }

    public async Task<Dtos.AuthResponse> LoginByPasswordAsync(string phone, string password)
    {
        var user = await GetUserByPhoneAsync(phone)
            ?? throw new BizException("用户不存在", ErrorCodes.NotFound);
        if (user.PasswordHash == null || !Security.VerifyPassword(password, user.PasswordHash))
            throw new BizException("手机号或密码错误", ErrorCodes.Unauthorized);
        return await IssueTokenAsync(user);
    }

    public async Task<Dtos.AuthResponse> RegisterAsync(Dtos.RegisterRequest req)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(req.Phone, @"^1[3-9]\d{9}$"))
            throw new BizException("手机号格式不正确");
        var allowedRoles = new[] { "FARMER", "BUYER", "COOP", "ENTERPRISE" };
        if (!allowedRoles.Contains(req.Role)) throw new BizException("注册角色不合法");
        await VerifySmsCodeAsync(req.Phone, req.Code);
        if (await GetUserByPhoneAsync(req.Phone) != null)
            throw new BizException("该手机号已注册, 请直接登录", ErrorCodes.Conflict);
        if (req.HomeRegionId != null) _ = await new RegionService(_db).ValidateRegionAsync(req.HomeRegionId, null);

        using var conn = _db.Create();
        var id = await conn.ExecuteScalarAsync<long>(
            @"INSERT INTO sys_user (phone, password_hash, nickname, role, home_region_id)
              VALUES (@p, @h, @n, @r, @hrid); SELECT LAST_INSERT_ID();",
            new { p = req.Phone, h = Security.HashPassword(req.Password), n = req.Nickname, r = req.Role, hrid = req.HomeRegionId });
        await conn.ExecuteAsync("INSERT INTO sys_user_role (user_id, role) VALUES (@u, @r)", new { u = id, r = req.Role });
        var user = await GetUserByIdAsync(id) ?? throw new BizException("注册失败", ErrorCodes.ServerError);
        return await IssueTokenAsync(user);
    }

    public async Task<Dtos.AuthResponse> IssueTokenAsync(UserRow user)
    {
        if (user.Status == "FROZEN") throw new BizException("账号已被冻结, 请联系管理员", ErrorCodes.Forbidden);
        using var conn = _db.Create();
        await conn.ExecuteAsync("UPDATE sys_user SET last_login_at = NOW(3) WHERE id=@id", new { user.Id });
        var token = Security.GenerateJwt(user.Id, user.Phone, user.Role, _cfg);
        return new Dtos.AuthResponse(user.Id, user.Phone, user.Nickname, user.Role, token,
            user.HomeRegionId, user.ContactDisplay, user.ContactPhone, user.AvatarUrl);
    }
}
