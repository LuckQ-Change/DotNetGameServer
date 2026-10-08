using Core.Configuration;
using Core.Logging;
using Core.Telemetry;

using Hosting.HealthChecks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Shared.Configuration;

namespace Hosting;

/// <summary>
/// 通用服务器宿主：统一负责配置加载、Options 绑定、日志、遥测、健康检查、
/// HTTP 健康检查与生命周期。业务项目只需提供一个 <see cref="GameServerDefinition"/>。
/// </summary>
public static class GameServerHost
{
    private const string ReadinessName = "readiness";

    public static async Task RunAsync(string[] args, GameServerDefinition definition)
    {
        var builder = WebApplication.CreateBuilder(args);

        AddCommandLineOverrides(builder.Configuration, args);

        // ----- 配置绑定 -----
        builder.Services.AddOptions<ServerOptions>()
            .Bind(builder.Configuration.GetSection("server"))
            .Validate(options => options.MaxMessageSizeBytes is >= 1024 and <= 64 * 1024 * 1024,
                "server:maxMessageSizeBytes 必须在 1 KiB 到 64 MiB 之间。")
            .Validate(options => System.Net.IPAddress.TryParse(options.ListenAddress, out _),
                "server:listenAddress 必须是有效的 IPv4 或 IPv6 地址。")
            .Validate(options => options.MaxConcurrentConnections > 0,
                "server:maxConcurrentConnections 必须大于 0。")
            .Validate(options => options.ConnectionQueueCapacity > 0,
                "server:connectionQueueCapacity 必须大于 0。")
            .Validate(options => options.MaxConcurrentRequestsPerConnection is >= 1 and <= 64,
                "server:maxConcurrentRequestsPerConnection 必须在 1 到 64 之间。")
            .Validate(options => options.ReceiveTimeoutMs >= 0 && options.IdleTimeoutMs >= 0
                && options.AcceptBackoffMs >= 0 && options.RequestTimeoutMs >= 0,
                "server 中的超时和退避时间不能小于 0。")
            .Validate(options => options.StartupTimeoutMs >= 1_000,
                "server:startupTimeoutMs 不能小于 1000。")
            .Validate(options => options.ShutdownTimeoutMs >= 1_000,
                "server:shutdownTimeoutMs 不能小于 1000。")
            .Validate(options => options.ListenBacklog > 0,
                "server:listenBacklog 必须大于 0。")
            .Validate(options => options.MaxBytesPerSecond > 0 && options.MaxMessagesPerSecond > 0
                && options.IpMaxBytesPerSecond >= 0 && options.IpMaxMessagesPerSecond >= 0,
                "server 限流值无效。")
            .Validate(options => options.MaxMessageTypeLength > 0 && options.MaxRequestIdLength > 0
                && options.MaxTokenLength > 0 && options.MaxVersionLength > 0,
                "server 协议元数据长度上限必须大于 0。")
            .Validate(options => !options.Tls.Enabled || (!string.IsNullOrWhiteSpace(options.Tls.CertificatePath)
                && options.Tls.HandshakeTimeoutMs >= 1_000),
                "启用 server:tls 时必须提供证书路径，且握手超时不能小于 1000ms。")
            .ValidateOnStart();
        builder.Services.AddOptions<AuthenticationOptions>()
            .Bind(builder.Configuration.GetSection("authentication"))
            .Validate(options => options.ClockSkewSeconds >= 0,
                "authentication:clockSkewSeconds 不能小于 0。")
            .Validate(options => !options.RequireClientAuthentication
                || !string.IsNullOrWhiteSpace(options.TokenSecret),
                "启用客户端消息鉴权时 authentication:tokenSecret 不能为空。")
            .Validate(options => options.ClientTokenTtlSeconds >= 60,
                "authentication:clientTokenTtlSeconds 不能小于 60。")
            .Validate(options => options.ClientTokenRefreshGraceSeconds >= 0,
                "authentication:clientTokenRefreshGraceSeconds 不能小于 0。")
            .ValidateOnStart();
        builder.Services.AddOptions<ClientHeartbeatOptions>()
            .Bind(builder.Configuration.GetSection("clientHeartbeat"))
            .Validate(options => !options.Enabled || options.RecommendedIntervalSeconds > 0,
                "启用客户端心跳时，clientHeartbeat:recommendedIntervalSeconds 必须大于 0。")
            .ValidateOnStart();
        builder.Services.Configure<LoggingOptions>(builder.Configuration.GetSection("gameLogging"));
        builder.Services.AddOptions<ServiceDiscoveryOptions>()
            .Bind(builder.Configuration.GetSection("discovery"))
            .Validate(options => options.OfflineTtlSeconds > 0,
                "discovery:offlineTtlSeconds 必须大于 0。")
            .Validate(options => options.RemoveTtlSeconds > options.OfflineTtlSeconds,
                "discovery:removeTtlSeconds 必须大于 offlineTtlSeconds。")
            .Validate(options => options.CleanupIntervalSeconds > 0,
                "discovery:cleanupIntervalSeconds 必须大于 0。")
            .ValidateOnStart();
        builder.Services.AddOptions<ServiceRegistrationOptions>()
            .Bind(builder.Configuration.GetSection("serviceRegistration"))
            .Validate(options => options.IntervalSeconds > 0,
                "serviceRegistration:intervalSeconds 必须大于 0。")
            .Validate(options => options.DiscoveryPort is > 0 and <= 65535,
                "serviceRegistration:discoveryPort 必须在 1 到 65535 之间。")
            .Validate(options => options.Capacity > 0,
                "serviceRegistration:capacity 必须大于 0。")
            .ValidateOnStart();
        builder.Services.AddGameDatabase(builder.Configuration);
        builder.Services.AddGameServerRpc(builder.Configuration);

        // ----- 日志中心初始化 -----
        var gameLoggingOptions = builder.Configuration.GetSection("gameLogging").Get<LoggingOptions>() ?? new LoggingOptions();
        LogCenter.ApplyOptions(gameLoggingOptions);

        // ----- 遥测配置 -----
        ConfigureTelemetry(builder, definition.Role);

        // ----- 依赖注入服务 -----
        builder.Services.AddSingleton<CurrentServerAccessor>();
        builder.Services.AddSingleton(definition);
        definition.ConfigureServices?.Invoke(builder.Services, builder.Configuration);

        builder.Services.AddHostedService<GameServerHostedService>();
        builder.Services.AddHealthChecks()
            .AddCheck<ServerHealthCheck>("liveness", tags: new[] { "liveness" })
            .AddCheck<ReadinessHealthCheck>(ReadinessName, tags: new[] { ReadinessName })
            .AddCheck<DatabaseHealthCheck>("database", tags: new[] { ReadinessName });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        if (gameLoggingOptions.FileEnabled)
            builder.Logging.AddProvider(new RollingFileLoggerProvider(definition.Role, gameLoggingOptions));

        var app = builder.Build();
        var healthUrl = builder.Configuration[ConfigKeys.ServiceHealthCheckUrlKeyLower];
        if (!string.IsNullOrWhiteSpace(healthUrl))
        {
            try { app.Urls.Add(healthUrl); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Host] 添加 health check URL 失败: {ex.Message}"); }
        }

        MapHealthEndpoints(app, app.Environment.IsDevelopment());
        app.MapClientServiceCatalog(definition);
        definition.ConfigureEndpoints?.Invoke(app);

        await app.RunAsync();
    }

