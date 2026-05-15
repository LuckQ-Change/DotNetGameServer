namespace Core.Configuration;

public class HeartbeatOptions
{
    public string Region { get; set; } = "dev";
    public int Capacity { get; set; } = 100;
    public int IntervalSeconds { get; set; } = 5;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string ServerType { get; set; } = "default";
    public string ServerId { get; set; } = "";
    public string DirectoryHost { get; set; } = "127.0.0.1";
    public int DirectoryPort { get; set; } = 5500;
    public string TokenSecret { get; set; } = "";
}
