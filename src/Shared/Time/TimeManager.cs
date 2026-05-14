namespace Shared.Time;

/// <summary>
/// 时间提供者接口，统一抽象逻辑时间来源。
/// </summary>
public interface ITimeProvider
{
    public DateTime UtcNow { get; }
    public DateTime Now { get; }
    public long UnixSeconds { get; }
    public long UnixMilliseconds { get; }
}

/// <summary>
/// 使用系统时钟的默认实现。
/// </summary>
public sealed class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime Now => DateTime.Now;
    public long UnixSeconds => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public long UnixMilliseconds => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// 基于偏移量的时间提供者，适合 GM 调试服务器时间。
/// </summary>
public sealed class OffsetTimeProvider(TimeSpan offset) : ITimeProvider
{
    public TimeSpan Offset { get; } = offset;

    public DateTime UtcNow => DateTime.UtcNow.Add(Offset);
    public DateTime Now => DateTime.Now.Add(Offset);
    public long UnixSeconds => new DateTimeOffset(UtcNow).ToUnixTimeSeconds();
    public long UnixMilliseconds => new DateTimeOffset(UtcNow).ToUnixTimeMilliseconds();
}

/// <summary>
/// 手动固定时间的提供者，适合测试或特殊模拟场景。
/// </summary>
public sealed class ManualTimeProvider : ITimeProvider
{
    private readonly object _lock = new();
    private DateTimeOffset _utcBase;

    public ManualTimeProvider(DateTimeOffset utcBase)
    {
        _utcBase = utcBase.ToUniversalTime();
    }

    public DateTime UtcNow
    {
        get { lock (_lock) return _utcBase.UtcDateTime; }
    }

    public DateTime Now
    {
        get { lock (_lock) return _utcBase.LocalDateTime; }
    }

    public long UnixSeconds
    {
        get { lock (_lock) return _utcBase.ToUnixTimeSeconds(); }
    }

    public long UnixMilliseconds
    {
        get { lock (_lock) return _utcBase.ToUnixTimeMilliseconds(); }
    }

    public void SetUtc(DateTimeOffset utc)
    {
        lock (_lock)
        {
            _utcBase = utc.ToUniversalTime();
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_lock)
        {
            _utcBase = _utcBase.Add(delta);
        }
    }
}

/// <summary>
/// 时间管理器，统一管理逻辑时间入口，并支持系统时间、偏移时间与手动时间三种模式。
/// </summary>
public static class TimeManager
{
    private static readonly object _lock = new();
    private static ITimeProvider _provider = new SystemTimeProvider();

    public static ITimeProvider Provider
    {
        get
        {
            lock (_lock)
            {
                return _provider;
            }
        }
    }

    public static DateTime UtcNow => Provider.UtcNow;
    public static DateTime Now => Provider.Now;
    public static long UnixSeconds => Provider.UnixSeconds;
    public static long UnixMilliseconds => Provider.UnixMilliseconds;

    /// <summary>
    /// 返回当前时间模式快照，便于诊断与导出。
    /// </summary>
    public static TimeSnapshot GetSnapshot()
    {
        lock (_lock)
        {
            return BuildSnapshotUnsafe();
        }
    }

    /// <summary>
    /// 切回系统时间。
    /// </summary>
    public static void UseSystem()
    {
        lock (_lock)
        {
            _provider = new SystemTimeProvider();
        }
    }

    /// <summary>
    /// 以偏移方式设置逻辑时间，逻辑时间会继续自然流逝。
    /// </summary>
    public static void SetOffset(TimeSpan offset)
    {
        lock (_lock)
        {
            _provider = offset == TimeSpan.Zero
                ? new SystemTimeProvider()
                : new OffsetTimeProvider(offset);
        }
    }

    /// <summary>
    /// 将逻辑时间直接设置到指定 UTC 时间点，内部通过偏移量实现。
    /// </summary>
    public static void SetUtc(DateTimeOffset targetUtc)
    {
        var offset = targetUtc.UtcDateTime - DateTime.UtcNow;
        SetOffset(offset);
    }

    /// <summary>
    /// 推进逻辑时间。
    /// 对偏移模式会累加偏移量；对手动模式会推进固定时间。
    /// </summary>
    public static void Advance(TimeSpan delta)
    {
        lock (_lock)
        {
            switch (_provider)
            {
                case ManualTimeProvider manual:
                    manual.Advance(delta);
                    break;
                case OffsetTimeProvider offset:
                    _provider = new OffsetTimeProvider(offset.Offset.Add(delta));
                    break;
                default:
                    _provider = new OffsetTimeProvider(delta);
                    break;
            }
        }
    }

    /// <summary>
    /// 切换到手动固定时间模式。
    /// </summary>
    public static void UseManual(DateTimeOffset targetUtc)
    {
        lock (_lock)
        {
            _provider = new ManualTimeProvider(targetUtc);
        }
    }

    /// <summary>
    /// 在作用域内临时覆盖时间提供者。
    /// </summary>
    public static IDisposable Override(ITimeProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        lock (_lock)
        {
            var previous = _provider;
            _provider = provider;
            return new Revert(() =>
            {
                lock (_lock)
                {
                    _provider = previous;
                }
            });
        }
    }

    private static TimeSnapshot BuildSnapshotUnsafe()
    {
        var systemUtcNow = DateTime.UtcNow;
        var logicalUtcNow = _provider.UtcNow;
        var mode = _provider switch
        {
            OffsetTimeProvider => "offset",
            ManualTimeProvider => "manual",
            _ => "system"
        };

        var offset = logicalUtcNow - systemUtcNow;
        return new TimeSnapshot(
            mode,
            logicalUtcNow,
            systemUtcNow,
            offset,
            _provider.UnixSeconds,
            _provider.UnixMilliseconds);
    }

    private sealed class Revert(Action action) : IDisposable
    {
        private Action? _action = action;

        public void Dispose()
        {
            var current = Interlocked.Exchange(ref _action, null);
            current?.Invoke();
        }
    }
}

/// <summary>
/// 时间状态快照。
/// </summary>
public sealed record TimeSnapshot(
    string Mode,
    DateTime LogicalUtcNow,
    DateTime SystemUtcNow,
    TimeSpan Offset,
    long UnixSeconds,
    long UnixMilliseconds);
