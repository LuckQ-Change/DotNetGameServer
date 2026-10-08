using System.Collections.Concurrent;
namespace Core.Components;

public interface ITokenRevocationStore : IServerComponent
{
    /// <summary>
    /// 将 tokenId 标记为撤销，直到过期时间。
    /// </summary>
    public void Revoke(string tokenId, DateTimeOffset expireAt);

    public ValueTask RevokeAsync(
        string tokenId,
        DateTimeOffset expireAt,
        CancellationToken cancellationToken = default)
    {
        Revoke(tokenId, expireAt);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 检查 tokenId 是否已被撤销，并自动清理过期条目。
    /// </summary>
    public bool IsRevoked(string tokenId);

    public ValueTask<bool> IsRevokedAsync(
        string tokenId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsRevoked(tokenId));
}

/// <summary>
/// 基于内存的撤销列表存储，适合单实例或开发环境。
/// </summary>
public sealed class InMemoryTokenRevocationStore : ITokenRevocationStore
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new();
    private PeriodicTimer? _cleanupTimer;
    private Task? _cleanupTask;

    public string Name => "InMemoryTokenRevocation";

    public Task InitializeAsync()
    {
        _cleanupTimer = new PeriodicTimer(CleanupInterval);
        _cleanupTask = RunCleanupAsync(_cleanupTimer);
        return Task.CompletedTask;
    }

    public async Task DestroyAsync()
    {
        if (_cleanupTimer is null) return;
        _cleanupTimer.Dispose();
        if (_cleanupTask is null) return;
        try
        {
            await _cleanupTask;
        }
        catch (ObjectDisposedException)
        {
            // 定时器释放后，等待中的清理循环会结束。
        }
    }

    public void Revoke(string tokenId, DateTimeOffset expireAt) => _revoked[tokenId] = expireAt;

    public bool IsRevoked(string tokenId)
    {
        if (!_revoked.TryGetValue(tokenId, out var expireAt)) return false;
        if (DateTime.UtcNow <= expireAt.UtcDateTime) return true;
        _revoked.TryRemove(tokenId, out _);
        return false;
    }

    private async Task RunCleanupAsync(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync())
                RemoveExpired();
        }
        catch (ObjectDisposedException)
        {
            // 服务器停止时释放定时器。
        }
    }

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var item in _revoked)
        {
            if (item.Value.UtcDateTime < now)
                _revoked.TryRemove(item.Key, out _);
        }
    }
}
