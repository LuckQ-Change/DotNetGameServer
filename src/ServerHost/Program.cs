using Core;
using Core.Configuration;
using Core.Directory;
using Core.Diagnostics;
using Core.Heartbeat;
using Core.Logging;
using Core.Telemetry;
using Shared.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ServerHost.Directory;
using ServerHost.HealthChecks;
using ServerHost.ServerTypes;
using System.Net;

using ServerHost;

var builder = WebApplication.CreateBuilder(args);

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
            dict[ConfigKeys.ServiceConfigKey] = val;
        else if (string.Equals(key, "port", StringComparison.OrdinalIgnoreCase))
            dict["port"] = val;
        else if (string.Equals(key, "healthCheckUrl", StringComparison.OrdinalIgnoreCase))
            dict[ConfigKeys.ServiceHealthCheckUrlKeyLower] = val;
    }

    return dict;
}

builder.Configuration.AddInMemoryCollection(PreParseArgs(args)!);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddCommandLine(args, new Dictionary<string, string>
    {
        ["--service"] = ConfigKeys.ServiceConfigKey,
        ["--port"] = "port",
        ["--healthCheckUrl"] = ConfigKeys.ServiceHealthCheckUrlKeyLower
    });

// ----- Options binding -----
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection("server"));
builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("logging"));
builder.Services.Configure<DirectoryOptions>(builder.Configuration.GetSection("directory"));
builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection("heartbeat"));

// ----- LogCenter initialization -----
LogCenter.ApplyOptions(builder.Configuration.GetSection("logging").Get<LoggingOptions>() ?? new LoggingOptions());

// ----- OpenTelemetry -----
var metricsEnabled = builder.Configuration.GetValue<bool?>(ConfigKeys.TelemetryMetricsEnabledKeyLower) ?? false;

if (metricsEnabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(res => res.AddService(serviceName: "DotNetGameServer"))
        .WithMetrics(mb =>
        {
            mb.AddRuntimeInstrumentation();
            mb.AddHttpClientInstrumentation();
            mb.AddMeter(CoreTelemetry.Meter.Name);

            var endpoint = builder.Configuration[ConfigKeys.TelemetryMetricsOtlpEndpointKeyLower];
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
            var endpoint = builder.Configuration[ConfigKeys.TelemetryMetricsOtlpEndpointKeyLower];
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

// ----- DI services -----
builder.Services.AddSingleton<CurrentServerAccessor>();
builder.Services.AddSingleton<IServerRegistry>(sp =>
{
    var dirOpts = sp.GetRequiredService<IOptions<DirectoryOptions>>().Value;
    if (string.Equals(dirOpts.Registry.Mode, "redis", StringComparison.OrdinalIgnoreCase))
        return new RedisServerRegistry(dirOpts.Registry.Redis.ConnectionString, dirOpts.Registry.Redis.CleanupIntervalSeconds);
    return new MemoryServerRegistry();
});

builder.Services.AddHostedService<GameServerService>();
builder.Services.AddHostedService<AdminConsoleHostedService>();
builder.Services.AddHealthChecks()
    .AddCheck<ServerHealthCheck>("liveness", tags: new[] { "liveness" })
    .AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "readiness" });

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var healthUrl = builder.Configuration[ConfigKeys.ServiceHealthCheckUrlKeyLower];

var app = builder.Build();
BasicCommands.RegisterAll();

if (!string.IsNullOrWhiteSpace(healthUrl))
{
    try { app.Urls.Add(healthUrl); }
    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Program] 添加 health check URL 失败: {ex.Message}"); }
}

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

app.MapPost("/admin/command",
    async (HttpContext context, IConfiguration cfg, IHostEnvironment env, CurrentServerAccessor accessor) =>
    {
        var enabled = env.IsDevelopment() ||
                      string.Equals(cfg["ADMIN_HTTP_ENABLED"], "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled) return Results.NotFound();
        if (!env.IsDevelopment())
        {
            var auth = context.Request.Headers["Authorization"].ToString();
            if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ")) return Results.Unauthorized();
            var token = auth.Substring("Bearer ".Length);
            var expect = cfg["ADMIN_TOKEN"];
            if (string.IsNullOrWhiteSpace(expect) || !string.Equals(token, expect)) return Results.Unauthorized();
            var ip = context.Connection.RemoteIpAddress;
            if (ip != null && !IPAddress.IsLoopback(ip))
            {
                var allowed = (cfg["ADMIN_HTTP_ALLOWED_IPS"] ?? "127.0.0.1").Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!Array.Exists(allowed, a => string.Equals(a, ip.ToString(), StringComparison.OrdinalIgnoreCase)))
                    return Results.Unauthorized();
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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Program] 解析命令请求体失败: {ex.Message}");
            cmdText = body;
        }

        var ctx = new DefaultCommandContext(accessor.Server);
        var res = await CommandRegistry.ExecuteTextAsync(cmdText, ctx);
        return Results.Json(res);
    });

// ----- Routing API -----
app.MapGet("/api/routing/query", async (HttpContext context, IServerRegistry registry) =>
{
    var serverType = context.Request.Query["type"].FirstOrDefault();
    var region = context.Request.Query["region"].FirstOrDefault();
    var strategy = context.Request.Query["strategy"].FirstOrDefault() ?? "load";
    var limitStr = context.Request.Query["limit"].FirstOrDefault();
    int.TryParse(limitStr, out var limit);
    if (limit <= 0) limit = 10;

    var candidates = await registry.QueryAsync(serverType, region);
    var selector = Core.Directory.ServerSelectionStrategyFactory.Create(strategy);
    var selected = selector.Select(candidates, limit);

    var result = selected.Select(s => new
    {
        id = s.ServerId,
        type = s.ServerType,
        region = s.Region,
        host = s.Host,
        port = s.Port,
        playerCount = s.PlayerCount,
        capacity = s.Capacity,
        load = s.Load,
        status = s.Status,
        lastHeartbeat = s.LastHeartbeatUnix,
        version = s.Version
    }).ToArray();

    return Results.Json(new { servers = result, count = result.Length, strategy });
});

