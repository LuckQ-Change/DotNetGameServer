using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServerHost.HealthChecks;

public class ServerHealthCheck(CurrentServerAccessor accessor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var server = accessor.Server;
        if (server == null)
            return Task.FromResult(HealthCheckResult.Unhealthy("server not started"));
        if (!server.IsRunning)
            return Task.FromResult(HealthCheckResult.Unhealthy("TCP listener is not running"));
        if (server.ListeningPort == null)
            return Task.FromResult(HealthCheckResult.Unhealthy("not listening on any port"));

        return Task.FromResult(HealthCheckResult.Healthy("OK"));
    }
}
