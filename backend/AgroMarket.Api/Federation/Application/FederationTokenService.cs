using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Dtos;
using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AgroMarket.Api.Federation.Application;

/// <summary>
/// 节点令牌服务: 签发/校验节点身份令牌, 与用户登录 JWT 完全隔离 (独立 issuer/audience)。
/// 支持双模式 — SYMMETRIC(HS256 全联邦共享密钥) / ASYMMETRIC(RS256 每节点密钥对, 对端仅存公钥)。
/// 校验时按令牌头 alg 自动选择密钥, 与本地配置模式解耦。
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
    private readonly FederationKeyStore _keys;
    private readonly FederationSettings _settings;
    private readonly ILogger<FederationTokenService> _logger;

    public FederationTokenService(IOptions<FederationOptions> opt, FederationKeyStore keys,
        FederationSettings settings, ILogger<FederationTokenService> logger)
    {
        _opt = opt.Value;
        _keys = keys;
        _settings = settings;
        _logger = logger;
    }

    public bool IsConfigured => _opt.HasIdentity;

    /// <summary>签发节点令牌。days&lt;=0 时使用配置的 TokenDays; mode 为空时用配置的 TokenMode。</summary>
    public string Issue(string nodeCode, string nodeName, string level, string? regionCode, int days = 0, string? mode = null)
    {
        if (!IsConfigured) throw new BizException("联动未配置: 缺少节点身份或共享密钥/密钥对", ErrorCodes.BadRequest);
        if (string.IsNullOrWhiteSpace(nodeCode)) throw new BizException("节点编码不能为空", ErrorCodes.BadRequest);
        var span = TimeSpan.FromDays(days > 0 ? days : _opt.TokenDays);

        var claims = new List<Claim>
        {
            new(ClaimNodeCode, nodeCode),
            new(ClaimNodeName, nodeName ?? ""),
            new(ClaimNodeLevel, string.IsNullOrWhiteSpace(level) ? _opt.Level : level),
        };
        if (!string.IsNullOrWhiteSpace(regionCode)) claims.Add(new Claim(ClaimRegionCode, regionCode!));

        var token = new JwtSecurityToken(
            issuer: Issuer, audience: Audience, claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.Add(span),
            signingCredentials: BuildCredentials(mode));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>签发本节点自身令牌 (用于调用对端); 名称/区划采用运行时生效值 (可界面热改)。</summary>
    public string IssueSelf(int days = 0) =>
        Issue(_opt.NodeCode, _settings.NodeName, _opt.Level, _settings.RegionCode, days);

    private SigningCredentials BuildCredentials(string? mode)
    {
        var effective = string.IsNullOrWhiteSpace(mode) ? _opt.TokenMode : mode;
        if (string.Equals(effective, TokenModes.Asymmetric, StringComparison.OrdinalIgnoreCase))
            return new SigningCredentials(new RsaSecurityKey(_keys.GetPrivateKey()), SecurityAlgorithms.RsaSha256);
        var sharedKey = _opt.SharedKey ?? "";
        if (Encoding.UTF8.GetByteCount(sharedKey) < 32)
            throw new BizException("SYMMETRIC 模式需要 Federation:SharedKey (>=32 字节)", ErrorCodes.BadRequest);
        return new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(sharedKey)), SecurityAlgorithms.HmacSha256);
    }

    /// <summary>校验节点令牌, 失败返回 null。</summary>
    public NodeIdentity? Validate(string? token)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);   // 仅解析头部与声明, 不信任未验签内容
            var alg = jwt.Header.Alg;
            var codeClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimNodeCode)?.Value;

            var tvp = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                ValidateIssuerSigningKey = true,
            };

            if (string.Equals(alg, SecurityAlgorithms.RsaSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(codeClaim)) return null;
                var pem = _keys.GetPeerPublicKey(codeClaim!);
                if (string.IsNullOrWhiteSpace(pem) && codeClaim == _opt.NodeCode) pem = _keys.EnsureLocalKeypair();
                if (string.IsNullOrWhiteSpace(pem))
                {
                    _logger.LogDebug("缺少对端 {Node} 公钥, 拒绝 RS256 令牌", codeClaim);
                    return null;
                }
                var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                tvp.IssuerSigningKey = new RsaSecurityKey(rsa);
            }
            else if (string.Equals(alg, SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                var sharedKey = _opt.SharedKey ?? "";
                if (Encoding.UTF8.GetByteCount(sharedKey) < 32) return null;
                tvp.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(sharedKey));
            }
            else
            {
                return null;   // 仅接受 HS256 / RS256
            }

            var principal = handler.ValidateToken(token, tvp, out _);
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
