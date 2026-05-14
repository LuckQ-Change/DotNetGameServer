using System.Diagnostics;
using System.Globalization;

namespace Shared.Time;

/// <summary>
/// 时间工具类。
/// 与业务时间相关的当前时间统一走 TimeManager，单调时钟仍使用 Stopwatch。
/// </summary>
public static class TimeUtil
{
    public static DateTime UtcNow() => TimeManager.UtcNow;

    public static DateTime Now() => TimeManager.Now;

    public static long UnixNowSeconds() => TimeManager.UnixSeconds;

    public static long UnixNowMilliseconds() => TimeManager.UnixMilliseconds;

    public static long ToUnixSeconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    public static long ToUnixSeconds(DateTimeOffset dto) => dto.ToUnixTimeSeconds();

    public static long ToUnixMilliseconds(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    public static long ToUnixMilliseconds(DateTimeOffset dto) => dto.ToUnixTimeMilliseconds();

    public static DateTimeOffset FromUnixSeconds(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

    public static DateTimeOffset FromUnixMilliseconds(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

    public static string FormatIso8601Utc(DateTime utc)
    {
        var dt = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }

    public static DateTimeOffset ParseIso8601(string iso8601)
    {
        return DateTimeOffset.Parse(
            iso8601,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public static long MonotonicElapsedMs(long startTimestamp)
    {
        var dt = Stopwatch.GetTimestamp() - startTimestamp;
        return (long)(dt * 1000.0 / Stopwatch.Frequency);
    }

    public static long MonotonicNowTicks() => Stopwatch.GetTimestamp();
}
