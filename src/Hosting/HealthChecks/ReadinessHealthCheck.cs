using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hosting.HealthChecks;

public class ReadinessHealthCheck(CurrentServerAccessor accessor) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var server = accessor.Server;
        if (server == null)
            return HealthCheckResult.Unhealthy("server not started");
        if (!server.IsRunning)
            return HealthCheckResult.Unhealthy("not ready");

        var components = await server.GetComponentHealthAsync(cancellationToken);
        var unhealthy = components.Where(c => !c.Healthy).ToArray();
        if (unhealthy.Length > 0)
        {
            var details = string.Join(", ", unhealthy.Select(c => $"{c.Name}:{c.Message}"));
            return HealthCheckResult.Unhealthy(details);
        }

        return HealthCheckResult.Healthy("ready");
    }
}
