using System.Text.Json;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Core.Telemetry;
using StackExchange.Redis;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using MessagePack;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using Shared.Constants;

namespace DirectoryServer.Components;

public class RedisServerRegistryComponent : IServerRegistryComponent
{
    private readonly string _connectionString;
    private readonly string _keyPrefix;
    private readonly TimeSpan _offlineTtl = TimeSpan.FromSeconds(120);
    private readonly TimeSpan _removeTtl = TimeSpan.FromMinutes(10);
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromSeconds(10);
    private ConnectionMultiplexer? _redis;
    private IDatabase? _db;
    private CancellationTokenSource? _cts;
    private readonly Policy _redisPolicy;
    private readonly bool _useMessagePack;
    private readonly RedisSecurityOptions? _securityOptions;
    private static readonly string CheckTokenScriptText = "local v = redis.call('GET', KEYS[1]) \nif not v then return 0 end \nif v == ARGV[1] then \n  if ARGV[2] == '1' then redis.call('DEL', KEYS[1]) end \n  return 1 \nelse \n  return 0 \nend";
    private readonly LuaScript _checkTokenScript = LuaScript.Prepare(CheckTokenScriptText);
    private LoadedLuaScript? _checkTokenLoadedScript;

    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public string Name => "RedisServerRegistry";

