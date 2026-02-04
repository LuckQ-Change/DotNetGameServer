using Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Core.Db;
using Microsoft.EntityFrameworkCore;
using ServerHost.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using LoginServer.Components;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Core.Telemetry;
using Core.Diagnostics;
using System.Net;

using ServerHost; // 访问命名空间中的类型（HostingUtilities、GameServerService）

var builder = WebApplication.CreateBuilder(args);

// 预解析命令行：统一抽取 service/port/healthCheckUrl 到配置键
static IDictionary<string, string> PreParseArgs(string[] argv)
{
    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < argv.Length; i++)
    {
        var a = argv[i];
        if (string.IsNullOrWhiteSpace(a)) continue;
        string key;
        string? val = null;
        if (a.StartsWith("--")) a = a.Substring(2);
        var eq = a.IndexOf('=');
        if (eq >= 0)
        {
            key = a.Substring(0, eq);
            val = a.Substring(eq + 1);
        }
        else
        {
            key = a;
            if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--"))
            {
                val = argv[++i];
            }
        }
        if (string.IsNullOrWhiteSpace(key) || val is null) continue;
        if (string.Equals(key, "service", StringComparison.OrdinalIgnoreCase))
            dict[Shared.Constants.ConfigKeys.ServiceConfigKey] = val;
        else if (string.Equals(key, "port", StringComparison.OrdinalIgnoreCase))
            dict["port"] = val;
        else if (string.Equals(key, "healthCheckUrl", StringComparison.OrdinalIgnoreCase))
            dict[Shared.Constants.ConfigKeys.ServiceHealthCheckUrlKeyLower] = val;
    }
    return dict;
}
builder.Configuration.AddInMemoryCollection(PreParseArgs(args));

// Configuration
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args, new Dictionary<string, string>
    {
        ["--service"] = Shared.Constants.ConfigKeys.ServiceConfigKey,
        ["--port"] = "port",
        ["--healthCheckUrl"] = Shared.Constants.ConfigKeys.ServiceHealthCheckUrlKeyLower
    });

// Services
builder.Services.AddDbContext<GameDbContext>(options =>
{
    if (builder.Environment.IsDevelopment())
    {
        options.UseInMemoryDatabase("GameDb");
    }
    else
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
});

builder.Services.AddScoped<IUserAuthComponent, DbUserAuthComponent>();

// Telemetry: Metrics
var metricsEnabled = builder.Configuration.GetValue<bool?>(Shared.Constants.ConfigKeys.TelemetryMetricsEnabledKeyLower)
                     ?? (bool.TryParse(
                         Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.TelemetryMetricsEnabledEnv),
                         out var v) && v);

if (metricsEnabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(res => res.AddService(serviceName: "DotNetGameServer"))
        .WithMetrics(mb =>
        {
            mb.AddRuntimeInstrumentation();
            mb.AddHttpClientInstrumentation();
            mb.AddMeter(CoreTelemetry.Meter.Name);

            var endpoint = builder.Configuration[Shared.Constants.ConfigKeys.TelemetryMetricsOtlpEndpointKeyLower]
                           ?? Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.OtlpEndpointEnv);
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                mb.AddOtlpExporter(otlpOptions =>
                {
                    otlpOptions.Endpoint = new Uri(endpoint);
                    otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                });
            }
        })
        .WithTracing(tb =>
        {
            tb.AddSource(CoreTelemetry.ActivitySource.Name);
            var endpoint = builder.Configuration[Shared.Constants.ConfigKeys.TelemetryMetricsOtlpEndpointKeyLower]
                           ?? Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.OtlpEndpointEnv);
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                tb.AddOtlpExporter(otlpOptions =>
                {
                    otlpOptions.Endpoint = new Uri(endpoint);
                    otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                });
            }
        });
}

builder.Services.AddHostedService<GameServerService>();
builder.Services.AddSingleton<CurrentServerAccessor>();
builder.Services.AddHostedService<AdminConsoleHostedService>();
builder.Services.AddHealthChecks()
    .AddCheck<ServerHealthCheck>("liveness", tags: new[] { "liveness" })
    .AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "readiness" });

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Health check HTTP URL
var healthUrl = builder.Configuration[Shared.Constants.ConfigKeys.ServiceHealthCheckUrlKeyLower]
                ?? Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.ServiceHealthCheckUrlEnv)
                ?? Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.HealthCheckUrlEnv);

