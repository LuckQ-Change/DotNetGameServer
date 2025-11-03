using Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

// 简单统一启动器：根据环境配置与参数启动指定服务
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
var builder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();
var configuration = builder.Build();

// 解析服务类型与端口
var service = Environment.GetEnvironmentVariable("SERVICE") ?? configuration["service:type"] ?? "directory";
var port = int.TryParse(Environment.GetEnvironmentVariable("PORT"), out var p) ? p
    : (configuration.GetValue<int?>("service:port") ?? 5500);

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

var cts = new CancellationTokenSource();
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