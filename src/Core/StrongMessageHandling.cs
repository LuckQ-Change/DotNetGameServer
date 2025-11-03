using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using Google.Protobuf;
using Shared;
using Core.Components;
using Core.Config;
using Core.Telemetry;

namespace Core;

/// <summary>
/// 强类型消息处理器接口：以具体的请求/响应类型进行处理
/// </summary>
public interface IStrongMessageHandler<TReq, TResp>
    where TReq : class, IMessage<TReq>, new()
    where TResp : class, IMessage<TResp>, new()
{
    /// <summary>
    /// 处理强类型请求对象，并返回强类型响应对象。
    /// </summary>
    /// <param name="request">请求对象。</param>
    /// <param name="context">服务器上下文。</param>
    /// <returns>响应对象；无响应则返回 <c>null</c>。</returns>
    Task<TResp?> HandleAsync(TReq request, IServerContext context);
}

/// <summary>
/// 内部：统一的强类型处理器调用封装
/// </summary>
internal interface IStrongHandlerInvoker
{
    /// <summary>
    /// 使用 ByteString 载荷调用处理器，返回响应类型名与响应字节；无响应则为 null
    /// </summary>
    Task<(string responseType, byte[] payload, int errorCode, string errorMessage)?> Invoke(ByteString payload, IServerContext context);
}

internal sealed class StrongHandlerInvoker<TReq, TResp> : IStrongHandlerInvoker
    where TReq : class, IMessage<TReq>, new()
    where TResp : class, IMessage<TResp>, new()
{
    private readonly IStrongMessageHandler<TReq, TResp> _handler;
    // 缓存静态元数据，减少重复实例化与反射
    private static readonly MessageParser<TReq> _reqParser = (MessageParser<TReq>)new TReq().Descriptor.Parser;
    private static readonly string _reqTypeName = new TReq().Descriptor.Name;
    private static readonly string _respTypeName = new TResp().Descriptor.Name;
    private static readonly PropertyInfo? _reqUserIdProp = typeof(TReq).GetProperty("UserId")
        ?? typeof(TReq).GetProperty("PlayerId")
        ?? typeof(TReq).GetProperty("AccountId")
        ?? typeof(TReq).GetProperty("Uid");
    private static readonly PropertyInfo? _respSuccessProp = typeof(TResp).GetProperty("Success");
    private static readonly PropertyInfo? _respCodeProp = typeof(TResp).GetProperty("Code")
        ?? typeof(TResp).GetProperty("ErrorCode")
        ?? typeof(TResp).GetProperty("StatusCode")
        ?? typeof(TResp).GetProperty("Message");

    /// <summary>
    /// 构造统一的强类型处理器调用器。
    /// </summary>
    /// <param name="handler">强类型消息处理器实例。</param>
    public StrongHandlerInvoker(IStrongMessageHandler<TReq, TResp> handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// 使用字节载荷解析为请求对象并调用处理器，返回响应类型名与响应字节。
    /// </summary>
    /// <param name="payload">请求字节载荷。</param>
    /// <param name="context">服务器上下文。</param>
    /// <returns>响应类型名与字节载荷；无响应返回 <c>null</c>。</returns>
    public async Task<(string responseType, byte[] payload, int errorCode, string errorMessage)?> Invoke(ByteString payload, IServerContext context)
    {
        // 解析为强类型请求（缓存解析器）
        var reqObj = _reqParser.ParseFrom(payload);

        try
        {
            var token = context.AccessToken;
            if (!string.IsNullOrWhiteSpace(token))
            {
                var userId = TryExtractUserId(reqObj);
                var hb = context.GetComponent<HeartbeatClientComponent>();
                var serverId = hb?.TryGetCurrentServerId();
                if (!string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(serverId))
                {
                    var secretProvider = context.GetComponent<ITokenSecretProvider>();
                    var secret = secretProvider?.GetSecret() ?? SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory) ?? "dev-secret";
                    if (TokenSigner.TryVerify(token!, userId!, serverId!, secret, out var tokenId, out var exp))
                    {
                        if (context is RequestServerContext rctx)
                        {
                            rctx.SetAuth(new AuthInfo { UserId = userId!, TokenId = tokenId, ExpireUnixSeconds = exp });
                        }
                    }
                }
            }
        }
        catch { }

        var sw = Stopwatch.StartNew();
        var activity = CoreTelemetry.ActivitySource.StartActivity($"Handle {_reqTypeName}", ActivityKind.Server);
        activity?.AddTag("server_name", context.ServerName);
        activity?.AddTag("client_ip", context.RemoteAddress);
        if (context.Auth != null)
        {
            activity?.AddTag("user_id", context.Auth.UserId);
            activity?.AddTag("token_id", context.Auth.TokenId);
        }
        string status = "ok";
        string? errorCode = null;
        int errorCodeInt = 0;
        string errorMessage = string.Empty;
        TResp? respObj = null;
        try
        {
            respObj = await _handler.HandleAsync(reqObj, context);
            if (respObj == null)
            {
                status = "no_response";
            }
            else
            {
                (status, errorCode) = DetermineStatusAndCode(respObj);
            }
        }
        catch (Exception ex)
        {
            status = "error";
            errorCode = ex.GetType().Name;
            activity?.AddTag("exception", ex.Message);
            errorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            CoreTelemetry.RecordRequest(context.ServerName, _reqTypeName, status, sw.Elapsed.TotalSeconds);
            activity?.AddTag("status", status);
            if (!string.IsNullOrWhiteSpace(errorCode)) activity?.AddTag("error_code", errorCode);
            activity?.AddTag("latency_ms", (int)sw.Elapsed.TotalMilliseconds);
            context.Log($"req_id={context.RequestId} ip={context.RemoteAddress} type={_reqTypeName} status={status} code={errorCode ?? ""} latency_ms={(int)sw.Elapsed.TotalMilliseconds}");
            activity?.Stop();
        }

        if (respObj == null) return null;

        // 使用响应类型的消息名作为 Envelope.type
        var respType = _respTypeName;
        // 统一错误码规范化
        errorCodeInt = ErrorCodeMapper.Map(errorCode ?? status);
        if (string.IsNullOrWhiteSpace(errorMessage)) errorMessage = errorCode ?? status;
        return (respType, respObj.ToByteArray(), errorCodeInt, errorMessage);
    }
    
    private static string? TryExtractUserId(TReq req)
    {
        // 优先使用标准接口，避免反射依赖
        if (req is Shared.IUserIdentifiedRequest iid)
        {
            try { return iid.GetUserIdOrNull(); } catch { }
        }
        // 兼容旧请求：使用预缓存的常见字段属性
        try
        {
            var prop = _reqUserIdProp;
            if (prop != null)
            {
                var val = prop.GetValue(req);
                if (val != null) return Convert.ToString(val);
            }
        }
        catch { }
        return null;
    }

    private static (string status, string? code) DetermineStatusAndCode(TResp resp)
    {
        string status = "ok";
        string? code = null;
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
        }
        catch { }
        return (status, code);
    }
}