var app = builder.Build();
Core.Diagnostics.BasicCommands.RegisterAll();
// Configure listening URL for the embedded HTTP server (health checks)
if (!string.IsNullOrWhiteSpace(healthUrl))
{
    try
    {
        app.Urls.Add(healthUrl);
    }
    catch
    {
        /* ignore malformed url */
    }
}

// Seed the database in development environment
HostingUtilities.SeedDevelopmentDatabase(app.Services);

// Map health checks endpoint（liveness）
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
    }
});

// Map readiness endpoint（只包含 readiness 标签的检查）
app.MapHealthChecks("/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("readiness"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds
            })
        };
        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
    }
});

app.MapPost("/admin/command", async (HttpContext context, IConfiguration cfg, IHostEnvironment env, CurrentServerAccessor accessor) =>
{
    var enabled = env.IsDevelopment() || string.Equals(cfg["ADMIN_HTTP_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
    if (!enabled) return Results.NotFound();
    if (!env.IsDevelopment())
    {
        var auth = context.Request.Headers["Authorization"].ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ")) return Results.Unauthorized();
        var token = auth.Substring("Bearer ".Length);
        var expect = cfg["ADMIN_TOKEN"] ?? Environment.GetEnvironmentVariable("ADMIN_TOKEN");
        if (string.IsNullOrWhiteSpace(expect) || !string.Equals(token, expect)) return Results.Unauthorized();
        var ip = context.Connection.RemoteIpAddress;
        if (ip != null && !IPAddress.IsLoopback(ip))
        {
            var allowed = (cfg["ADMIN_HTTP_ALLOWED_IPS"] ?? "127.0.0.1").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var hit = allowed.Any(a => string.Equals(a, ip.ToString(), StringComparison.OrdinalIgnoreCase));
            if (!hit) return Results.Unauthorized();
        }
    }
    using var sr = new StreamReader(context.Request.Body);
    var body = await sr.ReadToEndAsync();
    string cmdText;
    try
    {
        var doc = System.Text.Json.JsonDocument.Parse(body);
        cmdText = doc.RootElement.TryGetProperty("cmd", out var c) ? c.GetString() ?? string.Empty : string.Empty;
    }
    catch
    {
        cmdText = body;
    }
    var ctx = new DefaultCommandContext(accessor.Server);
    var res = await CommandRegistry.ExecuteTextAsync(cmdText, ctx);
    return Results.Json(res);
});

await app.RunAsync();

namespace ServerHost
{
    public static class HostingUtilities
    {
        public static void SeedDevelopmentDatabase(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var serviceProvider = scope.ServiceProvider;
            var env = serviceProvider.GetRequiredService<IHostEnvironment>();
            if (env.IsDevelopment())
            {
                var dbContext = serviceProvider.GetRequiredService<GameDbContext>();
                if (!dbContext.Users.Any())
                {
                    dbContext.Users.Add(new Core.Db.User { Username = "test", Password = "password" });
                    dbContext.SaveChanges();
                }
            }
        }
    }
}

namespace ServerHost
{
    public class GameServerService : IHostedService
    {
        private readonly ILogger<GameServerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly CurrentServerAccessor _accessor;
        private readonly List<BaseGameServer> _servers = new();
        private IServiceScope? _loginScope;

        public GameServerService(ILogger<GameServerService> logger, IConfiguration configuration,
            IServiceProvider serviceProvider, IHostEnvironment hostEnvironment, CurrentServerAccessor accessor)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _hostEnvironment = hostEnvironment;
            _accessor = accessor;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var service = _configuration[Shared.Constants.ConfigKeys.ServiceConfigKey] ??
                          Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.ServiceEnv) ?? "directory";
            var portConfig = _configuration.GetValue<int?>("port") ??
                       (int.TryParse(Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.PortEnv), out var p)
                           ? p
                           : 5500);

            // 配置链路校验与提示：记录 DirectoryTokenSecret 来源
            Core.Config.ConfigurationBridge.ValidateAndLog(_configuration, _logger);

            // 安全护栏：Production 环境下进行关键配置校验
            if (_hostEnvironment.IsProduction())
            {
                ValidateProductionConfiguration();
            }

            if (string.Equals(service, "all", StringComparison.OrdinalIgnoreCase))
            {
                // 单进程启动所有服务模式（开发环境专用）
                await StartServerInstanceAsync("directory", 5500);
                await StartServerInstanceAsync("login", 5501);
                await StartServerInstanceAsync("battle", 5502);
                await StartServerInstanceAsync("logic", 5503);

                // 将目录服务器设为控制台上下文默认目标
                var dir = _servers.FirstOrDefault(s => s is DirectoryServer.DirectoryGameServer);
                if (dir != null) _accessor.Set(dir);
                
                try { Console.Title = "All Servers (5500-5503)"; } catch { }
            }
            else
            {
                await StartServerInstanceAsync(service, portConfig);
                if (_servers.Count > 0)
                {
                    var s = _servers[0];
                    _accessor.Set(s);
                    try { Console.Title = $"{s.ServerName} : {portConfig}"; } catch { }
                }
            }
        }

        private async Task StartServerInstanceAsync(string serviceType, int port)
        {
            BaseGameServer server = serviceType.ToLowerInvariant() switch
            {
                "directory" => new DirectoryServer.DirectoryGameServer(
                    _serviceProvider.GetRequiredService<ILogger<DirectoryServer.DirectoryGameServer>>(),
                    _configuration),
                "login" => CreateLoginServer(),
                "battle" => new BattleServer.BattleGameServer(_serviceProvider
                    .GetRequiredService<ILogger<BattleServer.BattleGameServer>>()),
                "logic" => new LogicServer.LogicGameServer(_serviceProvider
                    .GetRequiredService<ILogger<LogicServer.LogicGameServer>>()),
                _ => throw new ArgumentException($"未知服务类型: {serviceType}")
            };

            await server.StartAsync(port);
            _servers.Add(server);
            _logger.LogInformation("服务启动成功: {server} 正在监听 {port}, 环境={env}", server.ServerName, port, _hostEnvironment.EnvironmentName);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var server in _servers)
            {
                await server.StopAsync();
            }
            _servers.Clear();
            try { _accessor.Set(null); } catch { }
            try { _loginScope?.Dispose(); } catch { }
            _loginScope = null;
        }

        private BaseGameServer CreateLoginServer()
        {
            _loginScope = _serviceProvider.CreateScope();
            var sp = _loginScope.ServiceProvider;
            var logger = sp.GetRequiredService<ILogger<LoginServer.LoginGameServer>>();
            var auth = sp.GetRequiredService<IUserAuthComponent>();
            return new LoginServer.LoginGameServer(logger, auth);
        }

        private void ValidateProductionConfiguration()
        {
            // 目录令牌密钥校验：禁止使用默认 dev-secret/changeme
            var secret = Core.Config.SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory);
            if (string.IsNullOrWhiteSpace(secret) || secret is "dev-secret" or "changeme" or "changeme-dev")
            {
                throw new InvalidOperationException(
                    "[Guard] 生产环境必须通过 Secret/环境变量提供非默认的 DIRECTORY_TOKEN_SECRET，当前值非法（dev-secret / changeme）。");
            }

            // Redis 连接串校验（当目录注册模式为 redis）
            var registryMode = (_configuration[Shared.Constants.ConfigKeys.DirectoryRegistryModeLower] ??
                                Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.DirectoryRegistryEnv) ??
                                Shared.Constants.ConfigKeys.DirectoryRegistryModeValueMemory).ToLowerInvariant();
            if (registryMode == Shared.Constants.ConfigKeys.DirectoryRegistryModeValueRedis)
            {
                var conn = _configuration[Shared.Constants.ConfigKeys.DirectoryRegistryRedisConnectionStringLower] ??
                           Environment.GetEnvironmentVariable(Shared.Constants.ConfigKeys.RedisConnectionEnv);
                if (string.IsNullOrWhiteSpace(conn))
                {
                    throw new InvalidOperationException(
                        "[Guard] 生产环境启用 Redis 注册表时必须提供 REDIS_CONNECTION（含密码/鉴权或采用 TLS），当前缺失。");
                }

                if (conn.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                    conn.Contains("changeme", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "[Guard] 生产环境的 REDIS_CONNECTION 不应为 localhost 或包含默认口令（changeme）。请改为内网地址/域名并通过 Secret 注入强口令或证书。");
                }
            }

            // 数据库连接串（如果不是内存）
            if (!_hostEnvironment.IsDevelopment())
            {
                var dbConn = _configuration.GetConnectionString("DefaultConnection");
                if (string.IsNullOrWhiteSpace(dbConn))
                {
                    throw new InvalidOperationException("[Guard] 生产环境需要有效的数据库连接串（DefaultConnection）。当前缺失。");
                }
            }
        }
    }
}
