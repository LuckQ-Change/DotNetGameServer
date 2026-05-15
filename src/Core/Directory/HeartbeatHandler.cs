using DotNetGameServer;

namespace Core.Directory;

public class HeartbeatHandler(IServerRegistry registry) : IMessageHandler<HeartbeatRequest, HeartbeatResponse>
{
    public async Task<HeartbeatResponse?> HandleAsync(HeartbeatRequest request, IServerContext context)
    {
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
}
