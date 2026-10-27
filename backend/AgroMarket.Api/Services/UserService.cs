using System.Data;
using AgroMarket.Api.Common;
using Dapper;

namespace AgroMarket.Api.Services;

public class UserService
{
    private readonly IDbConnectionFactory _db;
    private readonly StorageService _storage;
    public UserService(IDbConnectionFactory db, StorageService storage)
    { _db = db; _storage = storage; }

    public async Task<Dtos.AuthResponse> UpdateProfileAsync(long userId, Dtos.UpdateProfileRequest req)
    {
        if (req.Role != null && req.Role is not ("FARMER" or "BUYER" or "COOP" or "ENTERPRISE"))
            throw new BizException("经营类型不合法");
        // 头像地址是前端直接回传的字符串, 不校验就能把任意外链(甚至 javascript:)写进资料页,
        // 由平台原样渲染给其他用户; 因此只接受本服务 /uploads/ 下的真实文件地址。
        if (!string.IsNullOrWhiteSpace(req.AvatarUrl) && !StorageService.IsOwnUploadUrl(req.AvatarUrl))
            throw new BizException("头像地址无效, 请重新上传");
        if (req.HomeRegionId != null)
        {
            var regionService = new RegionService(_db);
            _ = await regionService.ValidateRegionAsync(req.HomeRegionId, null);
        }
        using var conn = _db.Create();
        await conn.ExecuteAsync(@"
            UPDATE sys_user SET
                nickname = COALESCE(@n, nickname),
                avatar_url = COALESCE(@a, avatar_url),
                home_region_id = COALESCE(@hr, home_region_id),
                contact_display = COALESCE(@cd, contact_display),
                contact_phone = COALESCE(@cp, contact_phone),
                role = COALESCE(@r, role)
            WHERE id = @id",
            new { n = req.Nickname, a = req.AvatarUrl, hr = req.HomeRegionId, cd = req.ContactDisplay, cp = req.ContactPhone, r = req.Role, id = userId });
        if (req.Role != null)
            await conn.ExecuteAsync("INSERT IGNORE INTO sys_user_role (user_id, role) VALUES (@u, @r)", new { u = userId, r = req.Role });
        return await GetMeAsync(userId);
    }

    public async Task<Dtos.AuthResponse> GetMeAsync(long userId)
    {
        using var conn = _db.Create();
        var u = await conn.QueryFirstOrDefaultAsync<(long Id, string Phone, string Nickname, string? AvatarUrl, string Role,
            long? HomeRegionId, bool ContactDisplay, string? ContactPhone)>(
            @"SELECT id, phone, nickname, avatar_url, role, home_region_id, contact_display, contact_phone
              FROM sys_user WHERE id=@id", new { id = userId });
        if (u == default) throw new BizException("用户不存在", ErrorCodes.NotFound);
        return new Dtos.AuthResponse(u.Id, u.Phone, u.Nickname, u.Role, "", u.HomeRegionId, u.ContactDisplay, u.ContactPhone, u.AvatarUrl);
    }

    public async Task ChangePasswordAsync(long userId, string oldPwd, string newPwd)
    {
        if (newPwd.Length < 6) throw new BizException("新密码至少 6 位");
        using var conn = _db.Create();
        var hash = await conn.ExecuteScalarAsync<string?>("SELECT password_hash FROM sys_user WHERE id=@id", new { id = userId });
        if (hash != null && !Security.VerifyPassword(oldPwd, hash))
            throw new BizException("原密码错误", ErrorCodes.Unauthorized);
        await conn.ExecuteAsync("UPDATE sys_user SET password_hash=@h WHERE id=@id",
            new { h = Security.HashPassword(newPwd), id = userId });
        // 改密码同时作废旧 token: 否则改密码踢下线不成立, 攻击者手上的 token 仍有效
        await conn.ExecuteAsync("UPDATE sys_user SET token_version = token_version + 1 WHERE id=@id", new { id = userId });
    }

    // ---------- 商家认证 ----------
    public async Task<Dtos.MerchantProfileDto> SubmitAuthAsync(long userId, Dtos.MerchantProfileRequest req)
    {
        if (req.SubjectType is not ("PERSONAL" or "COOP" or "ENTERPRISE"))
            throw new BizException("主体类型不合法");
        if (string.IsNullOrWhiteSpace(req.SubjectName)) throw new BizException("请填写主体名称");
        if (!string.IsNullOrWhiteSpace(req.LicenseUrl) && !StorageService.IsOwnUploadUrl(req.LicenseUrl))
            throw new BizException("证件照地址无效, 请重新上传");
        using var conn = _db.Create();
        var existing = await conn.QueryFirstOrDefaultAsync<(long Id, string Status)>(
            "SELECT id, audit_status FROM merchant_profile WHERE user_id=@u ORDER BY id DESC LIMIT 1", new { u = userId });
        long id;
        if (existing.Id != 0 && existing.Status == "PENDING")
            throw new BizException("已有认证申请审核中, 请等待管理员审核", ErrorCodes.Conflict);
        if (existing.Id != 0 && existing.Status == "REJECTED")
        {
            id = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO merchant_profile (user_id, subject_type, subject_name, id_number, license_url, audit_status)
                VALUES (@u, @st, @sn, @idn, @lic, 'PENDING'); SELECT LAST_INSERT_ID();",
                new { u = userId, st = req.SubjectType, sn = req.SubjectName, idn = req.IdNumber, lic = req.LicenseUrl });
        }
        else
        {
            id = await conn.ExecuteScalarAsync<long>(@"
                INSERT INTO merchant_profile (user_id, subject_type, subject_name, id_number, license_url, audit_status)
                VALUES (@u, @st, @sn, @idn, @lic, 'PENDING'); SELECT LAST_INSERT_ID();",
                new { u = userId, st = req.SubjectType, sn = req.SubjectName, idn = req.IdNumber, lic = req.LicenseUrl });
        }
        return (await GetMyAuthAsync(userId, id))!;
    }

    public async Task<Dtos.MerchantProfileDto?> GetMyAuthAsync(long userId, long? id = null)
    {
        using var conn = _db.Create();
        const string sql = @"SELECT id Id, user_id UserId, subject_type SubjectType, subject_name SubjectName,
                id_number IdNumber, license_url LicenseUrl, audit_status AuditStatus, audit_remark AuditRemark, created_at CreatedAt
            FROM merchant_profile WHERE user_id=@u {0} ORDER BY id DESC LIMIT 1";
        var row = id == null
            ? await conn.QueryFirstOrDefaultAsync<Dtos.MerchantProfileDto>(string.Format(sql, ""), new { u = userId })
            : await conn.QueryFirstOrDefaultAsync<Dtos.MerchantProfileDto>(string.Format(sql, "AND id=@id"), new { u = userId, id });
        if (row == null) return null;
        // 证件材料存的是私有路径, 只在出参时换成 10 分钟有效的签名地址; 前端仍直接用作图片地址
        return row with { LicenseUrl = SignLicense(row.LicenseUrl), IdNumber = Mask.IdNumber(row.IdNumber) };
    }

    /// <summary>私有证件地址签名; 非私有地址原样返回。</summary>
    private string? SignLicense(string? url) => string.IsNullOrEmpty(url) ? url : _storage.SignPrivateUrl(url);
}
