using System.Reflection;
using Google.Protobuf;

namespace Core;

public interface IMessageHandler<TReq, TResp>
    where TReq : class, IMessage<TReq>, new()
    where TResp : class, IMessage<TResp>, new()
{
    public Task<TResp?> HandleAsync(TReq request, IServerContext context);
}

internal interface IHandlerInvoker
{
    public Task<(string responseType, byte[] payload, int errorCode, string errorMessage)?> Invoke(ByteString payload, IServerContext context);
}

internal sealed class HandlerInvoker<TReq, TResp> : IHandlerInvoker
    where TReq : class, IMessage<TReq>, new()
    where TResp : class, IMessage<TResp>, new()
{
    private readonly IMessageHandler<TReq, TResp> _handler;
    private static readonly MessageParser<TReq> _reqParser = (MessageParser<TReq>)new TReq().Descriptor.Parser;
    private static readonly string _respTypeName = new TResp().Descriptor.Name;
    private static readonly PropertyInfo? _respSuccessProp = typeof(TResp).GetProperty("Success");
    private static readonly PropertyInfo? _respCodeProp = typeof(TResp).GetProperty("Code")
        ?? typeof(TResp).GetProperty("ErrorCode")
        ?? typeof(TResp).GetProperty("StatusCode");
    private static readonly PropertyInfo? _respMessageProp = typeof(TResp).GetProperty("Message")
        ?? typeof(TResp).GetProperty("ErrorMessage");

    public HandlerInvoker(IMessageHandler<TReq, TResp> handler)
    {
        _handler = handler;
    }

    public async Task<(string responseType, byte[] payload, int errorCode, string errorMessage)?> Invoke(ByteString payload, IServerContext context)
    {
        var reqObj = _reqParser.ParseFrom(payload);

        TResp? respObj;
        try
        {
            respObj = await _handler.HandleAsync(reqObj, context);
        }
        catch (Exception ex)
        {
            return (_respTypeName, [], 500, ex.Message);
        }

        if (respObj == null) return null;

        var (status, code, message) = DetermineStatusAndCode(respObj);

        if (string.Equals(status, "fail", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            var errorCodeInt = ErrorCodeMapper.Map(code ?? status);
            var errorMsg = message ?? code ?? status;
            return (_respTypeName, respObj.ToByteArray(), errorCodeInt, errorMsg);
        }

        return (_respTypeName, respObj.ToByteArray(), 0, string.Empty);
    }

    private static (string status, string? code, string? message) DetermineStatusAndCode(TResp resp)
    {
        string status = "ok";
        string? code = null;
        string? message = null;
        try
        {
            var successProp = _respSuccessProp;
            if (successProp != null)
            {
                var val = successProp.GetValue(resp);
                if (val is bool b && !b) status = "fail";
            }
            var p = _respCodeProp;
            if (p != null)
            {
                var v = p.GetValue(resp);
                if (v != null) code = Convert.ToString(v);
            }
            var msg = _respMessageProp;
            if (msg != null)
            {
                var v = msg.GetValue(resp);
                if (v != null) message = Convert.ToString(v);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MessageHandling] 反射读取响应属性失败: {ex.Message}");
        }
        return (status, code, message);
    }
}
