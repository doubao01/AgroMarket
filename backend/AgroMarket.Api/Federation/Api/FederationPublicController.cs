using AgroMarket.Api.Common;
using AgroMarket.Api.Federation.Dtos;
using AgroMarket.Api.Federation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Api;

/// <summary>节点公开信息接口: 分发本节点公钥 (非敏感, 供对端校验 RS256 令牌)。</summary>
[ApiController]
[Route("api/federation/node")]
public class FederationPublicController : ControllerBase
{
    private readonly FederationKeyStore _keys;
    private readonly FederationOptions _opt;

    public FederationPublicController(FederationKeyStore keys, IOptions<FederationOptions> opt)
    {
        _keys = keys;
        _opt = opt.Value;
    }

    /// <summary>返回本节点公钥 (PEM)。非对称令牌模式下对端据此校验本节点签发的令牌。</summary>
    [HttpGet("key")]
    public ActionResult<ApiResponse<FedKeyDto>> Key()
    {
        if (!_opt.Enabled || string.IsNullOrWhiteSpace(_opt.NodeCode))
            throw new BizException("联动未启用", ErrorCodes.BadRequest);
        return Ok(ApiResponse<FedKeyDto>.Ok(new FedKeyDto
        {
            NodeCode = _opt.NodeCode,
            TokenMode = _opt.TokenMode,
            Alg = FederationKeyStore.Alg,
            PublicKey = _keys.EnsureLocalKeypair(),
        }));
    }
}
