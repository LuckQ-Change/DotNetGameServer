using Core.Configuration;
using Core.Directory;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hosting;

/// <summary>为 Unity/游戏客户端提供不含密钥的 HTTP 服务目录。</summary>
public static class ClientServiceCatalogEndpoints
{
    public static IEndpointRouteBuilder MapClientServiceCatalog(
        this IEndpointRouteBuilder endpoints,
        GameServerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(definition);

        endpoints.MapGet("/.well-known/dotnet-game-server", (HttpContext http, CurrentServerAccessor accessor) =>
        {
            var snapshot = accessor.Server?.GetRuntimeSnapshot();
            return Results.Ok(new
            {
                schemaVersion = 1,
                service = definition.Role,
                protocolVersion = definition.ProtocolVersion,
                catalog = "/.well-known/dotnet-game-server/services",
                ready = snapshot?.IsRunning == true
            });
        });

        endpoints.MapGet("/.well-known/dotnet-game-server/services", async (
            HttpContext http,
            CurrentServerAccessor accessor,
            IOptions<ClientHeartbeatOptions> heartbeatOptions,
            string? responsibility,
            string? region,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var server = accessor.Server;
            if (server is null || !server.IsRunning)
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

            var heartbeatSeconds = heartbeatOptions.Value.Enabled
                ? Math.Max(1, heartbeatOptions.Value.RecommendedIntervalSeconds)
                : 0;
            var maximum = Math.Clamp(limit ?? 16, 1, 100);
            var registry = server.GetComponent<IServerRegistry>();
            if (registry is not null)
            {
                var registered = await registry.QueryAsync(null, region, cancellationToken);
                var services = registered
                    .Where(item => string.Equals(item.Status, "online", StringComparison.OrdinalIgnoreCase))
                    .Where(item => string.IsNullOrWhiteSpace(responsibility)
                        || string.Equals(item.ServerType, responsibility, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.Load)
                    .ThenBy(item => item.ServerId, StringComparer.Ordinal)
                    .Take(maximum)
                    .Select(item => new ClientServiceEntry(
                        item.ServerId,
                        item.ServerType,
                        NormalizeHost(item.Host, http.Request.Host.Host),
                        item.Port,
                        item.Region,
                        item.Version,
                        item.PlayerCount,
                        item.Capacity,
                        item.TlsEnabled,
                        item.TlsServerName,
                        [item.ServerType],
                        heartbeatSeconds))
                    .ToArray();
                return Results.Ok(new ClientServiceCatalog(1, services));
            }

            var responsibilities = definition.Responsibilities.Count == 0
                ? new[] { definition.Role }
                : definition.Responsibilities.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (!string.IsNullOrWhiteSpace(responsibility)
                && !responsibilities.Contains(responsibility, StringComparer.OrdinalIgnoreCase))
                return Results.Ok(new ClientServiceCatalog(1, []));

            var snapshot = server.GetRuntimeSnapshot();
            var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
            var configuredHost = configuration["serviceRegistration:host"];
            var configuredRegion = configuration["serviceRegistration:region"] ?? string.Empty;
            var configuredTlsServerName = configuration["serviceRegistration:tlsServerName"] ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(region)
                && !string.Equals(region, configuredRegion, StringComparison.OrdinalIgnoreCase))
                return Results.Ok(new ClientServiceCatalog(1, []));

            var serverOptions = http.RequestServices
                .GetRequiredService<Microsoft.Extensions.Options.IOptions<Core.Configuration.ServerOptions>>().Value;
            var entry = new ClientServiceEntry(
                definition.Role,
                definition.Role,
                NormalizeHost(configuredHost ?? serverOptions.ListenAddress, http.Request.Host.Host),
                snapshot.ListeningPort ?? 0,
                configuredRegion,
                definition.ProtocolVersion,
                snapshot.PlayerCount,
                snapshot.Capacity,
                serverOptions.Tls.Enabled,
                configuredTlsServerName,
                responsibilities,
                heartbeatSeconds);
            return Results.Ok(new ClientServiceCatalog(1, [entry]));
        });

        return endpoints;
    }

    private static string NormalizeHost(string host, string requestHost) =>
        string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "::"
            ? requestHost
            : host;

    private sealed record ClientServiceCatalog(int SchemaVersion, IReadOnlyList<ClientServiceEntry> Services);

    private sealed record ClientServiceEntry(
        string ServiceId,
        string Role,
        string Host,
        int Port,
        string Region,
        string Version,
        int PlayerCount,
        int Capacity,
        bool TlsEnabled,
        string TlsServerName,
        IReadOnlyList<string> Responsibilities,
        int HeartbeatIntervalSeconds);
}
