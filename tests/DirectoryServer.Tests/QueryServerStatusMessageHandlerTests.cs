using System.Threading.Tasks;
using DirectoryServer.Components;
using DirectoryServer.Handlers;
using DotNetGameServer;
using Xunit;

namespace DirectoryServer.Tests;

public class QueryServerStatusMessageHandlerTests
{
    [Fact]
    public async Task Status_Found_And_NotFound()
    {
        var reg = new InMemoryServerRegistryComponent();
        await reg.InitializeAsync();
        try
        {
            reg.UpsertHeartbeat("s-ok", "logic", "cn-sh", "10.0.0.9", 5599, playerCount: 1, capacity: 10, status: "online", avgRttMs: 5);
            var ctx = new TestServerContext();
            ctx.AddComponent<IServerRegistryComponent>(reg);

            var handler = new QueryServerStatusMessageHandler();
            var ok = await handler.HandleAsync(new QueryServerStatusRequest { ServerId = "s-ok" }, ctx);
            Assert.NotNull(ok);
            Assert.True(ok!.Found);
            Assert.Equal("s-ok", ok.Info.Id);

            var miss = await handler.HandleAsync(new QueryServerStatusRequest { ServerId = "s-missing" }, ctx);
            Assert.NotNull(miss);
            Assert.False(miss!.Found);
        }
        finally
        {
            await reg.DestroyAsync();
        }
    }
}