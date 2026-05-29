using Core.Components;
using StackExchange.Redis;

namespace DirectoryServer;

/// <summary>
/// 可选 Redis 连接的包装器，避免在 DI 中注册 nullable 接口类型。
/// </summary>
public sealed class RedisConnectionHolder(IConnectionMultiplexer? connection)
{
    public IConnectionMultiplexer? Connection => connection;
}

/// <summary>
/// 基于 Redis 的 Token 吊销存储，适合多实例部署场景。
/// 吊销记录写入 Redis，所有实例共享同一视图，彻底消除内存实现跨节点失效的问题。
/// </summary>
public sealed class RedisTokenRevocationStore : ITokenRevocationStore
{
    private readonly IConnectionMultiplexer _redis;
    private const string KeyPrefix = "revoked:";

    public string Name => "RedisTokenRevocation";

    public RedisTokenRevocationStore(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DestroyAsync() => Task.CompletedTask;

    public void Revoke(string tokenId, DateTimeOffset expireAt)
    {
        var db = _redis.GetDatabase();
        var ttl = expireAt.UtcDateTime - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero) return;
        db.StringSet($"{KeyPrefix}{tokenId}", "1", ttl, flags: CommandFlags.FireAndForget);
    }

    public bool IsRevoked(string tokenId)
    {
        var db = _redis.GetDatabase();
        return db.KeyExists($"{KeyPrefix}{tokenId}");
    }
}
