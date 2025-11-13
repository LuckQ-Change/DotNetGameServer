using System;
using Microsoft.Extensions.Logging;

namespace Core.Logging;

/// <summary>
/// 日志事件数据（用于后台订阅与降噪分析）
/// </summary>
public sealed class LogEvent
{
    public DateTime TimestampUtc { get; init; } = DateTime.UtcNow;
    public LogLevel Level { get; init; } = LogLevel.Information;
    public string Category { get; init; } = "Core";
    public string Message { get; init; } = string.Empty;
    public string Server { get; init; } = string.Empty;
    public string? Remote { get; init; }
    public string? RequestId { get; init; }
}

