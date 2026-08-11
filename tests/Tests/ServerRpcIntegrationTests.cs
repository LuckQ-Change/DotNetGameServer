using System.Net.Sockets;

using Core;
using Core.Configuration;
using Core.Directory;
using Core.Networking;
using Core.Rpc;

using DotNetGameServer;

using Google.Protobuf;

using Xunit;

namespace Tests;

public sealed class ServerRpcIntegrationTests
{
    private const string Secret = "rpc-integration-test-secret-at-least-32-chars";

    [Fact]
    public async Task CallAsync_UsesAuthenticatedInternalChannelAndReusesConnection()
    {
        var registry = new MemoryServerRegistry();
        await registry.RegisterAsync(new ServerRegistration
        {
            ServerId = "worker-1",
            ServerType = "worker",
            Host = "127.0.0.1",
            Port = 6001,
            Capacity = 100,
            PlayerCount = 1,
            Status = "online",
            TokenAudience = "worker-audience"
        });
        var target = new InternalQueryServer(registry);
        await target.StartAsync(0);
        await using var pool = new ServerConnectionPool(new ServerRpcOptions
        {
            SourceServerId = "caller-1",
            TokenSecret = Secret,
            RequestTimeoutMs = 2_000
        });
        try
        {
            var resolver = new FixedServiceResolver([
                new ServerEndpoint(
                    "target-1",
                    "query",
                    "127.0.0.1",
                    target.ListeningPort!.Value,
                    "target-audience")
            ]);
            var client = new ServerRpcClient(resolver, pool, new ServerRpcOptions
            {
                SourceServerId = "caller-1",
                TokenSecret = Secret,
                RequestTimeoutMs = 2_000,
                MaxAttempts = 2
            });

            var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
                client.CallAsync<ServerQueryRequest, ServerQueryResponse>(
                    "query",
                    new ServerQueryRequest { ServerType = "worker", Limit = 1 })));

