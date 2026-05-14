using System.Collections.Concurrent;
using Shared.Time;

namespace Core.Components;

public interface ITokenRevocationStore : IServerComponent
{
    /// <summary>
    /// 将 tokenId 标记为撤销，直到过期时间。
    /// </summary>
    public void Revoke(string tokenId, DateTimeOffset expireAt);

    /// <summary>
    /// 检查 tokenId 是否已被撤销，并自动清理过期条目。
    /// </summary>
    public bool IsRevoked(string tokenId);
}

/// <summary>
/// 基于内存的撤销列表存储，适合单实例或开发环境。
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
        if (_revoked.TryGetValue(tokenId, out var expireAt))
        {
            if (TimeManager.UtcNow <= expireAt.UtcDateTime) return true;
            _revoked.TryRemove(tokenId, out _);
        }

        return false;
    }

    private void CleanupExpired()
    {
        var now = TimeManager.UtcNow;
        foreach (var item in _revoked)
        {
            if (item.Value.UtcDateTime < now)
            {
                _revoked.TryRemove(item.Key, out _);
            }
        }
    }
}
