using System.Net.Sockets;

using Core;
using Core.Configuration;

using DotNetGameServer;

using Google.Protobuf;

using Xunit;

namespace Tests;

public sealed class TcpServerIntegrationTests
{
    [Fact]
    public async Task UnknownMessage_ReturnsStructuredError()
    {
        var server = new TestServer(new ServerOptions
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
            var request = new envelope
            {
                Type = "NoSuchRequest",
                Channel = CommunicationChannel.Client,
                RequestId = "request-1",
                Payload = ByteString.Empty
            }.ToByteArray();
            var requestLength = new byte[sizeof(int)];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(requestLength, request.Length);
            await stream.WriteAsync(requestLength);
            await stream.WriteAsync(request);

            var lengthBuffer = new byte[4];
            await stream.ReadExactlyAsync(lengthBuffer);
            var responseBuffer = new byte[System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer)];
            await stream.ReadExactlyAsync(responseBuffer);
            var response = envelope.Parser.ParseFrom(responseBuffer);

            Assert.Equal(404, response.ErrorCode);
            Assert.Equal("unknown_message_type", response.ErrorMessage);
            Assert.Equal("request-1", response.RequestId);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private sealed class TestServer(ServerOptions options) : BaseGameServer("test", null, options);
}
