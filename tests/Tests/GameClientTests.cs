using Client;
using Client.Discovery;

using Core;
using Core.Configuration;

using DotNetGameServer;

using Shared;

using Xunit;

namespace Tests;

public sealed class GameClientTests
{
    [Fact]
    public async Task RequestResult_KeepsBusinessPayloadOnFailure()
    {
        var server = new RejectingServer();
        await server.StartAsync(0);
        try
        {
            await using var client = GameNetworkClient.CreateTcp(new ServiceEndpoint
            {
                Host = "127.0.0.1",
                Port = server.ListeningPort!.Value,
                Role = "reject",
                Responsibilities = ["reject"]
            });
            await client.ConnectAsync(new ServiceQuery { Responsibility = "reject" }, CancellationToken.None);

            var result = await client.RequestResultAsync(
                new ClientHeartbeatRequest(),
                ClientHeartbeatResponse.Parser,
                CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(Shared.ProtocolError.InvalidRequest, result.ErrorCode);
            Assert.Equal("message_required", result.Response!.Message);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task On_ReceivesServerPush()
    {
        var handler = new RememberingHandler();
        var server = new PushServer(handler);
        await server.StartAsync(0);
        try
        {
            await using var client = GameNetworkClient.CreateTcp(new ServiceEndpoint
            {
                Host = "127.0.0.1",
                Port = server.ListeningPort!.Value,
                Role = "push",
                Responsibilities = ["push"]
            });
            ClientHeartbeatResponse? pushed = null;
            using var subscription = client.On(
                ClientHeartbeatResponse.Parser,
                message => pushed = message);
            await client.ConnectAsync(new ServiceQuery { Responsibility = "push" }, CancellationToken.None);

            await client.RequestAsync(
                new ServerQueryRequest(),
                ServerQueryResponse.Parser,
                CancellationToken.None);
            Assert.False(string.IsNullOrWhiteSpace(handler.ConnectionId));

            Assert.True(await server.TryPushAsync(
                handler.ConnectionId!,
                new ClientHeartbeatResponse { Success = true, Message = "hello-push" }));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (pushed is null && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.NotNull(pushed);
            Assert.Equal("hello-push", pushed!.Message);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task RequestResult_RetriesOnceAfter401Refresh()
    {
        var server = new AuthRequiredServer();
        await server.StartAsync(0);
        try
        {
            await using var client = GameNetworkClient.CreateTcp(new ServiceEndpoint
            {
                Host = "127.0.0.1",
                Port = server.ListeningPort!.Value,
                Role = "auth",
                Responsibilities = ["auth"]
            });
            await client.ConnectAsync(new ServiceQuery { Responsibility = "auth" }, CancellationToken.None);

            var denied = await client.RequestResultAsync(
                new ServerQueryRequest(),
                ServerQueryResponse.Parser,
                CancellationToken.None);
            Assert.False(denied.Success);
            Assert.Equal(Shared.ProtocolError.AuthenticationRequired, denied.ErrorCode);

            var refreshCount = 0;
            client.TokenRefreshHandler = async cancellationToken =>
            {
                refreshCount++;
                var login = await client.RequestResultAsync(
                    new ClientHeartbeatRequest(),
                    ClientHeartbeatResponse.Parser,
                    cancellationToken);
                Assert.True(login.Success);
                return AccessTokenGrant.FromAccessToken(login.Response!.Message);
            };

            var granted = await client.RequestResultAsync(
                new ServerQueryRequest(),
                ServerQueryResponse.Parser,
                CancellationToken.None);
            Assert.True(granted.Success);
            Assert.Equal(1, refreshCount);
            Assert.False(string.IsNullOrWhiteSpace(client.AccessToken));
            Assert.Equal("user-1", client.CurrentAccessToken!.UserId);
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public void ProtocolCompatibility_RejectsDifferentMajor()
    {
        Assert.True(Shared.ProtocolCompatibility.AreCompatible("1.0.0", "1.9.0"));
        Assert.False(Shared.ProtocolCompatibility.AreCompatible("1.0.0", "2.0.0"));
    }

    private sealed class RejectingServer : BaseGameServer
    {
        public RejectingServer()
            : base("client-reject", null, new ServerOptions(), new AuthenticationOptions())
        {
            RegisterClientMessageHandler(new RejectHandler());
        }
    }

    private sealed class RejectHandler : IMessageHandler<ClientHeartbeatRequest, ClientHeartbeatResponse>
    {
        public Task<ClientHeartbeatResponse?> HandleAsync(
            ClientHeartbeatRequest request,
            IServerContext context) =>
            Task.FromResult<ClientHeartbeatResponse?>(new ClientHeartbeatResponse
            {
                Success = false,
                Code = 400,
                Message = "message_required"
            });
    }

    private sealed class PushServer : BaseGameServer
    {
        public PushServer(RememberingHandler handler)
            : base("client-push", null, new ServerOptions(), new AuthenticationOptions())
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

    private sealed class AuthRequiredServer : BaseGameServer
    {
        public AuthRequiredServer()
            : base(
                "auth-test",
                null,
                new ServerOptions(),
                new AuthenticationOptions
                {
                    Audience = "auth-test",
                    TokenSecret = "test-secret-at-least-32-characters",
                    RequireClientAuthentication = true
                })
        {
            RegisterAnonymousClientMessageHandler(new IssueTokenHandler());
            RegisterClientMessageHandler(new RememberingHandler());
        }
    }

    private sealed class IssueTokenHandler : IMessageHandler<ClientHeartbeatRequest, ClientHeartbeatResponse>
    {
        public Task<ClientHeartbeatResponse?> HandleAsync(
            ClientHeartbeatRequest request,
            IServerContext context)
        {
            var secret = context.GetComponent<Core.Components.ITokenSecretProvider>()?.GetSecret();
            var token = TokenSigner.ComposeUserToken("user-1", context.TokenAudience, secret!, 3600);
            return Task.FromResult<ClientHeartbeatResponse?>(new ClientHeartbeatResponse
            {
                Success = true,
                Message = token
            });
        }
    }
}
