using Microsoft.Extensions.Logging;
using Shared.Time;

namespace Core.Logging;

/// <summary>
/// 结构化日志事件。
/// 同时记录逻辑时间与系统时间，便于 GM 调时后的排障与导出分析。
/// </summary>
public sealed class LogEvent
{
    public DateTime TimestampUtc { get; init; } = TimeManager.UtcNow;
    public DateTime SystemTimestampUtc { get; init; } = DateTime.UtcNow;
    public long UnixSeconds { get; init; } = TimeManager.UnixSeconds;
    public LogLevel Level { get; init; } = LogLevel.Information;
    public string Category { get; init; } = "Core";
    public string Message { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public string? Remote { get; init; }
    public string? RequestId { get; init; }
    public string? UserId { get; init; }
}