            Assert.All(responses, response =>
            {
                Assert.Single(response.Servers);
                Assert.Equal("worker-1", response.Servers[0].ServerId);
            });
            Assert.Equal(1, target.GetRuntimeSnapshot().CurrentConnections);
        }
        finally
        {
            await target.StopAsync();
        }
    }

    [Fact]
    public async Task InternalChannel_WithoutServiceToken_IsRejected()
    {
        var target = new InternalQueryServer(new MemoryServerRegistry());
        await target.StartAsync(0);
        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync("127.0.0.1", target.ListeningPort!.Value);
            var request = new envelope
            {
                Type = ServerQueryRequest.Descriptor.FullName,
                Payload = new ServerQueryRequest { ServerType = "worker" }.ToByteString(),
                RequestId = "missing-service-token",
                Channel = CommunicationChannel.Internal
            };
            await ProtobufFrameCodec.WriteEnvelopeAsync(tcpClient.GetStream(), request, 64 * 1024);
            var response = await ProtobufFrameCodec.ReadEnvelopeAsync(tcpClient.GetStream(), 64 * 1024)
                ?? throw new EndOfStreamException("目标服务器未返回鉴权错误。");

            Assert.Equal(401, response.ErrorCode);
            Assert.Equal("authentication_required", response.ErrorMessage);
        }
        finally
        {
            await target.StopAsync();
        }
    }

    [Fact]
    public async Task DiscoveryResolver_QueriesDiscoveryServerBeforeRpcCall()
    {
        var workerRegistry = new MemoryServerRegistry();
        await workerRegistry.RegisterAsync(new ServerRegistration
        {
            ServerId = "worker-2",
            ServerType = "worker",
            Host = "127.0.0.1",
            Port = 6002,
            Capacity = 100,
            Status = "online"
        });
        var target = new InternalQueryServer(workerRegistry);
        var discoveryRegistry = new MemoryServerRegistry();
        var discovery = new ServiceDiscoveryServer(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ServiceDiscoveryServer>.Instance,
            discoveryRegistry,
            new ServerOptions(),
            new AuthenticationOptions
            {
                TokenSecret = Secret,
                Audience = "discovery-audience",
                RequireInternalAuthentication = true
            });
        await target.StartAsync(0);
        await discovery.StartAsync(0);
        await discoveryRegistry.RegisterAsync(new ServerRegistration
        {
            ServerId = "query-1",
            ServerType = "query",
            Host = "127.0.0.1",
            Port = target.ListeningPort!.Value,
            Capacity = 100,
            Status = "online",
            TokenAudience = "target-audience"
        });
        var rpcOptions = new ServerRpcOptions
        {
            SourceServerId = "caller-2",
            TokenSecret = Secret,
            RequestTimeoutMs = 2_000
        };
        var pool = new ServerConnectionPool(rpcOptions);
        try
        {
            var resolver = new DiscoveryServiceResolver(
                pool,
                new ServiceRegistrationOptions
                {
                    DiscoveryHost = "127.0.0.1",
                    DiscoveryPort = discovery.ListeningPort!.Value,
                    DiscoveryAudience = "discovery-audience"
                },
                rpcOptions);
            var client = new ServerRpcClient(resolver, pool, rpcOptions);
            var response = await client.CallAsync<ServerQueryRequest, ServerQueryResponse>(
                "query",
                new ServerQueryRequest { ServerType = "worker", Limit = 1 });

            Assert.Single(response.Servers);
            Assert.Equal("worker-2", response.Servers[0].ServerId);
        }
        finally
        {
            await pool.DisposeAsync();
            await target.StopAsync();
            await discovery.StopAsync();
        }
    }

    [Fact]
    public async Task IdempotentCall_WhenFirstEndpointFails_RetriesNextEndpoint()
    {
        var registry = new MemoryServerRegistry();
        await registry.RegisterAsync(new ServerRegistration
        {
            ServerId = "worker-3",
            ServerType = "worker",
            Host = "127.0.0.1",
            Port = 6003,
            Capacity = 100,
            Status = "online"
        });
        var target = new InternalQueryServer(registry);
        await target.StartAsync(0);
        var options = new ServerRpcOptions
        {
            SourceServerId = "caller-retry",
            TokenSecret = Secret,
            ConnectTimeoutMs = 200,
            RequestTimeoutMs = 2_000,
            MaxAttempts = 2
        };
        var pool = new ServerConnectionPool(options);
        try
        {
            var resolver = new FixedServiceResolver([
                new ServerEndpoint("failed", "query", "127.0.0.1", 65534, "target-audience"),
                new ServerEndpoint(
                    "healthy",
                    "query",
                    "127.0.0.1",
                    target.ListeningPort!.Value,
                    "target-audience")
            ]);
            var client = new ServerRpcClient(resolver, pool, options);
            var response = await client.CallAsync<ServerQueryRequest, ServerQueryResponse>(
                "query",
                new ServerQueryRequest { ServerType = "worker", Limit = 1 },
                new ServerCallOptions { Idempotent = true, MaxAttempts = 2 });

            Assert.Single(response.Servers);
            Assert.Equal("worker-3", response.Servers[0].ServerId);
        }
        finally
        {
            await pool.DisposeAsync();
            await target.StopAsync();
        }
    }

    [Fact]
    public async Task IdempotentCall_RetriesSingleEndpointWithSameRequestId()
    {
        var endpoint = new ServerEndpoint("only", "query", "127.0.0.1", 6000, "target-audience");
        var pool = new RecordingRetryPool();
        var client = new ServerRpcClient(
            new FixedServiceResolver([endpoint]),
            pool,
            new ServerRpcOptions
            {
                SourceServerId = "caller-single-retry",
                TokenSecret = Secret,
                MaxAttempts = 2
            });

        var response = await client.CallAsync<ServerQueryRequest, ServerQueryResponse>(
            "query",
            new ServerQueryRequest(),
            new ServerCallOptions { Idempotent = true, MaxAttempts = 2 });

        Assert.NotNull(response);
        Assert.Equal(2, pool.RequestIds.Count);
        Assert.Single(pool.RequestIds.Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ConnectionPool_EvictsIdleConnections()
    {
        var target = new InternalQueryServer(new MemoryServerRegistry());
        await target.StartAsync(0);
        await using var pool = new ServerConnectionPool(new ServerRpcOptions
        {
            SourceServerId = "caller-pool-cleanup",
            TokenSecret = Secret,
            IdleConnectionTimeoutSeconds = 1,
            PoolCleanupIntervalSeconds = 1
        });
        try
        {
            var endpoint = new ServerEndpoint(
                "idle-target",
                "query",
                "127.0.0.1",
                target.ListeningPort!.Value,
                "target-audience");
            var client = new ServerRpcClient(new FixedServiceResolver([endpoint]), pool, new ServerRpcOptions
            {
                SourceServerId = "caller-pool-cleanup",
                TokenSecret = Secret,
                RequestTimeoutMs = 2_000
            });
            await client.CallAsync<ServerQueryRequest, ServerQueryResponse>("query", new ServerQueryRequest());
            Assert.Equal(1, pool.PooledConnectionCount);

            var deadline = DateTime.UtcNow.AddSeconds(4);
            while (pool.PooledConnectionCount != 0 && DateTime.UtcNow < deadline)
                await Task.Delay(100);
            Assert.Equal(0, pool.PooledConnectionCount);
        }
        finally
        {
            await target.StopAsync();
        }
    }

    private sealed class RecordingRetryPool : IServerConnectionPool
    {
        private int _attempt;
        public List<string> RequestIds { get; } = [];

        public Task<envelope> SendAsync(
            ServerEndpoint endpoint,
            envelope request,
            TimeSpan requestTimeout,
            CancellationToken cancellationToken = default)
        {
            RequestIds.Add(request.RequestId);
            if (Interlocked.Increment(ref _attempt) == 1)
                throw new IOException("simulated first-attempt transport failure");
            return Task.FromResult(new envelope
            {
                Type = ServerQueryResponse.Descriptor.FullName,
                Payload = new ServerQueryResponse().ToByteString(),
                RequestId = request.RequestId,
                Channel = CommunicationChannel.Internal
            });
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InternalQueryServer : BaseGameServer
    {
        public InternalQueryServer(IServerRegistry registry)
            : base(
                "internal-query-test",
                null,
                new ServerOptions(),
                new AuthenticationOptions
                {
                    TokenSecret = Secret,
                    Audience = "target-audience",
                    RequireInternalAuthentication = true
                },
                allowedChannels: [CommunicationChannel.Internal])
        {
            RegisterInternalMessageHandler(new ServerQueryHandler(registry));
        }
    }
}
