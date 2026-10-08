using Client;
using Client.Discovery;

using Core;
using Core.Configuration;

using DotNetGameServer;

using Xunit;

namespace Tests;

public sealed class HotUpdateTests
{
    [Fact]
    public async Task ReplaceHandler_KeepsConnectionAndFinishesOldRequest()
    {
        var current = new VersionHandler("v1", gate: true);
        var server = new HotServer(current);
        await server.StartAsync(0);
        try
        {
            var port = server.ListeningPort!.Value;
            await using var client = GameNetworkClient.CreateTcp(new ServiceEndpoint
            {
                Host = "127.0.0.1",
                Port = port,
                Role = "hot",
                Responsibilities = ["hot"]
            });
            client.AutoHeartbeat = false;
            await client.ConnectAsync(new ServiceQuery { Responsibility = "hot" }, CancellationToken.None);

            var first = client.RequestResultAsync(
                new ClientHeartbeatRequest(),
                ClientHeartbeatResponse.Parser,
                CancellationToken.None);
            await current.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            server.ReplaceClientMessageHandler(new VersionHandler("v2"));
            current.Release.TrySetResult();

            var firstResult = await first;
            var second = await client.RequestResultAsync(
                new ClientHeartbeatRequest(),
                ClientHeartbeatResponse.Parser,
                CancellationToken.None);

            Assert.Equal("v1", firstResult.Response!.Message);
            Assert.Equal("v2", second.Response!.Message);
            Assert.True(client.IsConnected);
            Assert.Equal(port, server.ListeningPort);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task LoadPlugin_ReplacesHandlerWithoutStoppingListener()
    {
        var server = new HotServer(new VersionHandler("host"));
        await server.StartAsync(0);
        try
        {
            var port = server.ListeningPort!.Value;
            await using var client = GameNetworkClient.CreateTcp(new ServiceEndpoint
            {
                Host = "127.0.0.1",
                Port = port,
                Role = "hot",
                Responsibilities = ["hot"]
            });
            client.AutoHeartbeat = false;
            await client.ConnectAsync(new ServiceQuery { Responsibility = "hot" }, CancellationToken.None);

            var pluginPath = Path.Combine(AppContext.BaseDirectory, "ReloadPlugin.dll");
            var loaded = await server.LoadPluginAsync(pluginPath);
            var result = await client.RequestResultAsync(
                new ClientHeartbeatRequest(),
                ClientHeartbeatResponse.Parser,
                CancellationToken.None);

            Assert.Equal("sample-hot-plugin", loaded.PluginName);
            Assert.True(loaded.PreviousPluginReleased);
            Assert.Equal("plugin", result.Response!.Message);
            Assert.True(client.IsConnected);
            Assert.Equal(port, server.ListeningPort);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private sealed class HotServer : BaseGameServer
    {
        public HotServer(IMessageHandler<ClientHeartbeatRequest, ClientHeartbeatResponse> handler)
            : base("hot-update", null, new ServerOptions(), new AuthenticationOptions())
        {
            RegisterClientMessageHandler(handler);
        }
    }

    private sealed class VersionHandler : IMessageHandler<ClientHeartbeatRequest, ClientHeartbeatResponse>
    {
        private readonly string _version;
        private readonly bool _gate;

        public VersionHandler(string version, bool gate = false)
        {
            _version = version;
            _gate = gate;
        }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ClientHeartbeatResponse?> HandleAsync(
            ClientHeartbeatRequest request,
            IServerContext context)
        {
            if (_gate)
            {
                Entered.TrySetResult();
                await Release.Task;
            }

            return new ClientHeartbeatResponse { Success = true, Code = 0, Message = _version };
        }
    }
}
