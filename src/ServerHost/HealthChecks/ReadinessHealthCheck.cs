using System.Net.Sockets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServerHost.HealthChecks
{
    /// <summary>
    /// Readiness 探针：检查外部依赖（数据库、Redis 注册表等）。
    /// </summary>
    public class ReadinessHealthCheck : IHealthCheck
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;

        public ReadinessHealthCheck(IServiceProvider services, IConfiguration configuration)
        {
            _services = services;
            _configuration = configuration;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var failures = new List<string>();

            // 1) 数据库可达性（生产/非内存模式）
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetService<Core.Db.GameDbContext>();
                if (db != null)
                {
                    var can = await db.Database.CanConnectAsync(cancellationToken);
                    if (!can) failures.Add("db: cannot connect");
                }
            }
            catch (Exception ex)
            {
                failures.Add($"db: {ex.Message}");
            }

            // 2) Redis（当目录注册表为 redis 时）
            try
            {
                var mode = (_configuration[Shared.Constants.ConfigKeys.DirectoryRegistryModeLower]
                           ?? Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.DirectoryRegistryEnv)
                           ?? Shared.Constants.ConfigKeys.DirectoryRegistryModeValueMemory).ToLowerInvariant();
                if (mode == Shared.Constants.ConfigKeys.DirectoryRegistryModeValueRedis)
                {
                    var conn = _configuration[Shared.Constants.ConfigKeys.DirectoryRegistryRedisConnectionStringLower]
                               ?? Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.RedisConnectionEnv);
                    if (string.IsNullOrWhiteSpace(conn))
                    {
                        failures.Add("redis: REDIS_CONNECTION missing");
                    }
                    else
                    {
                        // 从 "host:port,..." 抽取 host 与 port
                        var first = conn.Split(',')[0];
                        var hp = first.Split(':');
                        var host = hp[0];
                        var port = hp.Length > 1 && int.TryParse(hp[1], out var p) ? p : 6379;
                        using var tcp = new TcpClient();
                        var connectTask = tcp.ConnectAsync(host, port);
                        var timeout = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                        var done = await Task.WhenAny(connectTask, timeout);
                        if (done != connectTask || !tcp.Connected)
                        {
                            failures.Add($"redis: cannot connect {host}:{port} (timeout)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                failures.Add($"redis: {ex.Message}");
            }

            if (failures.Count == 0) return HealthCheckResult.Healthy("ready");
            return HealthCheckResult.Unhealthy(string.Join("; ", failures));
        }
    }
}

