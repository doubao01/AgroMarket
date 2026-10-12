using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Dtos;
using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgroMarket.Api.Federation.Application;

/// <summary>
/// 节点令牌服务: 用全联邦共享密钥 (HS256) 签发/校验节点身份令牌。
/// 与用户登录 JWT 完全隔离 (独立 issuer/audience), 不代表任何自然人。
/// </summary>
public class FederationTokenService
{
    public const string Issuer = "AgroMarketFederation";
    public const string Audience = "AgroMarketFederation";

    public const string ClaimNodeCode = "node_code";
    public const string ClaimNodeName = "node_name";
    public const string ClaimNodeLevel = "node_level";
    public const string ClaimRegionCode = "region_code";

    private readonly FederationOptions _opt;
    private readonly ILogger<FederationTokenService> _logger;

    public FederationTokenService(IOptions<FederationOptions> opt, ILogger<FederationTokenService> logger)
    {
        _opt = opt.Value;
        _logger = logger;
    }

    public bool IsConfigured => _opt.HasIdentity && Encoding.UTF8.GetByteCount(_opt.SharedKey) >= 32;

    /// <summary>签发节点令牌。days&lt;=0 时使用配置的 TokenDays。</summary>
    public string Issue(string nodeCode, string nodeName, string level, string? regionCode, int days = 0)
    {
        if (!IsConfigured) throw new BizException("联动未配置: 缺少 Federation:SharedKey (>=32 字节)", ErrorCodes.BadRequest);
        if (string.IsNullOrWhiteSpace(nodeCode)) throw new BizException("节点编码不能为空", ErrorCodes.BadRequest);
        var span = TimeSpan.FromDays(days > 0 ? days : _opt.TokenDays);

        var claims = new List<Claim>
        {
            new(ClaimNodeCode, nodeCode),
            new(ClaimNodeName, nodeName ?? ""),
            new(ClaimNodeLevel, string.IsNullOrWhiteSpace(level) ? _opt.Level : level),
        };
        if (!string.IsNullOrWhiteSpace(regionCode)) claims.Add(new Claim(ClaimRegionCode, regionCode!));

        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SharedKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience, claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.Add(span),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>签发本节点自身令牌 (用于调用对端)。</summary>
    public string IssueSelf(int days = 0) =>
        Issue(_opt.NodeCode, _opt.NodeName, _opt.Level, _opt.RegionCode, days);

    /// <summary>校验节点令牌, 失败返回 null。</summary>
    public NodeIdentity? Validate(string? token)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.SharedKey)),
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);
            var code = principal.FindFirst(ClaimNodeCode)?.Value;
            if (string.IsNullOrWhiteSpace(code)) return null;
            return new NodeIdentity(
                code!,
                principal.FindFirst(ClaimNodeName)?.Value ?? code!,
                principal.FindFirst(ClaimNodeLevel)?.Value ?? FedLevels.County,
                principal.FindFirst(ClaimRegionCode)?.Value);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "节点令牌校验失败");
            return null;
        }
    }

    /// <summary>从请求头解析节点身份 (Authorization: Bearer 或 X-Node-Token)。</summary>
    public NodeIdentity? ParseFromHeader(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        var raw = request.Headers.Authorization.ToString();
        string? token = null;
        if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = raw["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token)) token = request.Headers["X-Node-Token"].ToString();
        return Validate(token);
    }
}
