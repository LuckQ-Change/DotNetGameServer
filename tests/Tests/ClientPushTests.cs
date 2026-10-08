using System.Net.Sockets;

using Core;
using Core.Configuration;
using Core.Networking;

using DotNetGameServer;

using Xunit;

namespace Tests;

public sealed class ClientPushTests
{
    [Fact]
    public async Task ServerCanPushToTheConnectionThatSentARequest()
    {
        var handler = new RememberingHandler();
        var server = new PushServer(handler, new ServerOptions
        {
            IdleTimeoutMs = 5_000,
            ReceiveTimeoutMs = 5_000,
            MaxConcurrentConnections = 2,
            ConnectionQueueCapacity = 2
        });
        await server.StartAsync(0);
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", server.ListeningPort!.Value);
            var stream = client.GetStream();
            await ProtobufFrameCodec.WriteEnvelopeAsync(stream, new envelope
            {
                Type = ServerQueryRequest.Descriptor.FullName,
                Channel = CommunicationChannel.Client,
                RequestId = "push-1"
            }, 64 * 1024);

            var response = await ProtobufFrameCodec.ReadEnvelopeAsync(stream, 64 * 1024);
            Assert.NotNull(response);
            Assert.Equal(0, response!.ErrorCode);
            Assert.False(string.IsNullOrWhiteSpace(handler.ConnectionId));

            var pushed = new ClientHeartbeatResponse
            {
                Success = true,
                Code = 0,
                Message = "push",
                ServerTimeUnix = 1,
                RecommendedIntervalSeconds = 20
            };
            Assert.True(await server.TryPushAsync(handler.ConnectionId!, pushed));

            var push = await ProtobufFrameCodec.ReadEnvelopeAsync(stream, 64 * 1024);
            Assert.NotNull(push);
            Assert.Equal(ClientHeartbeatResponse.Descriptor.FullName, push!.Type);
            Assert.Equal(CommunicationChannel.Client, push.Channel);
            Assert.Equal("push", ClientHeartbeatResponse.Parser.ParseFrom(push.Payload).Message);
            Assert.True(server.TryDisconnect(handler.ConnectionId!));
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private sealed class PushServer : BaseGameServer
    {
        public PushServer(RememberingHandler handler, ServerOptions options)
            : base("push-test", null, options)
        {
            RegisterClientMessageHandler(handler);
        }
    }

    private sealed class RememberingHandler : IMessageHandler<ServerQueryRequest, ServerQueryResponse>
    {
        public string? ConnectionId { get; private set; }

        public Task<ServerQueryResponse?> HandleAsync(ServerQueryRequest request, IServerContext context)
        {
            ConnectionId = context.ConnectionId;
            return Task.FromResult<ServerQueryResponse?>(new ServerQueryResponse());
        }
    }
}