    public RedisServerRegistryComponent(string connectionString, string keyPrefix = "registry", bool useMessagePack = false)
    {
        _connectionString = connectionString;
        _keyPrefix = keyPrefix.TrimEnd(':');
        _redisPolicy = CreatePolicy();
        _useMessagePack = useMessagePack || (Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRegistryUseMsgpack) == "1");
        _securityOptions = RedisSecurityOptions.FromEnvironment();
    }

    /// <summary>
    /// 可配置的构造函数：允许外部传入心跳离线判定 TTL、过期清理 TTL 与清理轮询间隔。
    /// </summary>
    public RedisServerRegistryComponent(
        string connectionString,
        string keyPrefix,
        TimeSpan? offlineTtl,
        TimeSpan? removeTtl,
        TimeSpan? cleanupInterval,
        RedisSecurityOptions? securityOptions = null)
        : this(connectionString, keyPrefix)
    {
        if (offlineTtl.HasValue) _offlineTtl = offlineTtl.Value;
        if (removeTtl.HasValue) _removeTtl = removeTtl.Value;
        if (cleanupInterval.HasValue) _cleanupInterval = cleanupInterval.Value;
        _redisPolicy = CreatePolicy();
        _securityOptions = securityOptions ?? _securityOptions;
    }

    public async Task InitializeAsync()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            // 如果提供了安全选项或连接串包含 ssl 指示，则走 ConfigurationOptions
            var useConfigOptions = _securityOptions != null || _connectionString.Contains("ssl=", StringComparison.OrdinalIgnoreCase);
            if (useConfigOptions)
            {
                var options = ConfigurationOptions.Parse(_connectionString);
                if (_securityOptions != null)
                {
                    if (_securityOptions.Ssl.HasValue) options.Ssl = _securityOptions.Ssl.Value;
                    if (!string.IsNullOrWhiteSpace(_securityOptions.SslHost)) options.SslHost = _securityOptions.SslHost;
                    if (_securityOptions.ClientCertificate != null)
                    {
                        var clientCert = _securityOptions.ClientCertificate;
                        options.CertificateSelection += delegate { return clientCert; };
                    }
                    if (_securityOptions.CertificateValidationOverride != null)
                    {
                        options.CertificateValidation += _securityOptions.CertificateValidationOverride;
                    }
                }
                _redis = await ConnectionMultiplexer.ConnectAsync(options);
            }
            else
            {
                _redis = await ConnectionMultiplexer.ConnectAsync(_connectionString);
            }
            _db = _redis.GetDatabase();
            _redisConnectTotal.Add(1);
            // 预加载令牌校验脚本，提高后续评估性能
            try
            {
                var endpoints = _redis.GetEndPoints();
                if (endpoints != null && endpoints.Length > 0)
                {
                    var server = _redis.GetServer(endpoints[0]);
                    _checkTokenLoadedScript = _checkTokenScript.Load(server);
                }
            }
            catch (Exception)
            {
                // Best-effort script preloading. If this fails, runtime evaluation still works.
                // We intentionally ignore exceptions here to avoid failing InitializeAsync.
            }
        }
        catch (Exception ex)
        {
            _redisConnectFailTotal.Add(1, new KeyValuePair<string, object?>(TagException, ex.GetType().Name));
            _redisErrorsTotal.Add(1, new KeyValuePair<string, object?>(TagOp, OpConnect), new KeyValuePair<string, object?>(TagException, ex.GetType().Name));
            _redisOpLatency.Record(sw.Elapsed.TotalSeconds, new KeyValuePair<string, object?>(TagOp, OpConnect));
            throw;
        }
        finally
        {
            _redisOpLatency.Record(sw.Elapsed.TotalSeconds, new KeyValuePair<string, object?>(TagOp, OpConnect));
        }
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => CleanupLoop(_cts.Token));
    }

    public Task DestroyAsync()
    {
        try { _cts?.Cancel(); } catch (Exception) { /* Best-effort cancellation during teardown. */ }
        _cts = null;
        try { _redis?.Dispose(); } catch (Exception) { /* Best-effort dispose; ignore failures during teardown. */ }
        _redis = null;
        _db = null;
        return Task.CompletedTask;
    }

    public void UpsertHeartbeat(string id, string type, string region, string host, int port, int playerCount, int capacity, string status, int avgRttMs)
    {
        if (_db == null) return;
        RedisExec("upsert_heartbeat", () =>
        {
            var rec = new ServerRecord(id, type, region, host, port, playerCount, capacity, status, DateTime.UtcNow, avgRttMs);
            var key1 = ServerKey(id);
            var zkey = ServersZSetKey();
            var payload = SerializeRecord(rec);
            var nowSec = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            // 使用 Lua 原子地写入记录（带 TTL）并更新 Sorted Set 的时间戳
            // KEYS[1] = server record key, KEYS[2] = servers zset key
            // ARGV[1] = payload, ARGV[2] = now epoch seconds, ARGV[3] = remove ttl seconds, ARGV[4] = server id
            var script = @"redis.call('SET', KEYS[1], ARGV[1], 'EX', ARGV[3]) \nredis.call('ZADD', KEYS[2], ARGV[2], ARGV[4]) \nreturn 1";
            _db!.ScriptEvaluate(script, new RedisKey[] { key1, zkey }, new RedisValue[] { payload, nowSec, (int)_removeTtl.TotalSeconds, id });
        });
    }

    public IEnumerable<ServerRecord> ListServers()
    {
        if (_db == null) yield break;
        PurgeExpired();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var entries = RedisExec("zrange_active", () => _db.SortedSetRangeByScoreWithScores(ServersZSetKey(), now - (long)_removeTtl.TotalSeconds, double.PositiveInfinity));
        if (entries == null || entries.Length == 0) yield break;
        // 批量读取记录值
        var keys = entries.Select(e => (RedisKey)ServerKey(e.Element!)).ToArray();
        var values = RedisExec("mget_servers", () => _db.StringGet(keys));
        for (int i = 0; i < entries.Length; i++)
        {
            var scoreSec = entries[i].Score;
            var val = values[i];
            if (val.IsNullOrEmpty) continue;
            var rec = DeserializeRecord(val);
            if (rec == null) continue;
            var isOnline = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)scoreSec) <= (long)_offlineTtl.TotalSeconds;
            yield return isOnline ? rec : rec with { Status = "offline" };
        }
    }

    public ServerRecord? GetServer(string id)
    {
        if (_db == null) return null;
        PurgeExpired();
        var rec = GetServerInternal(id);
        if (rec == null) return null;
        // 通过 ZSET 分数判断在线状态
        var score = RedisExec("zscore", () => _db.SortedSetScore(ServersZSetKey(), id));
        var isOnline = score.HasValue && (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)score.Value) <= (long)_offlineTtl.TotalSeconds;
        return isOnline ? rec : rec with { Status = "offline" };
    }

    public SwitchToken IssueSwitchToken(string userId, ServerRecord target, TimeSpan ttl)
    {
        var token = Guid.NewGuid().ToString("N");
        var expire = DateTime.UtcNow.Add(ttl);
        if (_db != null)
        {
            RedisExec("issue_switch_token", () =>
            {
                var key = TokenKey(userId, target.Id);
                _db.StringSet(key, token, ttl);
            });
            _switchTokenIssueTotal.Add(1,
                new KeyValuePair<string, object?>(TagUserId, userId),
                new KeyValuePair<string, object?>(TagServerId, target.Id));
        }
        return new SwitchToken(token, expire);
    }

    public bool CheckSwitchToken(string userId, string serverId, string tokenId, bool consume = false)
    {
        if (_db == null) return false;
        return RedisExec("check_switch_token", () =>
        {
            var key = TokenKey(userId, serverId);
            // 原子检查与可选消费
            // KEYS[1] = token key
            // ARGV[1] = token id, ARGV[2] = consume flag ('1' or '0')
            RedisResult result;
            if (_checkTokenLoadedScript != null)
            {
                // 使用预加载脚本（EVALSHA），参数通过匿名对象绑定
                result = _checkTokenLoadedScript.Evaluate(_db!, new { KEYS = new RedisKey[] { key }, ARGV = new RedisValue[] { tokenId, consume ? "1" : "0" } });
            }
            else
            {
                // 回退到直接评估脚本文本
                result = _db!.ScriptEvaluate(CheckTokenScriptText, new RedisKey[] { key }, new RedisValue[] { tokenId, consume ? "1" : "0" });
            }
            var s = result?.ToString();
            var ok = false;
            if (!string.IsNullOrEmpty(s))
            {
                if (long.TryParse(s, out var asLong)) ok = (asLong == 1L); else ok = (s == "1");
            }
            _switchTokenCheckTotal.Add(1,
                new KeyValuePair<string, object?>(TagUserId, userId),
                new KeyValuePair<string, object?>(TagServerId, serverId),
                new KeyValuePair<string, object?>(TagConsume, consume ? "true" : "false"),
                new KeyValuePair<string, object?>(TagResult, ok ? "success" : "fail"));
            return ok;
        });
    }

    private ServerRecord? GetServerInternal(string id)
    {
        if (_db == null) return null;
        var key = ServerKey(id);
        var val = RedisExec("string_get", () => _db.StringGet(key));
        if (val.IsNullOrEmpty) return null;
        return DeserializeRecord(val);
    }

    private void PurgeExpired()
    {
        if (_db == null) return;
        RedisExec("purge_expired", () =>
        {
            var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)_removeTtl.TotalSeconds;
            _db.SortedSetRemoveRangeByScore(ServersZSetKey(), double.NegativeInfinity, cutoff);
            // String 值已设置 EX TTL，会自行过期，无需逐个删除 Key
        });
    }

    private async Task CleanupLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                PurgeExpired();
            }
            catch
            {
                // ignore
            }
            try
            {
                await Task.Delay(_cleanupInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private string ServersZSetKey() => $"{_keyPrefix}:servers"; // 现在使用 ZSET 存储（名称保持不变，便于迁移）
    private string ServerKey(string id) => $"{_keyPrefix}:server:{id}";
    private string TokenKey(string userId, string serverId) => $"{_keyPrefix}:token:{userId}:{serverId}";

    private RedisValue SerializeRecord(ServerRecord rec)
    {
        if (_useMessagePack)
        {
            var bytes = MessagePackSerializer.Serialize(rec, MessagePack.MessagePackSerializerOptions.Standard.WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance));
            return bytes;
        }
        return JsonSerializer.Serialize(rec, _jsonOptions);
    }

    private ServerRecord? DeserializeRecord(RedisValue val)
    {
        try
        {
            // 先尝试按 JSON 字符串反序列化（兼容历史数据）
            string? jsonStr = val;
            if (!string.IsNullOrEmpty(jsonStr))
            {
                try
                {
                    return JsonSerializer.Deserialize<ServerRecord>(jsonStr!, _jsonOptions);
                }
                catch { /* 尝试 MessagePack */ }
            }

            // 尝试按 MessagePack 二进制反序列化
            var bytes = (byte[]?)val;
            if (bytes != null && bytes.Length > 0)
            {
                try
                {
                    return MessagePackSerializer.Deserialize<ServerRecord>(bytes, MessagePack.MessagePackSerializerOptions.Standard.WithResolver(MessagePack.Resolvers.ContractlessStandardResolver.Instance));
                }
                catch { /* no-op */ }
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // Metrics
    // 常量化单位/标签键，避免魔法字符串
    private const string UnitSeconds = "s";
    private const string UnitCount = "count";
    private const string TagOp = "op";
    private const string TagException = "exception";
    private const string TagUserId = "user_id";
    private const string TagServerId = "server_id";
    private const string TagConsume = "consume";
    private const string TagResult = "result";
    private const string OpConnect = "connect";
    private const string OpUnknown = "unknown";
    private const string OpCircuitBreak = "circuit_break";

    private static readonly Histogram<double> _redisOpLatency = CoreTelemetry.Meter.CreateHistogram<double>(
        "directory_redis_op_latency_seconds", unit: UnitSeconds, description: "目录服务对 Redis 的操作耗时");
    private static readonly Counter<long> _redisErrorsTotal = CoreTelemetry.Meter.CreateCounter<long>(
        "directory_redis_errors_total", unit: UnitCount, description: "目录服务对 Redis 的操作异常次数");
    private static readonly Counter<long> _redisConnectTotal = CoreTelemetry.Meter.CreateCounter<long>(
        "directory_redis_connect_total", unit: UnitCount, description: "Redis 连接成功次数");
    private static readonly Counter<long> _redisConnectFailTotal = CoreTelemetry.Meter.CreateCounter<long>(
        "directory_redis_connect_fail_total", unit: UnitCount, description: "Redis 连接失败次数");
    private static readonly Counter<long> _switchTokenIssueTotal = CoreTelemetry.Meter.CreateCounter<long>(
        "directory_switch_token_issue_total", unit: UnitCount, description: "发放切服令牌次数");
    private static readonly Counter<long> _switchTokenCheckTotal = CoreTelemetry.Meter.CreateCounter<long>(
        "directory_switch_token_check_total", unit: UnitCount, description: "校验切服令牌次数");

    private void RedisExec(string op, Action action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var ctx = new Context();
            ctx[TagOp] = op;
            _redisPolicy.Execute(_ => action(), ctx);
        }
        catch (Exception ex)
        {
            _redisErrorsTotal.Add(1, new KeyValuePair<string, object?>(TagOp, op), new KeyValuePair<string, object?>(TagException, ex.GetType().Name));
            throw;
        }
        finally
        {
            _redisOpLatency.Record(sw.Elapsed.TotalSeconds, new KeyValuePair<string, object?>(TagOp, op));
        }
    }

    private T RedisExec<T>(string op, Func<T> func)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var ctx = new Context();
            ctx[TagOp] = op;
            return _redisPolicy.Execute(_ => func(), ctx);
        }
        catch (Exception ex)
        {
            _redisErrorsTotal.Add(1, new KeyValuePair<string, object?>(TagOp, op), new KeyValuePair<string, object?>(TagException, ex.GetType().Name));
            throw;
        }
        finally
        {
            _redisOpLatency.Record(sw.Elapsed.TotalSeconds, new KeyValuePair<string, object?>(TagOp, op));
        }
    }

    private Policy CreatePolicy()
    {
        // 重试：指数退避，针对常见 Redis 异常
        var retry = Policy
            .Handle<RedisConnectionException>()
            .Or<RedisTimeoutException>()
            .Or<RedisServerException>()
            .Or<Exception>()
            .WaitAndRetry(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(100 * Math.Pow(2, attempt)),
                onRetry: (ex, ts, attempt, ctx) =>
                {
                    _redisErrorsTotal.Add(1, new KeyValuePair<string, object?>(TagOp, ctx != null && ctx.ContainsKey(TagOp) ? ctx[TagOp] : OpUnknown), new KeyValuePair<string, object?>(TagException, ex.GetType().Name));
                });

        // 断路器：连续 5 次失败后熔断 30s
        var breaker = Policy
            .Handle<Exception>()
            .CircuitBreaker(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, ts) =>
                {
                    _redisErrorsTotal.Add(1, new KeyValuePair<string, object?>(TagOp, OpCircuitBreak), new KeyValuePair<string, object?>(TagException, ex.GetType().Name));
                },
                onReset: () => { /* no-op */ },
                onHalfOpen: () => { /* no-op */ }
            );

        // 超时：每次 Redis 操作最多 500ms（可根据实际负载调整）
        var timeout = Policy.Timeout(TimeSpan.FromMilliseconds(500), TimeoutStrategy.Pessimistic);

        // Wrap 顺序：断路器（外）-> 重试（中）-> 超时（内）
        // 若发生超时会抛出 TimeoutRejectedException，被重试捕获；断路器在高失败率时快速失败。
        return Policy.Wrap(breaker, retry, timeout);
    }

    public class RedisSecurityOptions
    {
        public bool? Ssl { get; init; }
        public string? SslHost { get; init; }
        public X509Certificate2? ClientCertificate { get; init; }
        public RemoteCertificateValidationCallback? CertificateValidationOverride { get; init; }

        public static RedisSecurityOptions? FromEnvironment()
        {
            try
            {
                var sslEnv = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRedisSsl);
                var sslHostEnv = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRedisSslHost);
                var certPathEnv = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRedisClientCertPath);
                var certPwdEnv = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRedisClientCertPassword);
                var validateEnv = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRedisValidateServerCert);

                bool hasAny = !string.IsNullOrWhiteSpace(sslEnv) || !string.IsNullOrWhiteSpace(sslHostEnv)
                              || !string.IsNullOrWhiteSpace(certPathEnv) || !string.IsNullOrWhiteSpace(validateEnv);
                if (!hasAny) return null;

                bool? sslOpt = null;
                if (bool.TryParse(sslEnv, out var sslParsed)) sslOpt = sslParsed;

                X509Certificate2? cert = null;
                if (!string.IsNullOrWhiteSpace(certPathEnv))
                {
                    cert = string.IsNullOrWhiteSpace(certPwdEnv)
                        ? new X509Certificate2(certPathEnv)
                        : new X509Certificate2(certPathEnv, certPwdEnv);
                }

                RemoteCertificateValidationCallback? validation = null;
                if (!string.IsNullOrWhiteSpace(validateEnv) && bool.TryParse(validateEnv, out var validateFlag))
                {
                    validation = (sender, certificate, chain, errors) =>
                    {
                        if (validateFlag)
                        {
                            return errors == SslPolicyErrors.None;
                        }
                        // 允许跳过验证（仅测试环境使用）
                        return true;
                    };
                }

                return new RedisSecurityOptions
                {
                    Ssl = sslOpt,
                    SslHost = sslHostEnv,
                    ClientCertificate = cert,
                    CertificateValidationOverride = validation
                };
            }
            catch
            {
                return null;
            }
        }
    }
}