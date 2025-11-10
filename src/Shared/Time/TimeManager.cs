using System;
using System.Threading;

namespace Shared.Time;

/// <summary>
/// 时间获取抽象接口，便于测试/模拟场景对时间进行控制。
/// </summary>
public interface ITimeProvider
{
	DateTime UtcNow { get; }
	DateTime Now { get; }
	long UnixSeconds { get; }
	long UnixMilliseconds { get; }
}

/// <summary>
/// 使用系统时钟的默认提供者。
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider
{
	public DateTime UtcNow => DateTime.UtcNow;
	public DateTime Now => DateTime.Now;
	public long UnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
	public long UnixMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// 手动控制时间的提供者（适用于测试/模拟），线程安全。
/// </summary>
public sealed class ManualTimeProvider : ITimeProvider
{
	private readonly object _lock = new();
	private DateTimeOffset _utcBase;

	public ManualTimeProvider(DateTimeOffset utcBase)
	{
		_utcBase = utcBase.ToUniversalTime();
	}

	public DateTime UtcNow { get { lock (_lock) return _utcBase.UtcDateTime; } }
	public DateTime Now { get { lock (_lock) return _utcBase.LocalDateTime; } }
	public long UnixSeconds { get { lock (_lock) return _utcBase.ToUnixTimeSeconds(); } }
	public long UnixMilliseconds { get { lock (_lock) return _utcBase.ToUnixTimeMilliseconds(); } }

	public void SetUtc(DateTimeOffset utc)
	{
		lock (_lock) { _utcBase = utc.ToUniversalTime(); }
	}
	public void Advance(TimeSpan delta)
	{
		lock (_lock) { _utcBase = _utcBase.Add(delta); }
	}
}

/// <summary>
/// 全局时间管理器：可替换的时间提供者，支持作用域内临时覆盖。
/// </summary>
public static class TimeManager
{
	private static readonly object _lock = new();
	private static ITimeProvider _provider = new SystemTimeProvider();

	public static ITimeProvider Provider
	{
		get { lock (_lock) return _provider; }
		set { lock (_lock) _provider = value ?? throw new ArgumentNullException(nameof(value)); }
	}

	// Convenience accessors
	/// <summary>当前 UTC 时间。</summary>
	public static DateTime UtcNow => Provider.UtcNow;
	/// <summary>当前本地时间。</summary>
	public static DateTime Now => Provider.Now;
	/// <summary>当前 Unix 秒。</summary>
	public static long UnixSeconds => Provider.UnixSeconds;
	/// <summary>当前 Unix 毫秒。</summary>
	public static long UnixMilliseconds => Provider.UnixMilliseconds;

	/// <summary>
	/// 在 using 作用域内临时覆盖时间提供者。
	/// 作用域结束后自动恢复。
	/// </summary>
	public static IDisposable Override(ITimeProvider provider)
	{
		if (provider == null) throw new ArgumentNullException(nameof(provider));
		lock (_lock)
		{
			var previous = _provider;
			_provider = provider;
			return new Revert(() =>
			{
				lock (_lock) { _provider = previous; }
			});
		}
	}

	private sealed class Revert : IDisposable
	{
		private Action? _action;
		public Revert(Action action) { _action = action; }
		public void Dispose()
		{
			var a = Interlocked.Exchange(ref _action, null);
			a?.Invoke();
		}
	}
}


