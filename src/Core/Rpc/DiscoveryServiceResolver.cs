using Core.Configuration;

using DotNetGameServer;

using Google.Protobuf;

using Shared;

namespace Core.Rpc;

/// <summary>通过框架服务发现协议查询可用服务器节点。</summary>
public sealed class DiscoveryServiceResolver(
    IServerConnectionPool connectionPool,
    ServiceRegistrationOptions discoveryOptions,
    ServerRpcOptions rpcOptions) : IServiceResolver
{
    public async Task<IReadOnlyList<ServerEndpoint>> ResolveAsync(
        string serverType,
        string? region,
        string? strategy,
        string? routingKey,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var endpoint = new ServerEndpoint(
            "service-discovery",
            "service-discovery",
            discoveryOptions.DiscoveryHost,
            discoveryOptions.DiscoveryPort,
            discoveryOptions.DiscoveryAudience,
            discoveryOptions.DiscoveryTlsEnabled,
            discoveryOptions.DiscoveryTlsServerName);
        var query = new ServerQueryRequest
        {
            ServerType = serverType,
            Region = region ?? string.Empty,
            Strategy = strategy ?? "load",
            RoutingKey = routingKey ?? string.Empty,
            Limit = Math.Max(1, limit)
        };
        var request = new envelope
        {
            Type = ServerQueryRequest.Descriptor.FullName,
            Payload = query.ToByteString(),
            RequestId = Guid.NewGuid().ToString("N"),
            Channel = CommunicationChannel.Internal,
            Token = TokenSigner.ComposeServiceToken(
                rpcOptions.SourceServerId,
                discoveryOptions.DiscoveryAudience,
                rpcOptions.TokenSecret,
                Math.Max(1, rpcOptions.TokenTtlSeconds))
        };
        var timeout = TimeSpan.FromMilliseconds(Math.Max(100, rpcOptions.RequestTimeoutMs));
        var response = await connectionPool.SendAsync(endpoint, request, timeout, cancellationToken);
        if (response.Channel != CommunicationChannel.Internal)
            throw new ServerRpcException("服务发现响应通道不合法。", 502, "invalid_response_channel");
        if (response.ErrorCode != 0)
            throw new ServerRpcException("服务发现查询失败。", response.ErrorCode, response.ErrorMessage);
        if (!string.Equals(response.Type, ServerQueryResponse.Descriptor.FullName, StringComparison.Ordinal))
            throw new ServerRpcException("服务发现响应类型不合法。", 502, "response_type_mismatch");

        var result = ServerQueryResponse.Parser.ParseFrom(response.Payload);
        return result.Servers.Select(server => new ServerEndpoint(
            server.ServerId,
            server.ServerType,
            server.Host,
            server.Port,
            string.IsNullOrWhiteSpace(server.TokenAudience) ? server.ServerType : server.TokenAudience,
            server.TlsEnabled,
            server.TlsServerName)).ToArray();
    }
}
