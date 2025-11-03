using Core;

namespace DirectoryServer.Components;

public interface IServerRegistryComponent : IServerComponent
{
    void UpsertHeartbeat(string id, string type, string region, string host, int port, int playerCount, int capacity, string status, int avgRttMs);
    IEnumerable<ServerRecord> ListServers();
    ServerRecord? GetServer(string id);
    SwitchToken IssueSwitchToken(string userId, ServerRecord target, TimeSpan ttl);
}

public record ServerRecord(
    string Id,
    string Type,
    string Region,
    string Host,
    int Port,
    int PlayerCount,
    int Capacity,
    string Status,
    DateTime LastHeartbeatUtc,
    int AvgRttMs
);

public record SwitchToken(string Token, DateTime ExpireAtUtc);