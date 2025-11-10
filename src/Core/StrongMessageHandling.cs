using System.Reflection;
using System.Diagnostics;
using Google.Protobuf;
using Shared;
using Core.Components;
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
    public Task<TResp?> HandleAsync(TReq request, IServerContext context);
}

/// <summary>
/// 内部：统一的强类型处理器调用封装
/// </summary>
internal interface IStrongHandlerInvoker
{
    /// <summary>
    /// 使用 ByteString 载荷调用处理器，返回响应类型名与响应字节；无响应则为 null
    /// </summary>
    public Task<(string responseType, byte[] payload, int errorCode, string errorMessage)?> Invoke(ByteString payload, IServerContext context);
}

internal sealed class StrongHandlerInvoker<TReq, TResp> : IStrongHandlerInvoker
    where TReq : class, IMessage<TReq>, new()
    where TResp : class, IMessage<TResp>, new()
{
    // 常量化常用标签与状态，移除重复的魔法字符串
    private const string TagServerName = "server_name";
    private const string TagClientIp = "client_ip";
    private const string TagUserId = "user_id";
    private const string TagTokenId = "token_id";
    private const string TagException = "exception";
    private const string TagStatus = "status";
    private const string TagErrorCode = "error_code";
    private const string TagLatencyMs = "latency_ms";

    private const string StatusOk = "ok";
    private const string StatusError = "error";
    private const string StatusNoResponse = "no_response";
    private const string StatusFail = "fail";

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
            var userId = TryExtractUserId(reqObj);
            var hb = context.GetComponent<HeartbeatClientComponent>();
            var serverId = hb?.TryGetCurrentServerId();
            // 合并条件，避免嵌套 if (S1066)，当无法提取用户或服务器 ID 时按未认证继续处理。
            if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(serverId))
            {
                // 统一通过 TokenSecretProvider 获取密钥；生产环境缺失会在 Provider 初始化时抛错并阻止启动
                var secretProvider = context.GetComponent<ITokenSecretProvider>();
                var secret = secretProvider?.GetSecret();
                if (!string.IsNullOrWhiteSpace(secret))
                {
                    bool verified = false;
                    string? tokenId = null;
                    long exp = 0;
                    var verifySecrets = secretProvider?.GetVerifySecrets() ?? new[] { secret! };
                    foreach (var s in verifySecrets)
                    {
                        if (!string.IsNullOrWhiteSpace(s) && TokenSigner.TryVerify(token!, userId!, serverId!, s, out var tid, out var e))
                        {
                            verified = true;
                            tokenId = tid;
                            exp = e;
                            break;
                        }
                    }
                    if (verified && tokenId != null)
                    {
                        // 撤销列表检查：命中则视为未认证
                        var revocation = context.GetComponent<ITokenRevocationStore>();
                        if (revocation != null && revocation.IsRevoked(tokenId))
                        {
                            context.Log("令牌已被撤销，按未认证处理。");
                        }
                        else if (context is RequestServerContext rctx)
                        {
                            rctx.SetAuth(new AuthInfo { UserId = userId!, TokenId = tokenId, ExpireUnixSeconds = exp });
                        }
                    }
                }
            }

        }
        catch (Exception ex)
        {
            // 认证失败不应阻断请求处理，记录轻量日志后继续。
            context.Log($"认证信息解析/校验异常，忽略并按未认证处理: {ex.Message}");
        }

        var sw = Stopwatch.StartNew();
        var activity = CoreTelemetry.ActivitySource.StartActivity($"Handle {_reqTypeName}", ActivityKind.Server);
        activity?.AddTag(TagServerName, context.ServerName);
        activity?.AddTag(TagClientIp, context.RemoteAddress);
        if (context.Auth != null)
        {
            activity?.AddTag(TagUserId, context.Auth.UserId);
            activity?.AddTag(TagTokenId, context.Auth.TokenId);
        }
        string status = StatusOk;
        string? errorCode = null;
        int errorCodeInt = 0;
        string errorMessage = string.Empty;
        TResp? respObj = null;
        try
        {
            respObj = await _handler.HandleAsync(reqObj, context);
            if (respObj == null)
            {
                status = StatusNoResponse;
            }
            else
            {
                (status, errorCode) = DetermineStatusAndCode(respObj);
            }
        }
        catch (Exception ex)
        {
            status = StatusError;
            errorCode = ex.GetType().Name;
            activity?.AddTag(TagException, ex.Message);
            errorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            CoreTelemetry.RecordRequest(context.ServerName, _reqTypeName, status, sw.Elapsed.TotalSeconds);
            activity?.AddTag(TagStatus, status);
            if (!string.IsNullOrWhiteSpace(errorCode)) activity?.AddTag(TagErrorCode, errorCode);
            activity?.AddTag(TagLatencyMs, (int)sw.Elapsed.TotalMilliseconds);
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
            try { return iid.GetUserIdOrNull(); }
            catch (Exception)
            {
                // 对旧协议的兼容：接口实现异常时按未认证继续。
                return null;
            }
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
        catch (Exception)
        {
            // 反射读取失败时不影响请求处理，按未认证继续。
        }
        return null;
    }

    private static (string status, string? code) DetermineStatusAndCode(TResp resp)
    {
        string status = StatusOk;
        string? code = null;
        try
        {
            var successProp = _respSuccessProp;
            if (successProp != null)
            {
                var val = successProp.GetValue(resp);
                if (val is bool b && !b) status = StatusFail;
            }
            var p = _respCodeProp;
            if (p != null)
            {
                var v = p.GetValue(resp);
                if (v != null) code = Convert.ToString(v);
            }
        }
        catch (Exception)
        {
            // 兼容响应对象不含约定字段的情况，保持默认 status/code。
        }
        return (status, code);
    }
}
