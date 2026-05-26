using Core.Configuration;
using Core.Diagnostics;
using Core.Logging;
using Core.Telemetry;
using Hosting.HealthChecks;
using Shared.Constants;

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

using System.Net;

namespace Hosting;

/// <summary>
/// 通用服务器宿主：统一负责配置加载、Options 绑定、日志、遥测、健康检查、
/// 管理命令端点与控制台 REPL。各服务器项目只需提供一个 <see cref="GameServerDefinition"/>。
/// </summary>
public static class GameServerHost
{
    public static async Task RunAsync(string[] args, GameServerDefinition definition)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Configuration.AddInMemoryCollection(PreParseArgs(args)!);
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
            .AddCommandLine(args, new Dictionary<string, string>
            {
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
        ConfigureTelemetry(builder);

        // ----- DI services -----
        builder.Services.AddSingleton<CurrentServerAccessor>();
        builder.Services.AddSingleton(definition);
        definition.ConfigureServices?.Invoke(builder.Services, builder.Configuration);

        builder.Services.AddHostedService<GameServerHostedService>();
        builder.Services.AddHostedService<AdminConsoleHostedService>();
        builder.Services.AddHealthChecks()
            .AddCheck<ServerHealthCheck>("liveness", tags: new[] { "liveness" })
            .AddCheck<ReadinessHealthCheck>("readiness", tags: new[] { "readiness" });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        var app = builder.Build();
        BasicCommands.RegisterAll();

        var healthUrl = builder.Configuration[ConfigKeys.ServiceHealthCheckUrlKeyLower];
        if (!string.IsNullOrWhiteSpace(healthUrl))
        {
            try { app.Urls.Add(healthUrl); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Host] 添加 health check URL 失败: {ex.Message}"); }
        }

        MapHealthEndpoints(app);
        MapAdminCommand(app);
        definition.ConfigureEndpoints?.Invoke(app);

        await app.RunAsync();
    }

    private static IDictionary<string, string> PreParseArgs(string[] argv)
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
                    val = argv[++i];
            }

            if (string.IsNullOrWhiteSpace(key) || val is null) continue;
            if (string.Equals(key, "port", StringComparison.OrdinalIgnoreCase))
                dict["port"] = val;
            else if (string.Equals(key, "healthCheckUrl", StringComparison.OrdinalIgnoreCase))
                dict[ConfigKeys.ServiceHealthCheckUrlKeyLower] = val;
        }

        return dict;
    }

    private static void ConfigureTelemetry(WebApplicationBuilder builder)
    {
        var metricsEnabled = builder.Configuration.GetValue<bool?>(ConfigKeys.TelemetryMetricsEnabledKeyLower) ?? false;
        if (!metricsEnabled) return;

        var endpoint = builder.Configuration[ConfigKeys.TelemetryMetricsOtlpEndpointKeyLower];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(res => res.AddService(serviceName: "DotNetGameServer"))
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

    private static void MapHealthEndpoints(WebApplication app)
    {
        static Func<HttpContext, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport, Task> Writer() =>
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
                        description = e.Value.Description,
                        duration = e.Value.Duration.TotalMilliseconds
                    })
                };
                await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
            };

        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = Writer() });
        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("readiness"),
            ResponseWriter = Writer()
        });
    }

    private static void MapAdminCommand(WebApplication app)
    {
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
                    System.Diagnostics.Debug.WriteLine($"[Host] 解析命令请求体失败: {ex.Message}");
                    cmdText = body;
                }

                var ctx = new DefaultCommandContext(accessor.Server);
                var res = await CommandRegistry.ExecuteTextAsync(cmdText, ctx);
                return Results.Json(res);
            });
    }
}
