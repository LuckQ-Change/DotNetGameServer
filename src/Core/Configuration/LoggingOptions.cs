using Microsoft.Extensions.Logging;

namespace Core.Configuration;

public class LoggingOptions
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;
    public bool Enabled { get; set; }
    public int CacheCapacity { get; set; } = 2000;
    public bool HeartbeatEnabled { get; set; }
    public bool ForwardToILogger { get; set; }
}
