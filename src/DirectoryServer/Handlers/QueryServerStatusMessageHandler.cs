using Core;
using DotNetGameServer;
using Google.Protobuf;
using DirectoryServer.Components;

namespace DirectoryServer.Handlers;

public class QueryServerStatusMessageHandler : IStrongMessageHandler<QueryServerStatusRequest, QueryServerStatusResponse>
{
    public Task<QueryServerStatusResponse?> HandleAsync(QueryServerStatusRequest req, IServerContext context)
    {
        var reg = context.GetComponent<IServerRegistryComponent>();
        var resp = new QueryServerStatusResponse();
        if (reg == null)
        {
            resp.Found = false;
            return Task.FromResult<QueryServerStatusResponse?>(resp);
        }

        var s = reg.GetServer(req.ServerId);
        if (s == null)
        {
            resp.Found = false;
            return Task.FromResult<QueryServerStatusResponse?>(resp);
        }

        resp.Found = true;
        resp.Info = new ServerInfo
        {
            Id = s.Id,
            Type = s.Type,
            Region = s.Region,
            Host = s.Host,
            Port = s.Port,
            PlayerCount = s.PlayerCount,
            Capacity = s.Capacity,
            Status = s.Status,
            LastHeartbeat = new DateTimeOffset(s.LastHeartbeatUtc).ToUnixTimeMilliseconds()
        };

        return Task.FromResult<QueryServerStatusResponse?>(resp);
    }
}