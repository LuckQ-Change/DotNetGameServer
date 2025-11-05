using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServerHost.HealthChecks
{
    public class ServerHealthCheck : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            // 在这里，您可以添加检查服务器依赖项（如数据库、其他服务等）的逻辑。
            // 为简单起见，我们只返回一个健康状态。
            // 简化健康检查逻辑：直接返回健康状态，避免始终为真条件（S2583）
            return Task.FromResult(
                HealthCheckResult.Healthy("OK"));
        }
    }
}