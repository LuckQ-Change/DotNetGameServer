using Core;

using Hosting;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Xunit;

namespace Tests;

public sealed class GameServerHostIntegrationTests
{
    [Fact]
    public async Task RunAsync_StartsAndGracefullyStopsConfiguredServer()
    {
        HostTestServer? createdServer = null;
        var definition = new GameServerDefinition
        {
            Role = "host-integration-test",
            DefaultPort = 0,
            RequiresProductionTokenSecret = false,
            ConfigureServices = (services, _) => services.AddHostedService<StopHostService>(),
            CreateServer = context => createdServer = new HostTestServer(context.ServerOptions)
        };

        await GameServerHost.RunAsync(
            ["--environment=Development", "--urls=http://127.0.0.1:0", "--port=0"],
            definition).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(createdServer);
        Assert.False(createdServer.IsRunning);
        Assert.Null(createdServer.ListeningPort);
    }

    private sealed class StopHostService(IHostApplicationLifetime lifetime) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
            if (lifetime.ApplicationStarted.IsCancellationRequested)
                started.TrySetResult();
            await started.Task.WaitAsync(stoppingToken);
            lifetime.StopApplication();
        }
    }

    private sealed class HostTestServer(Core.Configuration.ServerOptions options)
        : BaseGameServer("host-integration-test", null, options);
}
