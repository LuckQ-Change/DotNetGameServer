using Core.Configuration;
using Core.Persistence;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hosting.HealthChecks;

public sealed class DatabaseHealthCheck(
    IServiceProvider serviceProvider,
    IOptions<DatabaseOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!options.Value.Enabled) return HealthCheckResult.Healthy("disabled");
        var probe = serviceProvider.GetService<IGameDatabaseHealthProbe>();
        if (probe is null)
            return HealthCheckResult.Unhealthy("database enabled but no health probe is registered");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.HealthCheckTimeoutSeconds));
        try
        {
            await probe.CheckHealthAsync(timeout.Token);
            return HealthCheckResult.Healthy("database responded");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy("database health check timed out");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("database connection failed", ex);
        }
    }
}
