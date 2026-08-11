using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Security.Authentication;

using Core.Configuration;
using Core.Telemetry;

using DotNetGameServer;

using Google.Protobuf;

using Shared;

namespace Core.Rpc;

/// <summary>完成节点解析、服务令牌签发、请求关联、超时、熔断与幂等重试的跨服客户端。</summary>
public sealed class ServerRpcClient : IServerRpcClient
{
    private readonly IServiceResolver _resolver;
    private readonly IServerConnectionPool _connectionPool;
    private readonly ServerRpcOptions _options;
    private readonly ConcurrentDictionary<ServerEndpoint, FailureState> _failures = new();

    public ServerRpcClient(
        IServiceResolver resolver,
        IServerConnectionPool connectionPool,
        ServerRpcOptions options)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<TResponse> CallAsync<TRequest, TResponse>(
        string serverType,
        TRequest request,
        ServerCallOptions? callOptions = null,
        CancellationToken cancellationToken = default)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverType);
        ArgumentNullException.ThrowIfNull(request);
        EnsureConfigured();
        CleanupFailureStates();

        var call = callOptions ?? new ServerCallOptions();
        var configuredAttempts = Math.Clamp(call.MaxAttempts ?? _options.MaxAttempts, 1, 10);
        var attempts = call.Idempotent ? configuredAttempts : 1;
        var resolved = await _resolver.ResolveAsync(
            serverType,
            call.Region ?? _options.DefaultRegion,
            call.Strategy ?? _options.DefaultStrategy,
            call.RoutingKey,
            Math.Clamp(attempts * 2, attempts, 32),
            cancellationToken);
        if (resolved.Count == 0)
            throw new ServerRpcException($"没有可用的目标服务器: {serverType}", 503, "service_unavailable");

        var endpoints = resolved.Distinct().Where(IsEndpointAvailable).ToArray();
        if (endpoints.Length == 0)
            throw new ServerRpcException($"目标服务暂时熔断: {serverType}", 503, "circuit_open");

        var requestId = Guid.NewGuid().ToString("N");
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            var endpoint = endpoints[attempt % endpoints.Length];
            var started = Stopwatch.GetTimestamp();
            try
            {
                var response = await SendAsync<TRequest, TResponse>(
                    endpoint,
                    request,
                    requestId,
                    call,
                    cancellationToken);
                _failures.TryRemove(endpoint, out _);
                CoreTelemetry.RecordServerRpc(endpoint.ServerType, "ok", Stopwatch.GetElapsedTime(started).TotalSeconds);
                return response;
            }
            catch (Exception ex) when (IsRetryable(ex))
            {
                lastFailure = ex;
                RecordFailure(endpoint);
                CoreTelemetry.RecordServerRpc(endpoint.ServerType, "transport_error",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                if (!call.Idempotent) break;
            }
            catch (OperationCanceledException)
            {
                CoreTelemetry.RecordServerRpc(endpoint.ServerType, "cancelled",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                throw;
            }
            catch
            {
                CoreTelemetry.RecordServerRpc(endpoint.ServerType, "remote_error",
                    Stopwatch.GetElapsedTime(started).TotalSeconds);
                throw;
            }
        }

        throw new ServerRpcException(
            $"跨服调用失败: {serverType}",
            503,
            "rpc_transport_failure",
            lastFailure);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        ServerEndpoint endpoint,
        TRequest request,
        string requestId,
        ServerCallOptions callOptions,
        CancellationToken cancellationToken)
        where TRequest : class, IMessage<TRequest>, new()
        where TResponse : class, IMessage<TResponse>, new()
    {
        var audience = string.IsNullOrWhiteSpace(endpoint.TokenAudience)
            ? endpoint.ServerType
            : endpoint.TokenAudience;
        var envelopeRequest = new envelope
        {
            Type = request.Descriptor.FullName,
            Payload = request.ToByteString(),
            RequestId = requestId,
            Channel = CommunicationChannel.Internal,
            Token = TokenSigner.ComposeServiceToken(
                _options.SourceServerId,
                audience,
                _options.TokenSecret,
                Math.Max(1, _options.TokenTtlSeconds))
        };
        var timeout = TimeSpan.FromMilliseconds(Math.Max(100, callOptions.TimeoutMs ?? _options.RequestTimeoutMs));
        var response = await _connectionPool.SendAsync(endpoint, envelopeRequest, timeout, cancellationToken);
        if (response.Channel != CommunicationChannel.Internal)
            throw new ServerRpcException("跨服响应通道不合法。", 502, "invalid_response_channel");
        if (response.ErrorCode != 0)
        {
            throw new ServerRpcException(
                $"目标服务器返回错误: {endpoint.ServerId} code={response.ErrorCode}",
                response.ErrorCode,
                response.ErrorMessage);
        }

        var expectedType = new TResponse().Descriptor.FullName;
        if (!string.Equals(response.Type, expectedType, StringComparison.Ordinal))
            throw new ServerRpcException(
                $"跨服响应类型不匹配，期望 {expectedType}，实际 {response.Type}",
                502,
                "response_type_mismatch");
        try
        {
            return (TResponse)new TResponse().Descriptor.Parser.ParseFrom(response.Payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            throw new ServerRpcException("跨服响应解析失败。", 502, "invalid_response_payload", ex);
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.SourceServerId))
            throw new InvalidOperationException("serverRpc:sourceServerId 不能为空。");
        if (string.IsNullOrWhiteSpace(_options.TokenSecret))
            throw new InvalidOperationException("serverRpc:tokenSecret 或 authentication:tokenSecret 不能为空。");
    }

    private bool IsEndpointAvailable(ServerEndpoint endpoint)
    {
        if (!_failures.TryGetValue(endpoint, out var state)) return true;
        return state.UnavailableUntilUtc <= DateTimeOffset.UtcNow;
    }

    private void RecordFailure(ServerEndpoint endpoint)
    {
        var threshold = Math.Max(1, _options.FailureThreshold);
        var cooldown = TimeSpan.FromSeconds(Math.Max(1, _options.FailureCooldownSeconds));
        _failures.AddOrUpdate(
            endpoint,
            _ => new FailureState(
                1,
                threshold <= 1 ? DateTimeOffset.UtcNow.Add(cooldown) : DateTimeOffset.MinValue,
                DateTimeOffset.UtcNow),
            (_, current) =>
            {
                var failures = current.UnavailableUntilUtc <= DateTimeOffset.UtcNow
                    ? current.Failures + 1
                    : current.Failures;
                return failures >= threshold
                    ? new FailureState(failures, DateTimeOffset.UtcNow.Add(cooldown), DateTimeOffset.UtcNow)
                    : new FailureState(failures, DateTimeOffset.MinValue, DateTimeOffset.UtcNow);
            });
    }

    private void CleanupFailureStates()
    {
        var retention = TimeSpan.FromSeconds(Math.Max(60, _options.FailureCooldownSeconds * 6));
        var cutoff = DateTimeOffset.UtcNow.Subtract(retention);
        foreach (var item in _failures)
        {
            if (item.Value.UpdatedUtc < cutoff)
                _failures.TryRemove(item.Key, out _);
        }
    }

    private static bool IsRetryable(Exception exception) =>
        exception is IOException or SocketException or TimeoutException or AuthenticationException;

    private sealed record FailureState(
        int Failures,
        DateTimeOffset UnavailableUntilUtc,
        DateTimeOffset UpdatedUtc);
}
