using System.Linq;
using System.Threading.Tasks;
using DirectoryServer.Components;
using DirectoryServer.Handlers;
using DotNetGameServer;
using Xunit;

namespace DirectoryServer.Tests;

public class QueryServersMessageHandlerTests
{
    [Fact]
    public async Task Load_Strategy_Orders_By_Load_And_Respects_Limit()
    {
        var reg = new InMemoryServerRegistryComponent();
        await reg.InitializeAsync();
        try
        {
            reg.UpsertHeartbeat("s1", "logic", "cn-sh", "10.0.0.1", 5501, playerCount: 30, capacity: 100, status: "online", avgRttMs: 50);
            reg.UpsertHeartbeat("s2", "logic", "cn-sh", "10.0.0.2", 5502, playerCount: 10, capacity: 100, status: "online", avgRttMs: 30);
            reg.UpsertHeartbeat("s3", "logic", "cn-sh", "10.0.0.3", 5503, playerCount: 60, capacity: 100, status: "online", avgRttMs: 10);

            var ctx = new TestServerContext();
            ctx.AddComponent<IServerRegistryComponent>(reg);

            var handler = new QueryServersMessageHandler();
            var resp = await handler.HandleAsync(new QueryServersRequest
            {
                Strategy = "load",
                Limit = 2
            }, ctx);

            Assert.NotNull(resp);
            Assert.True(resp!.Servers.Count == 2);
            var ids = resp.Servers.Select(s => s.Id).ToList();
            // Expected by load ascending: s2 (10%), s1 (30%), s3 (60%)
            Assert.Equal("s2", ids[0]);
            Assert.Equal("s1", ids[1]);
        }
        finally
        {
            await reg.DestroyAsync();
        }
    }
}