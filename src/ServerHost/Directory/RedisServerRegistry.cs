using System.Text.Json;
using Core.Directory;
using StackExchange.Redis;

namespace ServerHost.Directory;

public class RedisServerRegistry : IServerRegistry
{
    private readonly string _connectionString;
    private readonly int _cleanupIntervalSeconds;
    private ConnectionMultiplexer? _redis;
    private IServer? _server;
    private readonly string _keyPrefix = "ds:servers:";

    public string Name => "RedisServerRegistry";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public RedisServerRegistry(string connectionString, int cleanupIntervalSeconds = 10)
    {
        _connectionString = connectionString;
        _cleanupIntervalSeconds = Math.Max(1, cleanupIntervalSeconds);
    }

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(_connectionString);
        _server = _redis.GetServer(_redis.GetEndPoints()[0]);
    }

    public Task DestroyAsync()
    {
        _redis?.Dispose();
        return Task.CompletedTask;
    }

    public async Task<bool> RegisterAsync(ServerRegistration registration)
    {
        var db = _redis!.GetDatabase();
        registration.LastHeartbeatUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var json = JsonSerializer.Serialize(registration, JsonOptions);
        var key = $"{_keyPrefix}{registration.ServerId}";
        await db.StringSetAsync(key, json, TimeSpan.FromSeconds(_cleanupIntervalSeconds * 3));
        return true;
    }

    public async Task<bool> UnregisterAsync(string serverId)
    {
        var db = _redis!.GetDatabase();
        var key = $"{_keyPrefix}{serverId}";
        return await db.KeyDeleteAsync(key);
    }

    public async Task<ServerRegistration?> GetAsync(string serverId)
    {
        var db = _redis!.GetDatabase();
        var json = await db.StringGetAsync($"{_keyPrefix}{serverId}");
        return json.HasValue ? JsonSerializer.Deserialize<ServerRegistration>(json!, JsonOptions) : null;
    }

    public async Task<List<ServerRegistration>> QueryAsync(string? serverType = null, string? region = null)
    {
        var all = await GetAllAsync();
        var result = all.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(serverType))
            result = result.Where(s => string.Equals(s.ServerType, serverType, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(region))
            result = result.Where(s => string.Equals(s.Region, region, StringComparison.OrdinalIgnoreCase));
        return result.ToList();
    }

    public async Task<List<ServerRegistration>> GetAllAsync()
    {
        var db = _redis!.GetDatabase();
        var keys = _server!.Keys(pattern: $"{_keyPrefix}*").ToArray();
        if (keys.Length == 0) return new List<ServerRegistration>();
        var values = await db.StringGetAsync(keys);
        var result = new List<ServerRegistration>(values.Length);
        foreach (var val in values)
        {
            if (val.HasValue)
            {
                var reg = JsonSerializer.Deserialize<ServerRegistration>(val!, JsonOptions);
                if (reg != null) result.Add(reg);
            }
        }
        return result;
    }

    public async Task<int> CleanupStaleAsync(int offlineTtlSeconds, int removeTtlSeconds)
    {
        var db = _redis!.GetDatabase();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var removed = 0;
        var keys = _server!.Keys(pattern: $"{_keyPrefix}*").ToArray();
        foreach (var key in keys)
        {
            var json = await db.StringGetAsync(key);
            if (!json.HasValue) continue;
            var reg = JsonSerializer.Deserialize<ServerRegistration>(json!, JsonOptions);
            if (reg == null) continue;
            var age = now - reg.LastHeartbeatUnix;
            if (age > removeTtlSeconds)
            {
                await db.KeyDeleteAsync(key);
                removed++;
            }
        }
        return removed;
    }
}
