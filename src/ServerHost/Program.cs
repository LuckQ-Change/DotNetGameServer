using Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ServerHost.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;
using Microsoft.AspNetCore.Http; // <-- 添加 using
using Core.Db;
using Microsoft.EntityFrameworkCore;

// 简单统一启动器：根据环境配置与参数启动指定服务
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
var builder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args); // <-- 添加命令行参数源
var configuration = builder.Build();

var cts = new CancellationTokenSource();

// 启动健康检查主机
var healthCheckHost = CreateHealthCheckHost(args, configuration);
var healthCheckTask = healthCheckHost.RunAsync(cts.Token);


// 修正配置读取逻辑：优先从命令行获取
var service = configuration["service"] ?? Environment.GetEnvironmentVariable("SERVICE") ?? "directory";
var port = configuration.GetValue<int?>("port") ?? 
           (int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p : 5500);

// 将常用配置映射到环境变量，兼容现有 SimpleConfigLoader
MapServerSettingsToEnv(configuration);
MapDirectorySecretToEnv(configuration);
MapMySqlToEnv(configuration);

// 构建日志工厂（容器友好：输出到控制台）
using var loggerFactory = LoggerFactory.Create(lb =>
{
    lb.SetMinimumLevel(LogLevel.Information);
    lb.AddConsole();
});

BaseGameServer server = service.ToLowerInvariant() switch
{
    "directory" => new DirectoryServer.DirectoryGameServer(loggerFactory.CreateLogger("Directory")),
    "login" => new LoginServer.LoginGameServer(loggerFactory.CreateLogger("Login")),
    "resource" => new ResourceServer.ResourceGameServer(loggerFactory.CreateLogger("Resource")),
    "battle" => new BattleServer.BattleGameServer(loggerFactory.CreateLogger("Battle")),
    // LogicServer 暂未提供 ILogger 构造，回退委托方式
    "logic" => new LogicServer.LogicGameServer(message => loggerFactory.CreateLogger("Logic").LogInformation("{Message}", message)),
    _ => throw new ArgumentException($"未知服务类型: {service}")
};

Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, __) => { try { cts.Cancel(); } catch { } };

await server.StartAsync(port);
Console.WriteLine($"[Host] {server.ServerName} listening on port {port}, env={environment}");

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // 优雅关闭
    await server.StopAsync();
}

// 等待健康检查主机关闭
await healthCheckTask;

static void MapServerSettingsToEnv(IConfiguration config)
{
    var s = config.GetSection("server");
    void Set(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Environment.SetEnvironmentVariable(key, value);
    }
    var maxMsg = s.GetValue<int?>("maxMessageSizeBytes");
    var maxConn = s.GetValue<int?>("maxConcurrentConnections");
    var rcvTo = s.GetValue<int?>("receiveTimeoutMs");
    var backoff = s.GetValue<int?>("acceptBackoffMs");
    Set("SERVER_MAX_MESSAGE_SIZE", maxMsg?.ToString());
    Set("SERVER_MAX_CONNECTIONS", maxConn?.ToString());
    Set("SERVER_RECEIVE_TIMEOUT_MS", rcvTo?.ToString());
    Set("SERVER_ACCEPT_BACKOFF_MS", backoff?.ToString());

    // 限速/空闲覆盖（BaseGameServer 支持环境变量）
    var maxBps = s.GetValue<int?>("maxBytesPerSecond");
    var maxMsgs = s.GetValue<int?>("maxMessagesPerSecond");
    var idleMs = s.GetValue<int?>("idleTimeoutMs");
    Set("SERVER_MAX_BPS", maxBps?.ToString());
    Set("SERVER_MAX_MSGS", maxMsgs?.ToString());
    Set("SERVER_IDLE_MS", idleMs?.ToString());
}

static void MapDirectorySecretToEnv(IConfiguration config)
{
    var token = config["directory:tokenSecret"];
    if (!string.IsNullOrWhiteSpace(token))
    {
        Environment.SetEnvironmentVariable("DIRECTORY_TOKEN_SECRET", token);
    }
}

static void MapMySqlToEnv(IConfiguration config)
{
    var mysql = config.GetSection("mysql");
    // 支持完整连接串或分项变量
    var conn = mysql.GetValue<string?>("conn");
    if (!string.IsNullOrWhiteSpace(conn))
    {
        Environment.SetEnvironmentVariable("MYSQL_CONN", conn);
        return;
    }
    void Set(string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) Environment.SetEnvironmentVariable(key, value);
    }
    Set("MYSQL_HOST", mysql.GetValue<string?>("host"));
    var port = mysql.GetValue<int?>("port");
    Set("MYSQL_PORT", port?.ToString());
    Set("MYSQL_USER", mysql.GetValue<string?>("user"));
    Set("MYSQL_PASSWORD", mysql.GetValue<string?>("password"));
    Set("MYSQL_DB", mysql.GetValue<string?>("database"));
}

static IHost CreateHealthCheckHost(string[] args, IConfiguration config) =>
    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(webBuilder =>
        {
            var healthCheckUrl = config.GetValue<string>("healthCheckUrl") ?? "http://*:5100";
            webBuilder.UseUrls(healthCheckUrl); 
            webBuilder.ConfigureServices(services =>
            {
                services.AddHealthChecks().AddCheck<ServerHealthCheck>("server_health_check");
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapHealthChecks("/health", new HealthCheckOptions
                    {
                        ResponseWriter = WriteHealthCheckResponse
                    });
                });
            });
        }).Build();

// 将方法移出并设为 static
static Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var options = new JsonSerializerOptions
    {
        WriteIndented = true
    };
    var json = new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            exception = e.Value.Exception?.Message,
            data = e.Value.Data
        })
    };
    return context.Response.WriteAsync(JsonSerializer.Serialize(json, options));
}

// 添加数据库上下文
builder.Services.AddDbContext<GameDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.