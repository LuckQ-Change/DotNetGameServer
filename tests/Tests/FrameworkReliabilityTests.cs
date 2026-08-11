using System.Diagnostics;
using System.Net.Sockets;

using Core;
using Core.Configuration;
using Core.Networking;

using DotNetGameServer;

using Google.Protobuf;

using Xunit;

namespace Tests;

public sealed class FrameworkReliabilityTests
{
    [Fact]
    public async Task MalformedBusinessPayload_ReturnsStructuredBadRequest()
    {
        var server = new QueryServer(new ServerOptions());
        await server.StartAsync(0);
        try
        {
            var response = await SendAsync(server, new envelope
            {
                Type = ServerQueryRequest.Descriptor.FullName,
                Payload = ByteString.CopyFrom([0xFF]),
                RequestId = "malformed",
                Channel = CommunicationChannel.Client
            });

            Assert.Equal(ProtocolError.InvalidRequest, response.ErrorCode);
            Assert.Equal("invalid_request_payload", response.ErrorMessage);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task OversizedRequestMetadata_ReturnsStructuredBadRequest()
    {
        var server = new QueryServer(new ServerOptions { MaxRequestIdLength = 8 });
        await server.StartAsync(0);
        try
        {
            var response = await SendAsync(server, new envelope
            {
                Type = ServerQueryRequest.Descriptor.FullName,
                Payload = new ServerQueryRequest().ToByteString(),
                RequestId = "request-id-is-too-long",
                Channel = CommunicationChannel.Client
            });

            Assert.Equal(ProtocolError.InvalidRequest, response.ErrorCode);
            Assert.Equal("invalid_request_id", response.ErrorMessage);
            Assert.Equal("request-", response.RequestId);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task CooperativeHandlerTimeout_ReturnsStructuredTimeout()
    {
        var server = new QueryServer(new ServerOptions { RequestTimeoutMs = 50 }, delayUntilCancelled: true);
        await server.StartAsync(0);
        try
        {
            var response = await SendAsync(server, new envelope
            {
                Type = ServerQueryRequest.Descriptor.FullName,
                Payload = new ServerQueryRequest().ToByteString(),
                RequestId = "timeout",
                Channel = CommunicationChannel.Client
            });

            Assert.Equal(ProtocolError.RequestTimeout, response.ErrorCode);
            Assert.Equal("request_timeout", response.ErrorMessage);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task UncooperativeHandlerTimeout_StillReleasesRequestProcessor()
    {
        var server = new UncooperativeServer(new ServerOptions { RequestTimeoutMs = 50 });
        await server.StartAsync(0);
        try
        {
            var response = await SendAsync(server, new envelope
            {
                Type = ServerQueryRequest.Descriptor.FullName,
                Payload = new ServerQueryRequest().ToByteString(),
                RequestId = "uncooperative-timeout",
                Channel = CommunicationChannel.Client
            });

            Assert.Equal(ProtocolError.RequestTimeout, response.ErrorCode);
            Assert.Equal("request_timeout", response.ErrorMessage);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task Components_StartInRegistrationOrder_AndStopInReverseOrder()
    {
        var events = new List<string>();
        var server = new ComponentServer(events);

        await server.StartAsync(0);
        await server.StopAsync();

        Assert.Equal(["start:first", "start:second", "stop:second", "stop:first"], events);
    }

    [Fact]
    public async Task Disconnect_CancelsInFlightHandler_WhenRequestTimeoutIsDisabled()
    {
        var handler = new CancellationAwareHandler();
        var server = new CancellationServer(handler, new ServerOptions
        {
            RequestTimeoutMs = 0,
            IdleTimeoutMs = 5_000
        });
        await server.StartAsync(0);
        try
        {
            using (var client = new TcpClient())
            {
                await client.ConnectAsync("127.0.0.1", server.ListeningPort!.Value);
                await ProtobufFrameCodec.WriteEnvelopeAsync(client.GetStream(), new envelope
                {
                    Type = ClientHeartbeatRequest.Descriptor.FullName,
                    Payload = new ClientHeartbeatRequest().ToByteString(),
                    RequestId = "disconnect-cancellation",
                    Channel = CommunicationChannel.Client
                }, 64 * 1024);
                await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            }

            await handler.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(handler.Cancelled.Task.IsCompletedSuccessfully);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ConcurrentInvalidMetadataResponses_RemainValidFrames()
    {
        const int requestCount = 64;
        var server = new QueryServer(new ServerOptions
        {
            MaxMessageTypeLength = 8,
            MaxConcurrentRequestsPerConnection = 8,
            MaxMessagesPerSecond = 1_000
        });
        await server.StartAsync(0);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", server.ListeningPort!.Value);
            var stream = client.GetStream();
            for (var index = 0; index < requestCount; index++)
            {
                await ProtobufFrameCodec.WriteEnvelopeAsync(stream, new envelope
                {
                    Type = "message-type-is-too-long",
                    RequestId = $"invalid-{index}",
                    Channel = CommunicationChannel.Client
                }, 64 * 1024);
            }

            var responseIds = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < requestCount; index++)
            {
                var response = await ProtobufFrameCodec.ReadEnvelopeAsync(stream, 64 * 1024)
                    ?? throw new EndOfStreamException("Server closed before returning every error response.");
                Assert.Equal(ProtocolError.InvalidRequest, response.ErrorCode);
                Assert.Equal("invalid_message_type", response.ErrorMessage);
                responseIds.Add(response.RequestId);
            }

            Assert.Equal(requestCount, responseIds.Count);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ConcurrentLifecycleCalls_InitializeAndDestroyComponentsOnce()
    {
        var component = new CountingComponent();
        var server = new LifecycleServer(component, new ServerOptions());

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.StartAsync(0)));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => server.StopAsync()));

        Assert.Equal(1, component.InitializeCount);
        Assert.Equal(1, component.DestroyCount);
    }

    [Fact]
    public async Task ShutdownTimeout_IsEnforcedForLegacyComponents()
    {
        var server = new LifecycleServer(new HangingDestroyComponent(), new ServerOptions
        {
            ShutdownTimeoutMs = 1_000
        });
        await server.StartAsync(0);

        var started = Stopwatch.GetTimestamp();
        await server.StopAsync();

        Assert.InRange(Stopwatch.GetElapsedTime(started), TimeSpan.FromMilliseconds(800), TimeSpan.FromSeconds(3));
    }

    private static async Task<envelope> SendAsync(BaseGameServer server, envelope request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", server.ListeningPort!.Value);
        var stream = client.GetStream();
        await ProtobufFrameCodec.WriteEnvelopeAsync(stream, request, 64 * 1024);
        return await ProtobufFrameCodec.ReadEnvelopeAsync(stream, 64 * 1024)
            ?? throw new EndOfStreamException("服务器未返回响应。");
    }

    private sealed class QueryServer : BaseGameServer
    {
        public QueryServer(ServerOptions options, bool delayUntilCancelled = false)
            : base("reliability-test", null, options)
        {
            RegisterClientMessageHandler(new QueryHandler(delayUntilCancelled));
        }
    }

    private sealed class QueryHandler(bool delayUntilCancelled)
        : IMessageHandler<ServerQueryRequest, ServerQueryResponse>
    {
        public async Task<ServerQueryResponse?> HandleAsync(
            ServerQueryRequest request,
            IServerContext context)
        {
            if (delayUntilCancelled)
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            return new ServerQueryResponse();
        }
    }

    private sealed class UncooperativeServer : BaseGameServer
    {
        public UncooperativeServer(ServerOptions options) : base("uncooperative-test", null, options)
        {
            RegisterClientMessageHandler(new UncooperativeHandler());
        }
    }

    private sealed class UncooperativeHandler : IMessageHandler<ServerQueryRequest, ServerQueryResponse>
    {
        public Task<ServerQueryResponse?> HandleAsync(ServerQueryRequest request, IServerContext context) =>
            new TaskCompletionSource<ServerQueryResponse?>(
                TaskCreationOptions.RunContinuationsAsynchronously).Task;
    }

    private sealed class CancellationServer : BaseGameServer
    {
        public CancellationServer(CancellationAwareHandler handler, ServerOptions options)
            : base("cancellation-test", null, options)
        {
            RegisterClientMessageHandler(handler);
        }
    }

    private sealed class CancellationAwareHandler
        : IMessageHandler<ClientHeartbeatRequest, ClientHeartbeatResponse>
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ClientHeartbeatResponse?> HandleAsync(
            ClientHeartbeatRequest request,
            IServerContext context)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            }
            catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
            {
                Cancelled.TrySetResult();
                throw;
            }
            return null;
        }
    }

    private sealed class LifecycleServer : BaseGameServer
    {
        public LifecycleServer(IServerComponent component, ServerOptions options)
            : base("lifecycle-test", null, options)
        {
            AddComponent(component);
        }
    }

    private sealed class CountingComponent : IServerComponent
    {
        private int _initializeCount;
        private int _destroyCount;
        public string Name => "counting";
        public int InitializeCount => Volatile.Read(ref _initializeCount);
        public int DestroyCount => Volatile.Read(ref _destroyCount);
        public Task InitializeAsync()
        {
            Interlocked.Increment(ref _initializeCount);
            return Task.CompletedTask;
        }
        public Task DestroyAsync()
        {
            Interlocked.Increment(ref _destroyCount);
            return Task.CompletedTask;
        }
    }

    private sealed class HangingDestroyComponent : IServerComponent
    {
        public string Name => "hanging-destroy";
        public Task InitializeAsync() => Task.CompletedTask;
        public Task DestroyAsync() => Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private sealed class ComponentServer : BaseGameServer
    {
        public ComponentServer(List<string> events) : base("component-test", null)
        {
            AddComponent(new FirstComponent(events));
            AddComponent(new SecondComponent(events));
        }
    }

    private abstract class RecordingComponent(string name, List<string> events) : IServerComponent
    {
        public string Name => name;

        public Task InitializeAsync()
        {
            events.Add($"start:{name}");
            return Task.CompletedTask;
        }

        public Task DestroyAsync()
        {
            events.Add($"stop:{name}");
            return Task.CompletedTask;
        }
    }

    private sealed class FirstComponent(List<string> events) : RecordingComponent("first", events);
    private sealed class SecondComponent(List<string> events) : RecordingComponent("second", events);
}
