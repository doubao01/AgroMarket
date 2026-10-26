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
