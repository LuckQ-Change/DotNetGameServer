namespace Core.Directory;

public class ServerRegistration
{
    public string ServerId { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public int PlayerCount { get; set; }
    public int Capacity { get; set; }
    public float Load => Capacity > 0 ? (float)PlayerCount / Capacity : 1f;
    public string Region { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long LastHeartbeatUnix { get; set; }
    public string Status { get; set; } = "online";
    public string TokenAudience { get; set; } = string.Empty;
    public bool TlsEnabled { get; set; }
    public string TlsServerName { get; set; } = string.Empty;
}

public interface IServerRegistry : IServerComponent
{
    Task<bool> RegisterAsync(ServerRegistration registration, CancellationToken cancellationToken = default);
    Task<bool> UnregisterAsync(string serverId, CancellationToken cancellationToken = default);
    Task<ServerRegistration?> GetAsync(string serverId, CancellationToken cancellationToken = default);
    Task<List<ServerRegistration>> QueryAsync(
        string? serverType = null,
        string? region = null,
        CancellationToken cancellationToken = default);
    Task<List<ServerRegistration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<int> CleanupStaleAsync(
        int offlineTtlSeconds,
        int removeTtlSeconds,
        CancellationToken cancellationToken = default);
}
