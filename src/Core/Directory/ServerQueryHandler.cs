using DotNetGameServer;

namespace Core.Directory;

public class ServerQueryHandler(IServerRegistry registry) : IMessageHandler<ServerQueryRequest, ServerQueryResponse>
{
    public async Task<ServerQueryResponse?> HandleAsync(ServerQueryRequest request, IServerContext context)
    {
        var candidates = await registry.QueryAsync(
            request.ServerType,
            request.Region,
            context.CancellationToken);
        var strategy = ServerSelectionStrategyFactory.Create(request.Strategy);
        var selected = strategy.Select(candidates, request.Limit > 0 ? request.Limit : 10, request.RoutingKey);

        var response = new ServerQueryResponse();
        foreach (var s in selected)
        {
            response.Servers.Add(new ServerInfo
            {
                ServerId = s.ServerId,
                ServerType = s.ServerType,
                Host = s.Host,
                Port = s.Port,
                PlayerCount = s.PlayerCount,
                Capacity = s.Capacity,
                Load = s.Load,
                Region = s.Region,
                Version = s.Version,
                LastHeartbeat = s.LastHeartbeatUnix,
                Status = s.Status,
                TokenAudience = s.TokenAudience,
                TlsEnabled = s.TlsEnabled,
                TlsServerName = s.TlsServerName
            });
        }
        return response;
    }
}
