using System.Data;
using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

public class AuthService
{
    private readonly IDbConnectionFactory _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<AuthService> _logger;
    private readonly LoginThrottle _throttle;
    private readonly Sms.ISmsSender _sms;
    private readonly SmsThrottle _smsThrottle;

    public AuthService(IDbConnectionFactory db, IConfiguration cfg, ILogger<AuthService> logger,
        LoginThrottle throttle, Sms.ISmsSender sms, SmsThrottle smsThrottle)
    {
        _db = db; _cfg = cfg; _logger = logger; _throttle = throttle; _sms = sms; _smsThrottle = smsThrottle;
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
        public int TokenVersion { get; set; }
    }

    public async Task<UserRow?> GetUserByPhoneAsync(string phone)
    {
        using var conn = _db.Create();
        return await conn.QueryFirstOrDefaultAsync<UserRow>(
            @"SELECT id, phone, password_hash PasswordHash, nickname, avatar_url AvatarUrl, role,
                     home_region_id HomeRegionId, contact_display ContactDisplay, contact_phone ContactPhone, token_version TokenVersion, status
              FROM sys_user WHERE phone=@p", new { p = phone });
    }

    public async Task<UserRow?> GetUserByIdAsync(long id)
    {
        using var conn = _db.Create();
        return await conn.QueryFirstOrDefaultAsync<UserRow>(
            @"SELECT id, phone, password_hash PasswordHash, nickname, avatar_url AvatarUrl, role,
                     home_region_id HomeRegionId, contact_display ContactDisplay, contact_phone ContactPhone, token_version TokenVersion, status
              FROM sys_user WHERE id=@id", new { id });
    }

    public async Task SendSmsCodeAsync(string phone, string? ip = null)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^1[3-9]\d{9}$"))
            throw new BizException("手机号格式不正确");

        using var conn = _db.Create();

        // 按 IP 的总量限制: 同一手机号的分钟/小时限制挡不住"换号继续打"
        _smsThrottle.AcquireSendSlot(ip);

        // 发短信要真花钱, 且是"按号码打击他人"的现成工具: 不限频就能被脚本用来轰炸任意手机号,
        // 顺带把短信费用打上去。60 秒内只允许一条, 1 小时内最多 5 条。
        var recent = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM sys_sms_code
              WHERE phone=@p AND created_at > DATE_SUB(NOW(3), INTERVAL 60 SECOND)", new { p = phone });
        if (recent > 0)
            throw new BizException("验证码发送过于频繁, 请 60 秒后再试", ErrorCodes.TooManyRequests);
        var hourly = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM sys_sms_code
              WHERE phone=@p AND created_at > DATE_SUB(NOW(3), INTERVAL 1 HOUR)", new { p = phone });
        if (hourly >= 5)
            throw new BizException("该手机号今日验证码次数过多, 请 1 小时后再试", ErrorCodes.TooManyRequests);

        var code = Random.Shared.Next(100000, 999999).ToString();
        var digest = SmsCodeDigest(code);
        await conn.ExecuteAsync(
            @"INSERT INTO sys_sms_code (phone, code, purpose, expired_at) VALUES (@p, @c, 'LOGIN', DATE_ADD(NOW(3), INTERVAL 10 MINUTE))",
            new { p = phone, c = digest });
        // 渠道可插拔: 开发用 Dev 打印, 生产用阿里云短信, 未配置则明确失败。
        // 早期实现是"生产直接抛 ServerError 占位", 等于注册与短信登录在生产 100% 不可用。
        var sent = await _sms.SendAsync(phone, code);
        if (!sent.Ok)
        {
            // 发送失败就把这条验证码作废, 避免留下"用户没收到但可用"的记录
            await conn.ExecuteAsync(
                "UPDATE sys_sms_code SET is_used=1 WHERE phone=@p AND code=@c AND is_used=0",
                new { p = phone, c = digest });
            throw new BizException(sent.Error ?? "短信发送失败", ErrorCodes.ServerError);
        }
        _logger.LogInformation("短信验证码已发送 phone={Phone} ip={Ip}", MaskPhone(phone), ip ?? "-");
    }

    public async Task VerifySmsCodeAsync(string phone, string code)
    {
        // 6 位数字只有 100 万种可能, 而验证码有 10 分钟有效期:
        // 不限次数的话脚本能在有效期内跑完相当比例的枚举空间, 猜中即等于拿到登录凭据。
        _smsThrottle.EnsureCanVerify(phone);

        using var conn = _db.Create();
        var row = await conn.QueryFirstOrDefaultAsync<(long Id, string Code)>(
            @"SELECT id, code FROM sys_sms_code
              WHERE phone=@p AND purpose='LOGIN' AND is_used=0 AND expired_at > NOW(3)
              ORDER BY id DESC LIMIT 1", new { p = phone });
        if (row.Id == 0 || !DigestEquals(row.Code, SmsCodeDigest(code)))
        {
            _smsThrottle.OnVerifyFailed(phone);
            throw new BizException("验证码错误或已过期");
        }
        _smsThrottle.OnVerifySucceeded(phone);
        await conn.ExecuteAsync("UPDATE sys_sms_code SET is_used=1 WHERE id=@id", new { row.Id });
    }

    /// <summary>
    /// 验证码摘要: 用服务端密钥做 HMAC 而不是裸哈希。
    /// 6 位数字只有 100 万种可能, 裸 SHA-256 在一台笔记本上是秒级穷举, 拖库即可还原;
    /// 加密钥后 dump 出的摘要离开 Jwt:SecretKey 无法离线破解。
    /// </summary>
    private string SmsCodeDigest(string code)
    {
        var key = System.Text.Encoding.UTF8.GetBytes(_cfg["Jwt:SecretKey"] ?? "agromarket-sms-code-digest");
        using var h = new System.Security.Cryptography.HMACSHA256(key);
        return Convert.ToHexString(h.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"sms:{code}")));
    }

