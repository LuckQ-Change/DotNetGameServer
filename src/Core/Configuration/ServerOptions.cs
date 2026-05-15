namespace Core.Configuration;

public class ServerOptions
{
    public int MaxMessageSizeBytes { get; set; } = 1 * 1024 * 1024;
    public int MaxConcurrentConnections { get; set; } = 200;
    public int ReceiveTimeoutMs { get; set; } = 10000;
    public int AcceptBackoffMs { get; set; } = 200;
    public int MaxBytesPerSecond { get; set; } = 1_000_000;
    public int MaxMessagesPerSecond { get; set; } = 200;
    public int IdleTimeoutMs { get; set; } = 120_000;
    public int IpMaxBytesPerSecond { get; set; }
    public int IpMaxMessagesPerSecond { get; set; }
}
