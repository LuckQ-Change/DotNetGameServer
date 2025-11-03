using Core;
using DotNetGameServer;
using Google.Protobuf;
using Core.Components;
using Core.Config;
using Shared;

namespace LoginServer;

/// <summary>
/// 登录请求消息处理器
/// </summary>
public class LoginMessageHandler : IStrongMessageHandler<LoginRequest, LoginResponse>
{
    // 强类型处理实现
    public async Task<LoginResponse?> HandleAsync(LoginRequest request, IServerContext context)
    {
        // 获取用户验证组件
        var authComponent = context.GetComponent<IUserAuthComponent>();

        bool success;
        string message;
        string? token = null;
        long expire = 0;

        if (authComponent != null)
        {
            var result = await authComponent.AuthenticateAsync(request.Username, request.Password);
            success = result.Success;
            message = result.Message;
        }
        else
        {
            // 默认验证逻辑
            success = request.Username == "admin" && request.Password == "123456";
            message = success ? "登录成功" : "用户名或密码错误";
        }

        context.Log($"处理登录请求(强类型): {request.Username}, 结果={success}");

        // 登录成功则签发令牌（供客户端后续放入 Envelope.token）
        if (success)
        {
            try
            {
                var hb = context.GetComponent<HeartbeatClientComponent>();
                var serverId = hb?.TryGetCurrentServerId();
                var secretProvider = context.GetComponent<ITokenSecretProvider>();
                var secret = secretProvider?.GetSecret() ?? SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory) ?? "dev-secret";
                if (!string.IsNullOrWhiteSpace(serverId))
                {
                    expire = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600; // 1小时有效
                    var sig = TokenSigner.ComputeSignature(serverId!, expire, request.Username, serverId!, secret);
                    token = TokenSigner.ComposeSigned(serverId!, expire, sig);
                }
            }
            catch { }
        }

        var response = new LoginResponse
        {
            Success = success,
            Message = message,
            Token = token ?? string.Empty,
            ExpireAt = expire
        };

        return response;
    }
}