    /// <summary>定长时间比较, 避免按字节提前返回泄漏摘要前缀。</summary>
    private static bool DigestEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(a), System.Text.Encoding.ASCII.GetBytes(b));

    private static string MaskPhone(string phone) =>
        phone.Length == 11 ? phone[..3] + "****" + phone[^4..] : "***";

    public async Task<Dtos.AuthResponse> LoginByCodeAsync(string phone, string code)
    {
        await VerifySmsCodeAsync(phone, code);
        var user = await GetUserByPhoneAsync(phone)
            ?? throw new BizException("用户不存在, 请先注册", ErrorCodes.NotFound);
        return await IssueTokenAsync(user);
    }

    public async Task<Dtos.AuthResponse> LoginByPasswordAsync(string phone, string password)
    {
        _throttle.EnsureAllowed(phone);
        var user = await GetUserByPhoneAsync(phone);
        if (user == null || user.PasswordHash == null || !Security.VerifyPassword(password, user.PasswordHash))
        {
            _throttle.OnFailure(phone);
            // 统一话术: 不再区分"用户不存在"与"密码错误", 否则可用响应差异枚举已注册手机号
            throw new BizException("手机号或密码错误", ErrorCodes.Unauthorized);
        }
        _throttle.OnSuccess(phone);
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

    /// <summary>
    /// 让该用户此前签发的所有 token 立即失效(退出登录/改密码后调用)。
    /// 做的是版本自增而不是黑名单: 无需存储与清理, 代价是该用户在所有设备上都要重新登录 ——
    /// 对农户/经纪人这种基本单端使用的场景可以接受, 换来的是"退出就是真的退出"。
    /// </summary>
    public async Task RevokeTokensAsync(long userId)
    {
        using var conn = _db.Create();
        await conn.ExecuteAsync("UPDATE sys_user SET token_version = token_version + 1 WHERE id=@id", new { id = userId });
    }

    public async Task<Dtos.AuthResponse> IssueTokenAsync(UserRow user)
    {
        if (user.Status == "FROZEN") throw new BizException("账号已被冻结, 请联系管理员", ErrorCodes.Forbidden);
        using var conn = _db.Create();
        await conn.ExecuteAsync("UPDATE sys_user SET last_login_at = NOW(3) WHERE id=@id", new { user.Id });
        var token = Security.GenerateJwt(user.Id, user.Phone, user.Role, user.TokenVersion, _cfg);
        return new Dtos.AuthResponse(user.Id, user.Phone, user.Nickname, user.Role, token,
            user.HomeRegionId, user.ContactDisplay, user.ContactPhone, user.AvatarUrl);
    }
}
