using Core.Components;

using DotNetGameServer;

using Microsoft.Extensions.Logging;

using Shared;

namespace Core.Directory;

public class HeartbeatHandler(IServerRegistry registry) : IMessageHandler<HeartbeatRequest, HeartbeatResponse>
{
    public async Task<HeartbeatResponse?> HandleAsync(HeartbeatRequest request, IServerContext context)
    {
        if (!IsSignatureValid(request, context))
        {
            context.Log($"心跳签名校验失败，拒绝注册: {request.ServerId}", LogLevel.Warning);
            return new HeartbeatResponse
            {
                Accepted = false,
                Message = "unauthorized",
                NextHeartbeatInterval = 5
            };
        }

        var registration = new ServerRegistration
        {
            ServerId = request.ServerId,
            ServerType = request.ServerType,
            Host = request.Host,
            Port = request.Port,
            PlayerCount = request.PlayerCount,
            Capacity = request.Capacity > 0 ? request.Capacity : 100,
            Region = request.Region,
            Version = request.Version,
            Status = "online"
        };

        var accepted = await registry.RegisterAsync(registration);
        return new HeartbeatResponse
        {
            Accepted = accepted,
            Message = accepted ? "ok" : "register_failed",
            NextHeartbeatInterval = 5
        };
    }

    /// <summary>
    /// 校验心跳令牌签名：令牌缺失或任一已配置密钥均无法验证时拒绝。
    /// </summary>
    private static bool IsSignatureValid(HeartbeatRequest request, IServerContext context)
    {
        var token = context.AccessToken;
        if (string.IsNullOrWhiteSpace(token)) return false;

        var secretProvider = context.GetComponent<ITokenSecretProvider>();
        if (secretProvider == null) return false;

        foreach (var secret in secretProvider.GetVerifySecrets())
        {
            if (string.IsNullOrWhiteSpace(secret)) continue;
            if (TokenSigner.TryVerifyHeartbeat(token, request.ServerId, secret, out _, out _))
                return true;
        }

        return false;
    }
}
