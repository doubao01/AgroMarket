using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace AgroMarket.Api.Common;

public static class Security
{
    /// <summary>JWT 里携带 token_version 的 claim 名, 与 sys_user.token_version 比对。</summary>
    public const string TokenVersionClaim = "tv";
    public static string HashPassword(string password, string? saltB64 = null)
    {
        saltB64 ??= Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var salt = Convert.FromBase64String(saltB64);
        const int iterations = 10000;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${iterations}${saltB64}${Convert.ToBase64String(hash)}";
    }

    public static bool VerifyPassword(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2") return false;
        var computed = HashPassword(password, parts[2]);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computed), Encoding.UTF8.GetBytes(stored));
    }

    public static string GenerateJwt(long userId, string phone, string role, int tokenVersion, IConfiguration cfg)
    {
        var section = cfg.GetSection("Jwt");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(section["SecretKey"]!));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.MobilePhone, phone),
            new(ClaimTypes.Role, role),
            // 吊销用: 与 sys_user.token_version 比对, 退出登录/改密码后自增, 旧 token 立即失效
            new(TokenVersionClaim, tokenVersion.ToString()),
        };
        var token = new JwtSecurityToken(
            issuer: section["Issuer"],
            audience: section["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(double.Parse(section["ExpireHours"] ?? "168")),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
