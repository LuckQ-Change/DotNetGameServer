using System.Reflection;
using DotNetGameServer;
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

        // 尝试将响应转换为 IServerResponse
        IServerResponse? serverResponse = null;
        if (respObj is IServerResponse directResponse)
        {
            serverResponse = directResponse;
        }
        else
        {
            // 尝试使用扩展方法转换
            serverResponse = TryConvertToServerResponse(respObj);
        }

        string status;
        int code;
        string message;

        if (serverResponse != null)
        {
            status = serverResponse.Success ? "ok" : "fail";
            code = serverResponse.Code;
            message = serverResponse.Message;
        }
        else
        {
            // 回退到反射
            var (statusStr, codeStr, messageStr) = DetermineStatusAndCode(respObj);
            status = statusStr;
            code = string.IsNullOrEmpty(codeStr) ? 0 : ErrorCodeMapper.Map(codeStr);
            message = messageStr ?? string.Empty;
        }

        if (string.Equals(status, "fail", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "error", StringComparison.OrdinalIgnoreCase))
        {
            var errorCodeInt = code > 0 ? code : ErrorCodeMapper.Map(message);
            var errorMsg = !string.IsNullOrEmpty(message) ? message : status;
            return (_respTypeName, respObj.ToByteArray(), errorCodeInt, errorMsg);
        }

        return (_respTypeName, respObj.ToByteArray(), 0, string.Empty);
    }

    private static IServerResponse? TryConvertToServerResponse(TResp resp)
    {
        // 为已知的 Protobuf 响应类型提供适配
        if (resp is HeartbeatResponse heartbeat)
            return heartbeat.AsServerResponse();
        if (resp is ServerQueryResponse query)
            return query.AsServerResponse();
        
        return null;
    }

    private static (string status, string? code, string? message) DetermineStatusAndCode(TResp resp)
    {
        // 回退到反射（向后兼容）
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
