using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Core.Telemetry;

namespace DirectoryServer.Components;

public class InMemoryServerRegistryComponent : IServerRegistryComponent
{
    private readonly ConcurrentDictionary<string, ServerRecord> _servers = new();
    private readonly ConcurrentDictionary<string, SwitchToken> _tokens = new();
    private readonly TimeSpan _offlineTtl = TimeSpan.FromSeconds(120);
    private readonly TimeSpan _removeTtl = TimeSpan.FromMinutes(10);
    private CancellationTokenSource? _cts;

    public string Name => "ServerRegistry";

    // Metrics
    private const string UnitCount = "count";
    private const string TagUserId = "user_id";
    private const string TagServerId = "server_id";
    private const string TagConsume = "consume";
    private const string TagResult = "result";
    private static readonly Counter<long> _switchTokenIssueTotal = CoreTelemetry.Meter.CreateCounter<long>(
        "directory_switch_token_issue_total", unit: UnitCount, description: "发放切服令牌次数");
    private static readonly Counter<long> _switchTokenCheckTotal = CoreTelemetry.Meter.CreateCounter<long>(
        "directory_switch_token_check_total", unit: UnitCount, description: "校验切服令牌次数");

    public Task InitializeAsync()
    {
        _servers.Clear();
        _tokens.Clear();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => CleanupLoop(_cts.Token));
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        try { _cts?.Cancel(); } catch (Exception) { /* Best-effort cancellation; ignore during teardown. */ }
        _cts = null;
        _servers.Clear();
        _tokens.Clear();
        return Task.CompletedTask;
    }

    public void UpsertHeartbeat(string id, string type, string region, string host, int port, int playerCount, int capacity, string status, int avgRttMs)
    {
        var rec = new ServerRecord(id, type, region, host, port, playerCount, capacity, status, DateTime.UtcNow, avgRttMs);
        _servers[id] = rec;
    }

    public IEnumerable<ServerRecord> ListServers()
    {
        PurgeExpired();
        var now = DateTime.UtcNow;
        foreach (var rec in _servers.Values)
        {
            var isOnline = (now - rec.LastHeartbeatUtc) <= _offlineTtl;
            yield return isOnline ? rec : rec with { Status = "offline" };
        }
    }

    public ServerRecord? GetServer(string id)
    {
        PurgeExpired();
        if (!_servers.TryGetValue(id, out var rec)) return null;
        var now = DateTime.UtcNow;
        var isOnline = (now - rec.LastHeartbeatUtc) <= _offlineTtl;
        return isOnline ? rec : rec with { Status = "offline" };
    }

    public SwitchToken IssueSwitchToken(string userId, ServerRecord target, TimeSpan ttl)
    {
        // 简单令牌：随机值 + 过期时间（实际可HMAC签名）
        var token = Guid.NewGuid().ToString("N");
        var expire = DateTime.UtcNow.Add(ttl);
        var st = new SwitchToken(token, expire);
        _tokens[$"{userId}:{target.Id}"] = st;
        _switchTokenIssueTotal.Add(1,
            new KeyValuePair<string, object?>(TagUserId, userId),
            new KeyValuePair<string, object?>(TagServerId, target.Id));
        return st;
    }

    public bool CheckSwitchToken(string userId, string serverId, string tokenId, bool consume = false)
    {
        PurgeExpired();
        var key = $"{userId}:{serverId}";
        if (!_tokens.TryGetValue(key, out var st)) return false;
        if (st.ExpireAtUtc <= DateTime.UtcNow) { _tokens.TryRemove(key, out _); return false; }
        var ok = string.Equals(st.Token, tokenId, StringComparison.Ordinal);
        if (ok && consume) _tokens.TryRemove(key, out _);
        _switchTokenCheckTotal.Add(1,
            new KeyValuePair<string, object?>(TagUserId, userId),
            new KeyValuePair<string, object?>(TagServerId, serverId),
            new KeyValuePair<string, object?>(TagConsume, consume ? "true" : "false"),
            new KeyValuePair<string, object?>(TagResult, ok ? "success" : "fail"));
        return ok;
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        // 清理过期令牌
        foreach (var kv in _tokens)
        {
            if (kv.Value.ExpireAtUtc <= now)
            {
                _tokens.TryRemove(kv.Key, out _);
            }
        }
        // 清理长期未心跳的服务器，避免无限增长
        foreach (var kv in _servers)
        {
            var rec = kv.Value;
            if (now - rec.LastHeartbeatUtc > _removeTtl)
            {
                _servers.TryRemove(kv.Key, out _);
            }
        }
    }

    private async Task CleanupLoop(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(10);
        while (!token.IsCancellationRequested)
        {
            try
            {
                PurgeExpired();
            }
            catch
            {
                // 忽略清理异常，防止中断循环
            }
            try
            {
                await Task.Delay(interval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}