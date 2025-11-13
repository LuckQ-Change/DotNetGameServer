using System;
using Microsoft.Extensions.Logging;
using R3;

namespace Core.Logging;

/// <summary>
/// 日志中心：集中式最小级别控制与事件流分发
/// </summary>
public static class LogCenter
{
    private static readonly Subject<LogEvent> _subject = new();

    /// <summary>
    /// 日志事件流（R3），后台可通过 SSE 订阅
    /// </summary>
    public static Subject<LogEvent> Stream => _subject;

    /// <summary>
    /// 最小日志级别（可由环境变量 LOG_LEVEL 覆盖）
    /// Development 默认 Information，Production 默认 Warning
    /// </summary>
    public static LogLevel MinimumLevel { get; private set; } = ResolveDefaultLevel();

    /// <summary>
    /// 设置最小日志级别
    /// </summary>
    public static void SetMinimumLevel(LogLevel level)
    {
        MinimumLevel = level;
    }

    /// <summary>
    /// 判断是否应输出该级别日志
    /// </summary>
    public static bool ShouldLog(LogLevel level) => level >= MinimumLevel;

    /// <summary>
    /// 推送一条日志事件到订阅流
    /// </summary>
    public static void Publish(LogEvent e)
    {
        try { _subject.OnNext(e); }
        catch
        {
            // 忽略订阅回调异常，确保日志主流程不中断
        }
    }

    private static LogLevel ResolveDefaultLevel()
    {
        try
        {
            var envLevel = Environment.GetEnvironmentVariable("LOG_LEVEL");
            if (!string.IsNullOrWhiteSpace(envLevel) && Enum.TryParse<LogLevel>(envLevel, true, out var parsed))
                return parsed;

            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? string.Empty;
            if (string.Equals(env, "Production", StringComparison.OrdinalIgnoreCase)) return LogLevel.Warning;
            return LogLevel.Information;
        }
        catch
        {
            return LogLevel.Information;
        }
    }
}
