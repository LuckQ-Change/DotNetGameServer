using DotNetGameServer;

using Shared;

namespace Core.Directory;

/// <summary>处理服务实例的主动下线通知。</summary>
public sealed class ServerUnregisterHandler(IServerRegistry registry)
    : IMessageHandler<ServerUnregisterRequest, ServerUnregisterResponse>
{
    public async Task<ServerUnregisterResponse?> HandleAsync(
        ServerUnregisterRequest request,
        IServerContext context)
    {
        if (!TokenSigner.TryGetServiceIdentity(context.Auth?.UserId, out var sourceServerId)
            || !string.Equals(sourceServerId, request.ServerId, StringComparison.Ordinal))
        {
            return new ServerUnregisterResponse
            {
                Success = false,
                Code = ProtocolError.AuthenticationRequired,
                Message = "unauthorized"
            };
        }

        var removed = await registry.UnregisterAsync(request.ServerId, context.CancellationToken);
        return new ServerUnregisterResponse
        {
            Success = true,
            Code = 0,
            Message = removed ? "unregistered" : "already_absent"
        };
    }
}
