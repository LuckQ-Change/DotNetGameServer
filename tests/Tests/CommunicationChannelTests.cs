using System.Net.Sockets;

using Core;
using Core.Configuration;
using Core.Networking;

using DotNetGameServer;

using Google.Protobuf;

using Xunit;

namespace Tests;

public sealed class CommunicationChannelTests
{
    [Fact]
    public async Task ClientHeartbeat_WhenEnabled_ReturnsPong()
    {
        var server = new ClientServer(clientHeartbeatEnabled: true);
        await server.StartAsync(0);
        try
        {
            var response = await SendAsync(server, new envelope
            {
                Type = ClientHeartbeatRequest.Descriptor.FullName,
                Payload = new ClientHeartbeatRequest
                {
                    ClientTimeUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                }.ToByteString(),
                RequestId = "client-heartbeat-1",
                Channel = CommunicationChannel.Client
            });

            Assert.Equal(0, response.ErrorCode);
            Assert.Equal(CommunicationChannel.Client, response.Channel);
            var heartbeat = ClientHeartbeatResponse.Parser.ParseFrom(response.Payload);
            Assert.True(heartbeat.Success);
            Assert.Equal(20, heartbeat.RecommendedIntervalSeconds);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ClientHeartbeat_WhenDisabled_IsNotRegistered()
    {
        var server = new ClientServer(clientHeartbeatEnabled: false);
        await server.StartAsync(0);
        try
        {
            var response = await SendAsync(server, new envelope
            {
                Type = ClientHeartbeatRequest.Descriptor.FullName,
                Payload = new ClientHeartbeatRequest().ToByteString(),
                RequestId = "client-heartbeat-disabled",
                Channel = CommunicationChannel.Client
            });

            Assert.Equal(404, response.ErrorCode);
            Assert.Equal("unknown_message_type", response.ErrorMessage);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task ClientServer_RejectsInternalChannel()
    {
        var server = new ClientServer(clientHeartbeatEnabled: true);
        await server.StartAsync(0);
        try
        {
            var response = await SendAsync(server, new envelope
            {
                Type = ClientHeartbeatRequest.Descriptor.FullName,
                Payload = new ClientHeartbeatRequest().ToByteString(),
                RequestId = "wrong-channel",
                Channel = CommunicationChannel.Internal
            });

            Assert.Equal(403, response.ErrorCode);
            Assert.Equal("communication_channel_forbidden", response.ErrorMessage);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private static async Task<envelope> SendAsync(BaseGameServer server, envelope request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", server.ListeningPort!.Value);
        var stream = client.GetStream();
        await ProtobufFrameCodec.WriteEnvelopeAsync(stream, request, 64 * 1024);
        return await ProtobufFrameCodec.ReadEnvelopeAsync(stream, 64 * 1024)
            ?? throw new EndOfStreamException("测试服务器未返回响应。");
    }

    private sealed class ClientServer(bool clientHeartbeatEnabled)
        : BaseGameServer(
            "client-test",
            null,
            new ServerOptions(),
            new AuthenticationOptions(),
            clientHeartbeat: new ClientHeartbeatOptions
            {
                Enabled = clientHeartbeatEnabled,
                RecommendedIntervalSeconds = 20
            });
}
