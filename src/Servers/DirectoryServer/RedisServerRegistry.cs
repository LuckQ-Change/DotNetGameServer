using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Core.Configuration;
using Core.Directory;
using StackExchange.Redis;

namespace DirectoryServer;

public class RedisServerRegistry : IServerRegistry
{
    private readonly ConfigurationOptions _configOptions;
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
        : this(connectionString, null, cleanupIntervalSeconds)
    {
    }

    /// <summary>
    /// 支持 SSL/mTLS 配置的 Redis 服务器注册表构造函数。
    /// 修复：原实现仅使用连接字符串，忽略配置中的 SSL/证书选项。
    /// </summary>
    /// <param name="connectionString">Redis 连接字符串</param>
    /// <param name="sslOptions">SSL 配置选项，可为 null</param>
    /// <param name="cleanupIntervalSeconds">清理间隔秒数</param>
    public RedisServerRegistry(string connectionString, RedisRegistryOptions? sslOptions, int cleanupIntervalSeconds = 10)
    {
        _cleanupIntervalSeconds = Math.Max(1, cleanupIntervalSeconds);

        _configOptions = ConfigurationOptions.Parse(connectionString);

        // SSL/mTLS 配置
        if (sslOptions is { Ssl: true })
        {
            _configOptions.Ssl = true;
            if (!string.IsNullOrWhiteSpace(sslOptions.SslHost))
                _configOptions.SslHost = sslOptions.SslHost;
            _configOptions.SslProtocols = System.Security.Authentication.SslProtocols.Tls12
                                           | System.Security.Authentication.SslProtocols.Tls13;
            
            // 服务器证书验证
            _configOptions.CertificateValidation += (_, _, _, policyErrors) =>
                !sslOptions.ValidateServerCertificate || policyErrors == SslPolicyErrors.None;

            // 客户端证书
            if (!string.IsNullOrWhiteSpace(sslOptions.ClientCertificate.Path))
            {
                var cert = string.IsNullOrWhiteSpace(sslOptions.ClientCertificate.Password)
                    ? new X509Certificate2(sslOptions.ClientCertificate.Path)
                    : new X509Certificate2(sslOptions.ClientCertificate.Path,
                        sslOptions.ClientCertificate.Password);
                _configOptions.CertificateSelection += (_, _, _, _, _) => cert;
            }
        }
    }

    public async Task InitializeAsync()
    {
        _redis = await ConnectionMultiplexer.ConnectAsync(_configOptions);
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
