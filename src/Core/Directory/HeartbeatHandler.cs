using DotNetGameServer;

using Microsoft.Extensions.Logging;

namespace Core.Directory;

public class HeartbeatHandler(IServerRegistry registry) : IMessageHandler<HeartbeatRequest, HeartbeatResponse>
{
    public async Task<HeartbeatResponse?> HandleAsync(HeartbeatRequest request, IServerContext context)
    {
        if (!Shared.TokenSigner.TryGetServiceIdentity(context.Auth?.UserId, out var sourceServerId)
            || !string.Equals(sourceServerId, request.ServerId, StringComparison.Ordinal))
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
            TokenAudience = request.TokenAudience,
            TlsEnabled = request.TlsEnabled,
            TlsServerName = request.TlsServerName,
            Status = "online"
        };

        var accepted = await registry.RegisterAsync(registration, context.CancellationToken);
        return new HeartbeatResponse
        {
            Accepted = accepted,
            Message = accepted ? "ok" : "register_failed",
            NextHeartbeatInterval = 5
        };
    }

}
