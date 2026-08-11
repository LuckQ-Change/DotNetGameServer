using System.Diagnostics;
using System.Globalization;

namespace Shared.Time;

/// <summary>
/// 时间工具类。
/// 提供 UTC 时间转换和单调时钟耗时计算。
/// </summary>
public static class TimeUtil
{
    public static DateTime UtcNow() => DateTime.UtcNow;

    public static DateTime Now() => DateTime.Now;

    public static long UnixNowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static long UnixNowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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
