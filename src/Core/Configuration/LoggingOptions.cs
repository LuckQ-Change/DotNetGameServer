using Microsoft.Extensions.Logging;

namespace Core.Configuration;

public class LoggingOptions
{
    public LogLevel MinimumLevel { get; set; } = LogLevel.Warning;
    public bool Enabled { get; set; }
    public bool ForwardToILogger { get; set; }
    public bool FileEnabled { get; set; }
    public string FileDirectory { get; set; } = "logs";
    public int FileMaxMegabytes { get; set; } = 20;
    public int FileRetentionDays { get; set; } = 14;
}
