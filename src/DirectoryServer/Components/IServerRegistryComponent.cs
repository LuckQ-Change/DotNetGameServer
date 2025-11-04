using Core;

namespace DirectoryServer.Components;

public interface IServerRegistryComponent : IServerComponent
{
    void UpsertHeartbeat(string id, string type, string region, string host, int port, int playerCount, int capacity, string status, int avgRttMs);
    IEnumerable<ServerRecord> ListServers();
    ServerRecord? GetServer(string id);
    SwitchToken IssueSwitchToken(string userId, ServerRecord target, TimeSpan ttl);
    /// <summary>
    /// 校验切服令牌：匹配注册表中为 (userId, serverId) 存储的原始令牌ID。
    /// 若 consume 为 true，验证通过后从注册表中删除以防止重放。
    /// </summary>
    bool CheckSwitchToken(string userId, string serverId, string tokenId, bool consume = false);
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