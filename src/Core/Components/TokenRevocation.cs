using System.Collections.Concurrent;

namespace Core.Components;

public interface ITokenRevocationStore : IServerComponent
{
    /// <summary>将 tokenId 标记为撤销，直到过期时间。</summary>
    void Revoke(string tokenId, DateTimeOffset expireAt);

    /// <summary>检查 tokenId 是否已被撤销（自动清理过期条目）。</summary>
    bool IsRevoked(string tokenId);
}

/// <summary>
/// 基于内存的撤销列表存储，适合单实例或开发环境；按到期时间自动清理。
/// </summary>
public sealed class InMemoryTokenRevocationStore : ITokenRevocationStore
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new();
    public string Name => "InMemoryTokenRevocation";

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync() => Task.CompletedTask;

    public void Revoke(string tokenId, DateTimeOffset expireAt)
    {
        _revoked[tokenId] = expireAt;
        CleanupExpired();
    }

    public bool IsRevoked(string tokenId)
    {
        CleanupExpired();
        if (_revoked.TryGetValue(tokenId, out var exp))
        {
            if (DateTimeOffset.UtcNow <= exp) return true;
            _revoked.TryRemove(tokenId, out _);
        }
        return false;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var kv in _revoked)
        {
            if (kv.Value < now) _revoked.TryRemove(kv.Key, out _);
        }
    }
}

