using Core;
using DotNetGameServer;
using DirectoryServer.Components;

namespace DirectoryServer.Handlers;

public class QueryServerStatusMessageHandler : IMessageHandler<c2s_query_server_status, s2c_query_server_status>
{
    public Task<s2c_query_server_status?> HandleAsync(c2s_query_server_status req, IServerContext context)
    {
        var reg = context.GetComponent<IServerRegistryComponent>();
        var resp = new s2c_query_server_status();
        if (reg == null)
        {
            resp.Found = false;
            return Task.FromResult<s2c_query_server_status?>(resp);
        }

        var s = reg.GetServer(req.ServerId);
        if (s == null)
        {
            resp.Found = false;
            return Task.FromResult<s2c_query_server_status?>(resp);
        }

        resp.Found = true;
        resp.Info = new server_info()
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

        return Task.FromResult<s2c_query_server_status?>(resp);
    }
}