await app.RunAsync();

namespace ServerHost
{
    public class GameServerService : IHostedService
    {
        private readonly ILogger<GameServerService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHostEnvironment _hostEnvironment;
        private readonly CurrentServerAccessor _accessor;
        private readonly IServerRegistry _registry;
        private readonly IOptions<ServerOptions> _serverOptions;
        private readonly IOptions<HeartbeatOptions> _heartbeatOptions;
        private readonly IOptions<DirectoryOptions> _directoryOptions;
        private readonly List<BaseGameServer> _servers = new();

        public GameServerService(
            ILogger<GameServerService> logger,
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            IHostEnvironment hostEnvironment,
            CurrentServerAccessor accessor,
            IServerRegistry registry,
            IOptions<ServerOptions> serverOptions,
            IOptions<HeartbeatOptions> heartbeatOptions,
            IOptions<DirectoryOptions> directoryOptions)
        {
            _logger = logger;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _hostEnvironment = hostEnvironment;
            _accessor = accessor;
            _registry = registry;
            _serverOptions = serverOptions;
            _heartbeatOptions = heartbeatOptions;
            _directoryOptions = directoryOptions;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var service = _configuration[ConfigKeys.ServiceConfigKey] ?? "default";
            var portConfig = _configuration.GetValue<int?>("port") ?? 5500;

            if (_hostEnvironment.IsProduction())
                ValidateProductionConfiguration();

            if (string.Equals(service, "all", StringComparison.OrdinalIgnoreCase))
            {
                await StartDirectoryServer(5500);
                await StartServerInstanceAsync("login", 5501);
                await StartServerInstanceAsync("logic", 5502);
                await StartServerInstanceAsync("battle", 5503);
                try { Console.Title = "All Servers"; }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Program] 设置控制台标题失败: {ex.Message}"); }
            }
            else
            {
                if (string.Equals(service, "directory", StringComparison.OrdinalIgnoreCase))
                    await StartDirectoryServer(portConfig);
                else
                    await StartServerInstanceAsync(service, portConfig);

                if (_servers.Count > 0)
                {
                    try { Console.Title = $"{_servers[0].ServerName} : {portConfig}"; }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Program] 设置控制台标题失败: {ex.Message}"); }
                }
            }
        }

        private async Task StartDirectoryServer(int port)
        {
            var dirOpts = _directoryOptions.Value;
            var serverOpts = _serverOptions.Value;
            var logger = _serviceProvider.GetRequiredService<ILogger<DirectoryGameServer>>();
            var server = new DirectoryGameServer(logger, _registry, serverOpts);
            server.AddComponent<IServerRegistry>(_registry);
            server.AddComponent<DirectoryCleanupService>(new DirectoryCleanupService(_registry, dirOpts));
            await server.StartAsync(port);
            _servers.Add(server);
            _accessor.Add(server.ServerName, server);
            _logger.LogInformation("Directory server 启动成功, 监听端口={port}", port);
        }

        private async Task StartServerInstanceAsync(string serviceType, int port)
        {
            var serverOpts = _serverOptions.Value;
            var hbOpts = _heartbeatOptions.Value;
            var dirOpts = _directoryOptions.Value;
            BaseGameServer server = serviceType.ToLowerInvariant() switch
            {
                "login" => new LoginGameServer(
                    _serviceProvider.GetRequiredService<ILogger<LoginGameServer>>(), serverOpts),
                "logic" => new LogicGameServer(
                    _serviceProvider.GetRequiredService<ILogger<LogicGameServer>>(), serverOpts),
                "battle" => new BattleGameServer(
                    _serviceProvider.GetRequiredService<ILogger<BattleGameServer>>(), serverOpts),
                _ => new DefaultGameServer(
                    _serviceProvider.GetRequiredService<ILogger<DefaultGameServer>>(), serverOpts),
            };

            var tokenSecret = dirOpts.TokenSecret;
            if (string.IsNullOrWhiteSpace(tokenSecret))
                tokenSecret = "dev-secret";
            server.AddComponent<Core.Components.ITokenSecretProvider>(new Core.Components.TokenSecretProviderComponent(tokenSecret));

            hbOpts.ServerType = serviceType;
            hbOpts.Port = port;
            hbOpts.DirectoryPort = _configuration.GetValue<int?>("directory:registry:port") ?? 5500;
            server.AddComponent<HeartbeatSenderService>(new HeartbeatSenderService(hbOpts, tokenSecret, _logger));

            await server.StartAsync(port);
            _servers.Add(server);
            _accessor.Add(server.ServerName, server);
            _logger.LogInformation("服务启动成功: {serverType} 监听 {port}", serviceType, port);
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            foreach (var server in _servers)
                await server.StopAsync();
            _servers.Clear();
            try { _accessor.Clear(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Program] 清除访问器失败: {ex.Message}"); }
        }

        private void ValidateProductionConfiguration()
        {
            var secret = _directoryOptions.Value.TokenSecret;
            if (string.IsNullOrWhiteSpace(secret) || secret is "dev-secret" or "changeme")
                throw new InvalidOperationException("[Guard] 生产环境必须通过配置提供非默认的 TokenSecret。");
        }
    }

    public class DefaultGameServer : BaseGameServer
    {
        public DefaultGameServer(ILogger<DefaultGameServer> logger, ServerOptions? options = null)
            : base("Default", logger, options)
        {
        }
    }
}
