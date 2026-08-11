using Microsoft.Extensions.Logging;
namespace Core.Logging;

/// <summary>
/// 结构化日志事件。
/// 包含请求关联字段，但不依赖具体日志存储或仪表盘产品。
/// </summary>
public sealed class LogEvent
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public long UnixSeconds { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public LogLevel Level { get; init; } = LogLevel.Information;
    public string Category { get; init; } = "Core";
    public string Message { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public string? Remote { get; init; }
    public string? RequestId { get; init; }
    public string? UserId { get; init; }
}
