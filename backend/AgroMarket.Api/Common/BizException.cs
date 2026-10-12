namespace AgroMarket.Api.Common;

public class BizException : Exception
{
    public int Code { get; }
    public BizException(string msg, int code = ErrorCodes.BadRequest) : base(msg) => Code = code;
}
