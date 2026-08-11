using Core;

using Hosting;
using Hosting.HealthChecks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Xunit;

namespace Tests;

public sealed class HostingHealthCheckTests
{
    [Fact]
    public async Task Liveness_TracksServerLifecycle()
    {
        var accessor = new CurrentServerAccessor();
        var healthCheck = new ServerHealthCheck(accessor);
        var context = new HealthCheckContext();

        Assert.Equal(HealthStatus.Unhealthy, (await healthCheck.CheckHealthAsync(context)).Status);

        var server = new HealthServer();
        await server.StartAsync(0);
        try
        {
            accessor.Set(server);
            Assert.Equal(HealthStatus.Healthy, (await healthCheck.CheckHealthAsync(context)).Status);
        }
        finally
        {
            accessor.Clear();
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Readiness_ReportsUnhealthyComponents()
    {
        var accessor = new CurrentServerAccessor();
        var server = new HealthServer(new UnhealthyComponent());
        await server.StartAsync(0);
        try
        {
            accessor.Set(server);
            var result = await new ReadinessHealthCheck(accessor)
                .CheckHealthAsync(new HealthCheckContext());

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.Contains("dependency:unavailable", result.Description, StringComparison.Ordinal);
        }
        finally
        {
            accessor.Clear();
            await server.StopAsync();
        }
    }

    private sealed class HealthServer : BaseGameServer
    {
        public HealthServer(IServerComponent? component = null) : base("health-test", null)
        {
            if (component is not null) AddComponent(component);
        }
    }

    private sealed class UnhealthyComponent : IServerComponent, IServerComponentHealth
    {
        public string Name => "dependency";
        public Task InitializeAsync() => Task.CompletedTask;
        public Task DestroyAsync() => Task.CompletedTask;
        public Task<ServerComponentHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ServerComponentHealth(Name, false, "unavailable"));
    }
}
