using Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Core.Db;
using Microsoft.EntityFrameworkCore;
using ServerHost.HealthChecks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
using LoginServer.Components;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using Core.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

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
var metricsEnabled = builder.Configuration.GetValue<bool?>("telemetry:metrics:enabled")
    ?? (bool.TryParse(Environment.GetEnvironmentVariable("TELEMETRY_METRICS_ENABLED"), out var v) ? v : false);
var metricsMode = builder.Configuration["telemetry:metrics:mode"]
    ?? Environment.GetEnvironmentVariable("TELEMETRY_METRICS_MODE")
    ?? "prometheus";

if (metricsEnabled)
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(res => res.AddService(serviceName: "DotNetGameServer"))
        .WithMetrics(mb =>
        {
            mb.AddRuntimeInstrumentation();
            mb.AddHttpClientInstrumentation();
            mb.AddMeter(CoreTelemetry.Meter.Name);

            var endpoint = builder.Configuration["telemetry:metrics:otlp:endpoint"]
                ?? Environment.GetEnvironmentVariable("OTLP_ENDPOINT")
                ?? "http://localhost:4318";
            mb.AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(endpoint);
                otlpOptions.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });
        });
}

builder.Services.AddHostedService<GameServerService>();
builder.Services.AddHealthChecks().AddCheck<ServerHealthCheck>("server_health_check");

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Health check HTTP URL
var healthUrl = builder.Configuration["service:healthCheckUrl"]
               ?? Environment.GetEnvironmentVariable("SERVICE__HEALTHCHECKURL")
               ?? Environment.GetEnvironmentVariable("HEALTHCHECK_URL")
               ?? "http://localhost:5100";

var app = builder.Build();
// Configure listening URL for the embedded HTTP server (health checks)
try { app.Urls.Add(healthUrl); } catch { /* ignore malformed url */ }

// Seed the database in development environment
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var env = services.GetRequiredService<IHostEnvironment>();
    if (env.IsDevelopment())
    {
        var dbContext = services.GetRequiredService<GameDbContext>();
        if (!dbContext.Users.Any())
        {
            // Assuming the User model is accessible, as per the user's intent.
            // A 'using' for the User model's namespace and System.Linq may be required.
            dbContext.Users.Add(new Core.Db.User { Username = "test", Password = "password" });
            dbContext.SaveChanges();
        }
    }
}

// Map health checks endpoint
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

await app.RunAsync();

public class GameServerService : IHostedService
{
    private readonly ILogger<GameServerService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHostEnvironment _hostEnvironment;
    private BaseGameServer? _server;

    public GameServerService(ILogger<GameServerService> logger, IConfiguration configuration, IServiceProvider serviceProvider, IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _hostEnvironment = hostEnvironment;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var service = _configuration["service"] ?? Environment.GetEnvironmentVariable("SERVICE") ?? "directory";
        var port = _configuration.GetValue<int?>("port") ??
                   (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 5500);

        // 启动提示（更明显的控制台输出）
        _logger.LogInformation("[Host] 正在启动服务: {service} (port={port}, env={env})", service, port, _hostEnvironment.EnvironmentName);
        Console.WriteLine($"[Host] 正在启动服务: {service} (port={port}, env={_hostEnvironment.EnvironmentName})");

        // 配置链路校验与提示：记录 DirectoryTokenSecret 来源
        Core.Config.ConfigurationBridge.ValidateAndLog(_configuration, _logger);

        // 安全护栏：Production 环境下进行关键配置校验
        if (_hostEnvironment.IsProduction())
        {
            ValidateProductionConfiguration(service);
        }

        _server = service.ToLowerInvariant() switch
        {
            "directory" => new DirectoryServer.DirectoryGameServer(_serviceProvider.GetRequiredService<ILogger<DirectoryServer.DirectoryGameServer>>(), _configuration),
            "login" => new LoginServer.LoginGameServer(_serviceProvider.GetRequiredService<ILogger<LoginServer.LoginGameServer>>(), _serviceProvider.GetRequiredService<IUserAuthComponent>()),
            "resource" => new ResourceServer.ResourceGameServer(_serviceProvider.GetRequiredService<ILogger<ResourceServer.ResourceGameServer>>(), _configuration),
            "battle" => new BattleServer.BattleGameServer(_serviceProvider.GetRequiredService<ILogger<BattleServer.BattleGameServer>>()),
            "logic" => new LogicServer.LogicGameServer(_serviceProvider.GetRequiredService<ILogger<LogicServer.LogicGameServer>>()),
            _ => throw new ArgumentException($"未知服务类型: {service}")
        };

        await _server.StartAsync(port);
        _logger.LogInformation("✅ 服务启动成功: {server} 正在监听 {port}, 环境={env}", _server.ServerName, port, _hostEnvironment.EnvironmentName);
        Console.WriteLine($"✅ 服务启动成功: {_server.ServerName} 正在监听 {port}, 环境={_hostEnvironment.EnvironmentName}");
        Console.WriteLine("按 Ctrl+C 退出该服务窗口，或关闭窗口以停止服务。");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_server != null)
        {
            await _server.StopAsync();
        }
    }
    private void ValidateProductionConfiguration(string service)
    {
        // 目录令牌密钥校验：禁止使用默认 dev-secret/changeme
        var secret = Core.Config.SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(secret) || secret is "dev-secret" or "changeme" or "changeme-dev")
        {
            throw new InvalidOperationException("[Guard] 生产环境必须通过 Secret/环境变量提供非默认的 DIRECTORY_TOKEN_SECRET，当前值非法（dev-secret / changeme）。");
        }

        // Redis 连接串校验（当目录注册模式为 redis）
        var registryMode = (_configuration["directory:registry:mode"] ?? Environment.GetEnvironmentVariable("DIRECTORY_REGISTRY") ?? "memory").ToLowerInvariant();
        if (registryMode == "redis")
        {
            var conn = _configuration["directory:registry:redis:connectionString"] ?? Environment.GetEnvironmentVariable("REDIS_CONNECTION");
            if (string.IsNullOrWhiteSpace(conn))
            {
                throw new InvalidOperationException("[Guard] 生产环境启用 Redis 注册表时必须提供 REDIS_CONNECTION（含密码/鉴权或采用 TLS），当前缺失。");
            }
            if (conn.Contains("localhost", StringComparison.OrdinalIgnoreCase) || conn.Contains("changeme", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("[Guard] 生产环境的 REDIS_CONNECTION 不应为 localhost 或包含默认口令（changeme）。请改为内网地址/域名并通过 Secret 注入强口令或证书。");
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
// NOTE: Using directives should appear at the top of the file. The previous attempt added them mid-file and caused CS1529.