using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Core;
using Core.Configuration;

using DotNetGameServer;
using DotNetGameServer.UnityClient;
using DotNetGameServer.UnityClient.Discovery;
using DotNetGameServer.UnityClient.Protocol;
using DotNetGameServer.UnityClient.Transport;

using Google.Protobuf;

using Xunit;

namespace UnityClientContractTests;

public sealed class UnityClientProtocolTests
{
    [Fact]
    public void UnityCodec_IsWireCompatibleWithServerGeneratedEnvelope()
    {
        var codec = new ProtobufEnvelopeCodec();
        var unity = new NetworkEnvelope
        {
            Type = "Game.PingRequest",
            Payload = [1, 2, 3],
            Token = "token",
            RequestId = "request-1",
            Version = "1.2.3",
            ErrorCode = 408,
            ErrorMessage = "timeout",
            Channel = DotNetGameServer.UnityClient.Protocol.CommunicationChannel.Client
        };

        var server = envelope.Parser.ParseFrom(codec.Encode(unity));
        Assert.Equal(unity.Type, server.Type);
        Assert.Equal(unity.Payload, server.Payload.ToByteArray());
        Assert.Equal(unity.Token, server.Token);
        Assert.Equal(unity.RequestId, server.RequestId);
        Assert.Equal(unity.Version, server.Version);
        Assert.Equal(unity.ErrorCode, server.ErrorCode);
        Assert.Equal(unity.ErrorMessage, server.ErrorMessage);
        Assert.Equal(DotNetGameServer.CommunicationChannel.Client, server.Channel);

        var roundTrip = codec.Decode(server.ToByteArray());
        Assert.Equal(unity.Type, roundTrip.Type);
        Assert.Equal(unity.Payload, roundTrip.Payload);
        Assert.Equal(unity.RequestId, roundTrip.RequestId);
    }

    [Fact]
    public void EndpointSelector_IsStableForSameRoutingKey()
    {
        ServiceEndpoint[] endpoints =
        [
            new() { ServiceId = "a", Host = "127.0.0.1", Port = 1, Capacity = 10, PlayerCount = 1 },
            new() { ServiceId = "b", Host = "127.0.0.1", Port = 2, Capacity = 10, PlayerCount = 5 },
            new() { ServiceId = "c", Host = "127.0.0.1", Port = 3, Capacity = 10, PlayerCount = 2 }
        ];

        var first = ServiceEndpointSelector.OrderForConnection(endpoints, "player-42");
        var second = ServiceEndpointSelector.OrderForConnection(endpoints, "player-42");

        Assert.Equal(first.Select(item => item.ServiceId), second.Select(item => item.ServiceId));
        Assert.Equal(3, first.Count);
    }

    [Fact]
    public async Task Client_CorrelatesRawRequestAndResponseAcrossPluggableTransport()
    {
        var codec = new ProtobufEnvelopeCodec();
        using var transport = new EchoTransport(codec);
        var endpoint = new ServiceEndpoint
        {
            ServiceId = "demo-1",
            Role = "demo",
            Host = "127.0.0.1",
            Port = 5500,
            Responsibilities = ["gameplay"]
        };
        var discovery = new StaticServiceDiscoveryProvider([endpoint]);
        using var client = new GameNetworkClient(transport, discovery, codec);

        var connected = await client.ConnectAsync(
            new ServiceQuery { Responsibility = "gameplay" },
            CancellationToken.None);
        var response = await client.RequestRawAsync("Game.PingRequest", [7, 8, 9], CancellationToken.None);

        Assert.Same(endpoint, connected);
        Assert.Equal("Game.PingResponse", response.Type);
        Assert.Equal([9, 8, 7], response.Payload);
    }

    [Fact]
    public async Task UnityTcpTransport_ExchangesGeneratedProtoWithRealServer()
    {
        var server = new HeartbeatServer();
        await server.StartAsync(0);
        try
        {
            var endpoint = new ServiceEndpoint
            {
                ServiceId = "heartbeat-test",
                Role = "heartbeat-test",
                Host = "127.0.0.1",
                Port = server.ListeningPort!.Value,
                Responsibilities = ["heartbeat"]
            };
            using var transport = new TcpNetworkTransport();
            using var client = new GameNetworkClient(
                transport,
                new StaticServiceDiscoveryProvider([endpoint]));
            await client.ConnectAsync(new ServiceQuery { Responsibility = "heartbeat" }, CancellationToken.None);

            var response = await client.RequestAsync(
                new ClientHeartbeatRequest { ClientTimeUnix = 123 },
                ClientHeartbeatResponse.Parser,
                CancellationToken.None);

            Assert.True(response.Success);
            Assert.True(response.ServerTimeUnix > 0);
            Assert.Equal(20, response.RecommendedIntervalSeconds);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private sealed class EchoTransport : INetworkTransport
    {
        private readonly IEnvelopeCodec _codec;
        private readonly Channel<byte[]> _responses;
        public bool IsConnected { get; private set; }
        public ServiceEndpoint CurrentEndpoint { get; private set; }

        public EchoTransport(IEnvelopeCodec codec)
        {
            _codec = codec;
            _responses = Channel.CreateUnbounded<byte[]>();
        }

        public Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken)
        {
            CurrentEndpoint = endpoint;
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] envelopeBytes, CancellationToken cancellationToken)
        {
            var request = _codec.Decode(envelopeBytes);
            var response = new NetworkEnvelope
            {
                Type = "Game.PingResponse",
                RequestId = request.RequestId,
                Payload = [9, 8, 7],
                Channel = DotNetGameServer.UnityClient.Protocol.CommunicationChannel.Client
            };
            return _responses.Writer.WriteAsync(_codec.Encode(response), cancellationToken).AsTask();
        }

        public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken) =>
            _responses.Reader.ReadAsync(cancellationToken).AsTask();

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            CurrentEndpoint = null;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }

    private sealed class HeartbeatServer() : BaseGameServer(
        "unity-heartbeat-test",
        null,
        new ServerOptions(),
        new AuthenticationOptions(),
        clientHeartbeat: new ClientHeartbeatOptions
        {
            Enabled = true,
            RecommendedIntervalSeconds = 20
        });
}
