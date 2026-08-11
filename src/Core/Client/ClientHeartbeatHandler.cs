using Core.Configuration;

using DotNetGameServer;

namespace Core.Client;

/// <summary>按需启用的客户端应用层心跳处理器。</summary>
internal sealed class ClientHeartbeatHandler(ClientHeartbeatOptions options)
    : IMessageHandler<ClientHeartbeatRequest, ClientHeartbeatResponse>
{
    public Task<ClientHeartbeatResponse?> HandleAsync(
        ClientHeartbeatRequest request,
        IServerContext context)
    {
        return Task.FromResult<ClientHeartbeatResponse?>(new ClientHeartbeatResponse
        {
            Success = true,
            Code = 0,
            Message = "ok",
            ServerTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            RecommendedIntervalSeconds = Math.Max(1, options.RecommendedIntervalSeconds)
        });
    }
}
