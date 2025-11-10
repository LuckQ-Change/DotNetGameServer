using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServerHost.HealthChecks
{
    /// <summary>
    /// Liveness 探针：仅反映进程是否活着。
    /// </summary>
    public class ServerHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(HealthCheckResult.Healthy("OK"));
        }
    }
}