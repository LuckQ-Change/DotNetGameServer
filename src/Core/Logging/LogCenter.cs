using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Core.Logging;

/// <summary>
/// 进程级轻量日志事件源。应用可以将事件接入任意日志后端，
/// 无需把仪表盘、持久化或命令系统引入核心层。
/// </summary>
public static class LogCenter
{
    public static event Action<LogEvent>? Published;

    public static LogLevel MinimumLevel { get; private set; } = LogLevel.Warning;
    public static bool Enabled { get; private set; }
    public static bool ForwardToILogger { get; private set; }

    public static void ApplyOptions(LoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        MinimumLevel = options.MinimumLevel;
        Enabled = options.Enabled;
        ForwardToILogger = options.ForwardToILogger;
    }

    public static bool ShouldLog(LogLevel level, string? category = null) =>
        Enabled && level >= MinimumLevel;

    public static void Publish(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);
        var handlers = Published;
        if (handlers is null) return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action<LogEvent>>())
        {
            try
            {
                handler(logEvent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LogCenter] subscriber failed: {ex.Message}");
            }
        }
    }
}
