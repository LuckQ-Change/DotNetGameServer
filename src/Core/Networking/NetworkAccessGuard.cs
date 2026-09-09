using System.Collections.Concurrent;
using System.Diagnostics;

using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Core.Networking;

/// <summary>
/// 统一管理连接级/IP 级限流、临时黑名单和过期状态清理。
/// </summary>
internal sealed class NetworkAccessGuard
{
    private static readonly TimeSpan BlacklistDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan InactiveRateLifetime = TimeSpan.FromMinutes(2);

    private readonly ServerRuntimeOptions _options;
    private readonly Action<string, LogLevel> _log;
    private readonly ConcurrentDictionary<string, DateTime> _blacklist = new();
    private readonly ConcurrentDictionary<string, SlidingRateWindow> _ipRates = new();
    private int _rateEventCount;

    public NetworkAccessGuard(ServerRuntimeOptions options, Action<string, LogLevel> log)
    {
        _options = options;
        _log = log;
    }

    /// <summary>为一条新连接创建独立的每连接限流计数器。</summary>
    public ConnectionRateLimiter CreateConnectionLimiter(string remoteEndpoint, string remoteIp) =>
        new(this, remoteEndpoint, remoteIp);

    /// <summary>判断 IP 是否仍处于临时封禁期，并顺手移除已过期记录。</summary>
    public bool IsBlacklisted(string remoteIp)
    {
        if (!_blacklist.TryGetValue(remoteIp, out var expiresAt)) return false;
        if (DateTime.UtcNow < expiresAt) return true;
        _blacklist.TryRemove(remoteIp, out _);
        return false;
    }

    /// <summary>
    /// 周期性汇总限流事件并清理过期网络状态；生命周期由服务器取消令牌控制。
    /// </summary>
    public async Task RunMaintenanceLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var nextCleanup = DateTime.UtcNow.AddMinutes(1);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var eventCount = Interlocked.Exchange(ref _rateEventCount, 0);
            if (eventCount > 0)
                _log($"限流事件: {eventCount}", LogLevel.Warning);

