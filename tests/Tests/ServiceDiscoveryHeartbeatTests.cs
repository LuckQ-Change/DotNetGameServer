using Core.Components;
using Core.Configuration;
using Core.Directory;
using Core.Heartbeat;

using Xunit;

namespace Tests;

public sealed class ServiceDiscoveryHeartbeatTests
{
    private const string Secret = "heartbeat-integration-secret-at-least-32-chars";

    [Fact]
    public async Task Heartbeat_RegistersServerMetadataInDiscoveryRegistry()
    {
        var registry = new MemoryServerRegistry();
        var discovery = new ServiceDiscoveryServer(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceDiscoveryServer>.Instance,
            registry,
            new ServerOptions(),
            new AuthenticationOptions
            {
                TokenSecret = Secret,
                Audience = "discovery-audience",
                RequireInternalAuthentication = true
            });
        await discovery.StartAsync(0);
        var load = new MutableServerLoadProvider(200);
        load.SetPlayerCount(35);
        var heartbeat = new HeartbeatSenderService(
            new ServiceRegistrationOptions
            {
                ServerId = "game-1",
                ServerType = "game",
                Host = "127.0.0.1",
                Port = 7001,
                Region = "cn",
                Version = "2.0.0",
                TokenAudience = "game-audience",
                TlsEnabled = true,
                TlsServerName = "game.internal",
                DiscoveryHost = "127.0.0.1",
                DiscoveryPort = discovery.ListeningPort!.Value,
                DiscoveryAudience = "discovery-audience",
                IntervalSeconds = 30
            },
            Secret,
            load);
        var heartbeatStopped = false;
        try
        {
            await heartbeat.InitializeAsync();
            ServerRegistration? registration = null;
            for (var attempt = 0; attempt < 50 && registration is null; attempt++)
            {
                registration = await registry.GetAsync("game-1");
                if (registration is null) await Task.Delay(20);
            }

            Assert.NotNull(registration);
            Assert.Equal(35, registration.PlayerCount);
            Assert.Equal(200, registration.Capacity);
            Assert.Equal("game-audience", registration.TokenAudience);
            Assert.True(registration.TlsEnabled);
            Assert.Equal("game.internal", registration.TlsServerName);

            await heartbeat.DestroyAsync();
            heartbeatStopped = true;
            Assert.Null(await registry.GetAsync("game-1"));
        }
        finally
        {
            if (!heartbeatStopped) await heartbeat.DestroyAsync();
            await discovery.StopAsync();
        }
    }
}
