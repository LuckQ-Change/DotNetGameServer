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
    public static bool GlobalEnabled { get; private set; } = false;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _categoryEnabled = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 设置最小日志级别
    /// </summary>
    public static void SetMinimumLevel(LogLevel level) { MinimumLevel = level; }
    public static void SetGlobalEnabled(bool enabled) { GlobalEnabled = enabled; }
    public static void EnableCategory(string category) { _categoryEnabled[category] = true; }
    public static void DisableCategory(string category) { _categoryEnabled[category] = false; }
    public static bool IsCategoryEnabled(string category) => _categoryEnabled.TryGetValue(category, out var en) && en;

    /// <summary>
    /// 判断是否应输出该级别日志
    /// </summary>
    public static bool ShouldLog(LogLevel level) => ShouldLog(level, null);
    public static bool ShouldLog(LogLevel level, string? category)
    {
        var catEnabled = category != null && IsCategoryEnabled(category);
        if (catEnabled) return true;
        if (!GlobalEnabled) return false;
        return level >= MinimumLevel;
    }

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

            return LogLevel.None;
        }
        catch
        {
            return LogLevel.None;
        }
    }
}