            if (DateTime.UtcNow < nextCleanup) continue;
            CleanupExpiredState();
            nextCleanup = DateTime.UtcNow.AddMinutes(1);
        }
    }

    private bool TryAddBytes(
        SlidingRateWindow connectionRate,
        int bytes,
        string remoteEndpoint,
        string remoteIp)
    {
        var connectionBytes = connectionRate.AddBytesAndGetRate(bytes);
        if (_options.IpMaxBytesPerSecond > 0)
        {
            var ipRate = _ipRates.GetOrAdd(remoteIp, static _ => new SlidingRateWindow());
            var ipBytes = ipRate.AddBytesAndGetRate(bytes);
            if (ipBytes > _options.IpMaxBytesPerSecond)
            {
                Reject(
                    remoteIp,
                    "ip_bytes_rate_limit_exceeded",
                    $"[IP限速-字节] {remoteEndpoint} {ipBytes}B/s > {_options.IpMaxBytesPerSecond}B/s，断开");
                return false;
            }
        }

        if (connectionBytes <= _options.MaxBytesPerSecond) return true;
        Reject(
            remoteIp,
            "bytes_rate_limit_exceeded",
            $"限速超限(字节): {remoteEndpoint} {connectionBytes}B/s > {_options.MaxBytesPerSecond}B/s，断开");
        return false;
    }

    private bool TryAddMessage(
        SlidingRateWindow connectionRate,
        string remoteEndpoint,
        string remoteIp)
    {
        var connectionMessages = connectionRate.AddMessagesAndGetRate(1);
        if (_options.IpMaxMessagesPerSecond > 0)
        {
            var ipRate = _ipRates.GetOrAdd(remoteIp, static _ => new SlidingRateWindow());
            var ipMessages = ipRate.AddMessagesAndGetRate(1);
            if (ipMessages > _options.IpMaxMessagesPerSecond)
            {
                Reject(
                    remoteIp,
                    "ip_message_rate_limit_exceeded",
                    $"[IP限速-频率] {remoteEndpoint} {ipMessages}/s > {_options.IpMaxMessagesPerSecond}/s，断开");
                return false;
            }
        }

        if (connectionMessages <= _options.MaxMessagesPerSecond) return true;
        Reject(
            remoteIp,
            "message_rate_limit_exceeded",
            $"限速超限(频率): {remoteEndpoint} {connectionMessages}/s > {_options.MaxMessagesPerSecond}/s，断开");
        return false;
    }

    private void Reject(string remoteIp, string reason, string message)
    {
        _log(message, LogLevel.Warning);
        Interlocked.Increment(ref _rateEventCount);
        _blacklist[remoteIp] = DateTime.UtcNow.Add(BlacklistDuration);
        _log($"加入黑名单: {remoteIp} 原因={reason} 时长={BlacklistDuration}", LogLevel.Information);
    }

    private void CleanupExpiredState()
    {
        var now = DateTime.UtcNow;
        foreach (var item in _blacklist)
        {
            if (item.Value <= now)
                _blacklist.TryRemove(item.Key, out _);
        }

        // IP 窗口仅保存最近一秒的数据，长期未活动的条目可直接移除。
        var inactiveBefore = now - InactiveRateLifetime;
        foreach (var item in _ipRates)
        {
            if (item.Value.LastActivityUtc <= inactiveBefore)
                _ipRates.TryRemove(item.Key, out _);
        }
    }

    /// <summary>绑定远端地址的连接级限流入口。</summary>
    internal sealed class ConnectionRateLimiter
    {
        private readonly NetworkAccessGuard _owner;
        private readonly string _remoteEndpoint;
        private readonly string _remoteIp;
        private readonly SlidingRateWindow _connectionRate = new();

        internal ConnectionRateLimiter(NetworkAccessGuard owner, string remoteEndpoint, string remoteIp)
        {
            _owner = owner;
            _remoteEndpoint = remoteEndpoint;
            _remoteIp = remoteIp;
        }

        public bool TryAddBytes(int bytes) =>
            _owner.TryAddBytes(_connectionRate, bytes, _remoteEndpoint, _remoteIp);

        public bool TryAddMessage() =>
            _owner.TryAddMessage(_connectionRate, _remoteEndpoint, _remoteIp);
    }

    /// <summary>
    /// 由十个 100ms 桶组成的一秒滑动窗口。
    /// 同一个 IP 可能被多条连接并发更新，因此所有推进和累加操作都在锁内完成。
    /// </summary>
    private sealed class SlidingRateWindow
    {
        private const int BucketCount = 10;
        private const int BucketMilliseconds = 100;

        private readonly int[] _messageBuckets = new int[BucketCount];
        private readonly int[] _byteBuckets = new int[BucketCount];
        private readonly object _gate = new();
        private int _currentBucketIndex;
        private long _bucketWindowStartTicks = Stopwatch.GetTimestamp();
        private long _lastActivityUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        public DateTime LastActivityUtc => DateTimeOffset.FromUnixTimeSeconds(
            Interlocked.Read(ref _lastActivityUnixSeconds)).UtcDateTime;

        public int AddBytesAndGetRate(int bytes)
        {
            lock (_gate)
            {
                MarkActiveAndAdvance();
                _byteBuckets[_currentBucketIndex] += bytes;
                return Sum(_byteBuckets);
            }
        }

        public int AddMessagesAndGetRate(int count)
        {
            lock (_gate)
            {
                MarkActiveAndAdvance();
                _messageBuckets[_currentBucketIndex] += count;
                return Sum(_messageBuckets);
            }
        }

        private void MarkActiveAndAdvance()
        {
            Interlocked.Exchange(ref _lastActivityUnixSeconds, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var elapsedMilliseconds =
                (Stopwatch.GetTimestamp() - _bucketWindowStartTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMilliseconds < BucketMilliseconds) return;

            var elapsedBuckets = (int)(elapsedMilliseconds / BucketMilliseconds);
            for (var index = 0; index < Math.Min(elapsedBuckets, BucketCount); index++)
            {
                _currentBucketIndex = (_currentBucketIndex + 1) % BucketCount;
                _messageBuckets[_currentBucketIndex] = 0;
                _byteBuckets[_currentBucketIndex] = 0;
            }

            _bucketWindowStartTicks = Stopwatch.GetTimestamp();
        }

        private static int Sum(int[] buckets)
        {
            var total = 0;
            for (var index = 0; index < buckets.Length; index++) total += buckets[index];
            return total;
        }
    }
}
