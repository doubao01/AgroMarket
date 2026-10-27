using AgroMarket.Api.Common;
using AgroMarket.Api.Dtos;
using AgroMarket.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace AgroMarket.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AuthService _auth;
    public AuthController(AuthService auth) => _auth = auth;

    [HttpPost("sms/send")]
    public async Task<ActionResult<ApiResponse<object>>> SendSms([FromBody] SendSmsRequest req)
    {
        await _auth.SendSmsCodeAsync(req.Phone, HttpContext.ClientIp());
        return Ok(ApiResponse<object>.Ok(null, "验证码已发送 (开发模式请查看后端日志)"));
    }

    [HttpPost("login/code")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> LoginByCode([FromBody] LoginByCodeRequest req)
        => Ok(ApiResponse<AuthResponse>.Ok(await _auth.LoginByCodeAsync(req.Phone, req.Code)));

    [HttpPost("login/password")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> LoginByPassword([FromBody] LoginByPasswordRequest req)
        => Ok(ApiResponse<AuthResponse>.Ok(await _auth.LoginByPasswordAsync(req.Phone, req.Password)));

    /// <summary>
    /// 退出登录。清客户端本地副本是不够的 —— token 是 7 天有效期的无状态票据,
    /// 服务端不自增版本, 被抄走的 token 在这 7 天里依然是有效凭证。
    /// </summary>
    [HttpPost("logout")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> Logout()
    {
        await _auth.RevokeTokensAsync(HttpContext.User().Id);
        return Ok(ApiResponse<object>.Ok(null, "已退出登录"));
    }

    [HttpPost("register")]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Register([FromBody] RegisterRequest req)
        => Ok(ApiResponse<AuthResponse>.Ok(await _auth.RegisterAsync(req), "注册成功"));
}

[ApiController]
[Route("api/user")]
public class UserController : ControllerBase
{
    private readonly UserService _users;
    private readonly AuthService _auth;
    public UserController(UserService users, AuthService auth) { _users = users; _auth = auth; }

    /// <summary>
    /// 注销账号(匿名化)。会同时作废该用户所有 token。
    /// 不可逆: 手机号被改写, 重新使用需重新注册。
    /// </summary>
    [HttpPost("deactivate")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> Deactivate()
    {
        await _users.DeactivateAsync(HttpContext.User().Id, "USER_SELF");
        return Ok(ApiResponse<object>.Ok(null, "账号已注销"));
    }

    [HttpGet("me")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> Me()
        => Ok(ApiResponse<AuthResponse>.Ok(await _users.GetMeAsync(HttpContext.User().Id)));

    [HttpPut("profile")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<AuthResponse>>> UpdateProfile([FromBody] UpdateProfileRequest req)
        => Ok(ApiResponse<AuthResponse>.Ok(await _users.UpdateProfileAsync(HttpContext.User().Id, req)));

    [HttpPost("password")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        await _users.ChangePasswordAsync(HttpContext.User().Id, req.OldPassword, req.NewPassword);
        return Ok(ApiResponse<object>.Ok(null, "密码已修改"));
    }

    [HttpGet("auth/merchant")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<MerchantProfileDto?>>> MyMerchant()
        => Ok(ApiResponse<MerchantProfileDto?>.Ok(await _users.GetMyAuthAsync(HttpContext.User().Id)));

    [HttpPost("auth/merchant")]
    [RequireAuth]
    public async Task<ActionResult<ApiResponse<MerchantProfileDto>>> SubmitMerchant([FromBody] MerchantProfileRequest req)
        => Ok(ApiResponse<MerchantProfileDto>.Ok(await _users.SubmitAuthAsync(HttpContext.User().Id, req), "已提交, 等待审核"));
}
