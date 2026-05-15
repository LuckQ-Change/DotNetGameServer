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
}

public interface IServerRegistry : IServerComponent
{
    Task<bool> RegisterAsync(ServerRegistration registration);
    Task<bool> UnregisterAsync(string serverId);
    Task<ServerRegistration?> GetAsync(string serverId);
    Task<List<ServerRegistration>> QueryAsync(string? serverType = null, string? region = null);
    Task<List<ServerRegistration>> GetAllAsync();
    Task<int> CleanupStaleAsync(int offlineTtlSeconds, int removeTtlSeconds);
}
