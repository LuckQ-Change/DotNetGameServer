using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hosting.HealthChecks;

public class ReadinessHealthCheck(CurrentServerAccessor accessor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var server = accessor.Server;
        if (server == null)
            return Task.FromResult(HealthCheckResult.Unhealthy("server not started"));
        if (!server.IsRunning)
            return Task.FromResult(HealthCheckResult.Unhealthy("not ready"));

        return Task.FromResult(HealthCheckResult.Healthy("ready"));
    }
}
