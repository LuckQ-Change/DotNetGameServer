using System.Collections.Concurrent;
using System.Text.Json;

using Core.Configuration;
using Microsoft.Extensions.Logging;

using R3;

using Shared.Time;

namespace Core.Logging;

public static class LogCenter
{
    private static readonly Subject<LogEvent> Subject = new();
    private static ConcurrentQueue<LogEvent> Cache = new();
    private static readonly object CacheTrimLock = new();
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static Subject<LogEvent> Stream => Subject;

    public static LogLevel MinimumLevel { get; private set; } = LogLevel.Warning;

    public static bool GlobalEnabled { get; private set; }

    public static bool CacheEnabled { get; private set; } = true;

    public static int CacheCapacity { get; private set; } = 2000;

    private static readonly ConcurrentDictionary<string, bool> CategoryEnabled = new(StringComparer.OrdinalIgnoreCase);

    public static void ApplyOptions(LoggingOptions options)
    {
        MinimumLevel = options.MinimumLevel;
        GlobalEnabled = options.Enabled;
        CacheCapacity = Math.Max(1, options.CacheCapacity);
        if (options.HeartbeatEnabled) CategoryEnabled["Heartbeat"] = true;
        TrimCache();
    }

    public static void SetMinimumLevel(LogLevel level) => MinimumLevel = level;

    public static void SetGlobalEnabled(bool enabled) => GlobalEnabled = enabled;

    public static void SetCacheEnabled(bool enabled) => CacheEnabled = enabled;

    public static void SetCacheCapacity(int capacity)
    {
        CacheCapacity = Math.Max(1, capacity);
        TrimCache();
    }

    public static void EnableCategory(string category) => CategoryEnabled[category] = true;

    public static void DisableCategory(string category) => CategoryEnabled[category] = false;

    public static bool IsCategoryEnabled(string category) =>
        CategoryEnabled.TryGetValue(category, out var enabled) && enabled;

    public static bool ShouldLog(LogLevel level) => ShouldLog(level, null);

    public static bool ShouldLog(LogLevel level, string? category)
    {
        if (category != null && CategoryEnabled.TryGetValue(category, out var enabled))
            return enabled;
        if (!GlobalEnabled) return false;
        return level >= MinimumLevel;
    }

    public static void Publish(LogEvent logEvent)
    {
        if (CacheEnabled)
        {
            var cache = Cache;
            cache.Enqueue(logEvent);
            TrimCache(cache);
        }

        try
        {
            Subject.OnNext(logEvent);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LogCenter] 订阅回调异常: {ex.Message}");
        }
    }

    public static IReadOnlyList<LogEvent> GetRecent(int count = 200)
    {
        var safeCount = Math.Max(1, count);
        var snapshot = Cache.ToArray();
        return snapshot.Length <= safeCount
            ? snapshot
            : snapshot.Skip(snapshot.Length - safeCount).ToArray();
    }

    public static void ClearCache()
    {
        Interlocked.Exchange(ref Cache, new ConcurrentQueue<LogEvent>());
    }

    public static async Task<LogExportResult> ExportAsync(string? path = null, int count = 0, CancellationToken cancellationToken = default)
    {
        var snapshot = Cache.ToArray();
        if (count > 0 && snapshot.Length > count)
        {
            snapshot = snapshot.Skip(snapshot.Length - count).ToArray();
        }

        var resolvedPath = ResolveExportPath(path);
        var dir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            System.IO.Directory.CreateDirectory(dir);
        }

        await using var stream = new FileStream(
            resolvedPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 8192,
            useAsync: true);
        await using var writer = new StreamWriter(stream);

        foreach (var item in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = JsonSerializer.Serialize(item, ExportJsonOptions);
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync();
        return new LogExportResult(resolvedPath, snapshot.Length);
    }

    private static string ResolveExportPath(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            return Path.IsPathRooted(path)
                ? path
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
        }

        var fileName = $"logs-{TimeManager.UtcNow:yyyyMMdd-HHmmss}.jsonl";
        return Path.Combine(AppContext.BaseDirectory, "exports", fileName);
    }

    private static void TrimCache()
    {
        TrimCache(Cache);
    }

    private static void TrimCache(ConcurrentQueue<LogEvent> cache)
    {
        if (CacheCapacity <= 0) return;

        lock (CacheTrimLock)
        {
            var overflow = cache.Count - CacheCapacity;
            if (overflow <= 0) return;
            for (var i = 0; i < overflow; i++)
            {
                if (!cache.TryDequeue(out _)) break;
            }
        }
    }

}

/// <summary>
/// 日志导出结果。
/// </summary>
public sealed record LogExportResult(string Path, int Count);