    internal static void AddCommandLineOverrides(
        ConfigurationManager configuration,
        string[] args)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(args);

        // CreateBuilder 已按 JSON < User Secrets < 环境变量 < 命令行建立默认配置链。
        // 此处只在链尾补充框架短参数映射，避免重新添加 JSON 后反向覆盖环境变量。
        configuration.AddCommandLine(args, new Dictionary<string, string>
        {
            ["--port"] = "port",
            ["--healthCheckUrl"] = ConfigKeys.ServiceHealthCheckUrlKeyLower
        });
    }

    private static void ConfigureTelemetry(WebApplicationBuilder builder, string serverRole)
    {
        var metricsEnabled = builder.Configuration.GetValue<bool?>(ConfigKeys.TelemetryMetricsEnabledKeyLower) ?? false;
        if (!metricsEnabled) return;

        var endpoint = builder.Configuration[ConfigKeys.TelemetryMetricsOtlpEndpointKeyLower];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(res => res.AddService(
                serviceName: $"DotNetGameServer.{serverRole}",
                serviceInstanceId: Environment.MachineName))
            .WithMetrics(mb =>
            {
                mb.AddRuntimeInstrumentation();
                mb.AddHttpClientInstrumentation();
                mb.AddMeter(CoreTelemetry.Meter.Name);
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    mb.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(endpoint);
                        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
                }
            })
            .WithTracing(tb =>
            {
                tb.AddSource(CoreTelemetry.ActivitySource.Name);
                if (!string.IsNullOrWhiteSpace(endpoint))
                {
                    tb.AddOtlpExporter(o =>
                    {
                        o.Endpoint = new Uri(endpoint);
                        o.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                    });
                }
            });
    }

    private static void MapHealthEndpoints(WebApplication app, bool includeDetails)
    {
        Func<HttpContext, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport, Task> Writer() =>
            async (context, report) =>
            {
                context.Response.ContentType = "application/json";
                var payload = new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.Select(e => new
                    {
                        name = e.Key,
                        status = e.Value.Status.ToString(),
                        description = includeDetails ? e.Value.Description : null,
                        duration = e.Value.Duration.TotalMilliseconds
                    })
                };
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
            };

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("liveness"),
            ResponseWriter = Writer()
        });
        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains(ReadinessName),
            ResponseWriter = Writer()
        });
    }

}
