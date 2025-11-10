using System;
using System.Diagnostics;
using System.Globalization;

namespace Shared.Time;

/// <summary>
/// 轻量级的时间戳与时间格式化/转换工具。
/// 全为纯函数，不包含全局可变状态。
/// </summary>
public static class TimeUtil
{
	/// <summary>
	/// 当前 UTC 时间。
	/// </summary>
	public static DateTime UtcNow() => DateTime.UtcNow;

	/// <summary>
	/// 当前本地时间。
	/// </summary>
	public static DateTime Now() => DateTime.Now;

	/// <summary>
	/// 当前 Unix 时间戳（秒）。
	/// </summary>
	public static long UnixNowSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	/// <summary>
	/// 当前 Unix 时间戳（毫秒）。
	/// </summary>
	public static long UnixNowMilliseconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	/// <summary>
	/// 将 UTC 时间转换为 Unix 秒。
	/// </summary>
	public static long ToUnixSeconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
	public static long ToUnixSeconds(DateTimeOffset dto) => dto.ToUnixTimeSeconds();

	/// <summary>
	/// 将 UTC 时间转换为 Unix 毫秒。
	/// </summary>
	public static long ToUnixMilliseconds(DateTime utc) => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
	public static long ToUnixMilliseconds(DateTimeOffset dto) => dto.ToUnixTimeMilliseconds();

	/// <summary>
	/// 将 Unix 秒转换为 UTC 时间。
	/// </summary>
	public static DateTimeOffset FromUnixSeconds(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

	/// <summary>
	/// 将 Unix 毫秒转换为 UTC 时间。
	/// </summary>
	public static DateTimeOffset FromUnixMilliseconds(long milliseconds) => DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);

	/// <summary>
	/// 按 ISO8601（UTC，结尾为 'Z'）格式化。
	/// </summary>
	public static string FormatIso8601Utc(DateTime utc)
	{
		var dt = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
		return dt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
	}

	/// <summary>
	/// 解析 ISO8601 字符串。支持 'Z' 或带偏移；返回 UTC 的 DateTimeOffset。
	/// 非法输入会抛出异常。
	/// </summary>
	public static DateTimeOffset ParseIso8601(string iso8601)
	{
		return DateTimeOffset.Parse(iso8601, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
	}

	/// <summary>
	/// 使用高精度单调时钟计算耗时（毫秒）。
	/// </summary>
	public static long MonotonicElapsedMs(long startTimestamp)
	{
		var dt = Stopwatch.GetTimestamp() - startTimestamp;
		return (long)(dt * 1000.0 / Stopwatch.Frequency);
	}

	/// <summary>
	/// 获取当前单调时钟计数（Stopwatch ticks）。
	/// </summary>
	public static long MonotonicNowTicks() => Stopwatch.GetTimestamp();
}


