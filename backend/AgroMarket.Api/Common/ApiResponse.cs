namespace AgroMarket.Api.Common;

public record ApiResponse<T>(int Code, string Msg, T? Data)
{
    public static ApiResponse<T> Ok(T? data, string msg = "ok") => new(0, msg, data);
    public static ApiResponse<T> Fail(int code, string msg) => new(code, msg, default);
}

public static class ErrorCodes
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int ServerError = 500;
    public const int NotImplemented = 501;
}
