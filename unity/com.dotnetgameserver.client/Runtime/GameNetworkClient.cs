using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DotNetGameServer.UnityClient.Discovery;
using DotNetGameServer.UnityClient.Logging;
using DotNetGameServer.UnityClient.Protocol;
using DotNetGameServer.UnityClient.Transport;

using Google.Protobuf;

namespace DotNetGameServer.UnityClient
{
    public sealed class GameNetworkClient : IDisposable
    {
        private readonly INetworkTransport _transport;
        private readonly IServiceDiscoveryProvider _discovery;
        private readonly IEnvelopeCodec _codec;
        private readonly IClientEventDispatcher _eventDispatcher;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<NetworkEnvelope>> _pending =
            new ConcurrentDictionary<string, TaskCompletionSource<NetworkEnvelope>>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, List<Action<NetworkEnvelope>>> _typedHandlers =
            new ConcurrentDictionary<string, List<Action<NetworkEnvelope>>>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _receiveCts;
        private ServiceQuery _lastQuery;
        private bool _disconnectRequested;
        private int _reconnecting;
        private bool _disposed;
        private AccessTokenGrant _accessTokenGrant;
        private readonly SemaphoreSlim _tokenRefreshGate = new SemaphoreSlim(1, 1);
        private int _tokenRefreshDepth;

        public GameNetworkClient(
            INetworkTransport transport,
            IServiceDiscoveryProvider discovery,
            IEnvelopeCodec codec = null,
            IClientEventDispatcher eventDispatcher = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _codec = codec ?? new ProtobufEnvelopeCodec();
            _eventDispatcher = eventDispatcher ?? ImmediateClientEventDispatcher.Instance;
        }

        public bool IsConnected { get { return _transport.IsConnected; } }
        public ServiceEndpoint CurrentEndpoint { get { return _transport.CurrentEndpoint; } }
        public string AccessToken { get; set; } = string.Empty;
        public AccessTokenGrant CurrentAccessToken { get { return _accessTokenGrant; } }
        public Func<CancellationToken, Task<AccessTokenGrant>> TokenRefreshHandler;
        public TimeSpan TokenRefreshLeadTime = TimeSpan.FromSeconds(120);
        public string ClientVersion { get; set; } = string.Empty;
        public bool RequireCompatibleVersion { get; set; }
        public bool AutoHeartbeat { get; set; } = true;
        public bool AutoReconnect { get; set; }
        public int MaxReconnectAttempts { get; set; } = 3;
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(2);
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public event Action<NetworkEnvelope> NotificationReceived;
        public event Action<Exception> ConnectionClosed;
        public event Action Reconnected;

        public Task<ServiceEndpoint> ConnectAsync(ServiceQuery query, CancellationToken cancellationToken)
        {
            _disconnectRequested = false;
            _lastQuery = query;
            return ConnectCoreAsync(query, cancellationToken);
        }

        public async Task<TResponse> RequestAsync<TResponse>(
            IMessage request,
            MessageParser<TResponse> responseParser,
            CancellationToken cancellationToken) where TResponse : class, IMessage<TResponse>
        {
            var result = await RequestResultAsync(request, responseParser, cancellationToken).ConfigureAwait(false);
            if (result.HasResponse) return result.Response;
            throw new GameProtocolException(result.ErrorCode, result.ErrorMessage, string.Empty);
        }

        public async Task<GameRequestResult<TResponse>> RequestResultAsync<TResponse>(
            IMessage request,
            MessageParser<TResponse> responseParser,
            CancellationToken cancellationToken) where TResponse : class, IMessage<TResponse>
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (responseParser == null) throw new ArgumentNullException(nameof(responseParser));
            var envelope = await RequestRawAsync(
                GoogleProtobufSerializer.GetMessageType(request),
                GoogleProtobufSerializer.Serialize(request),
                cancellationToken).ConfigureAwait(false);
            TResponse parsed = null;
            try { parsed = GoogleProtobufSerializer.Deserialize(responseParser, envelope.Payload); }
            catch (InvalidProtocolBufferException) { parsed = null; }

            return new GameRequestResult<TResponse>
            {
                Success = envelope.ErrorCode == 0 && parsed != null,
                ErrorCode = envelope.ErrorCode,
                ErrorMessage = envelope.ErrorMessage ?? string.Empty,
                Response = parsed
            };
        }

        public Task<NetworkEnvelope> RequestRawAsync(
            string messageType,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            return SendRequestAsync(messageType, payload, true, cancellationToken);
        }

        public void ApplyAccessToken(AccessTokenGrant grant)
        {
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            if (string.IsNullOrWhiteSpace(grant.AccessToken))
                throw new ArgumentException("Access token is required.", nameof(grant));
            AccessToken = grant.AccessToken;
            _accessTokenGrant = grant;
        }

        public void ClearAccessToken()
        {
            AccessToken = string.Empty;
            _accessTokenGrant = null;
        }

        public IDisposable On<T>(MessageParser<T> parser, Action<T> handler) where T : class, IMessage<T>, new()
        {
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var typeName = new T().Descriptor.FullName;
            Action<NetworkEnvelope> wrapped = envelope =>
            {
                try { handler(parser.ParseFrom(envelope.Payload)); }
                catch (InvalidProtocolBufferException)
                {
                    NetworkLogHub.Debug("Notification", "Typed push payload could not be parsed.");
                }
            };
            var list = _typedHandlers.GetOrAdd(typeName, _ => new List<Action<NetworkEnvelope>>());
            lock (list) list.Add(wrapped);
            return new Subscription(() =>
            {
                lock (list) list.Remove(wrapped);
            });
        }

        public async Task SendNotificationAsync(string messageType, byte[] payload, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            var envelope = new NetworkEnvelope
            {
                Type = messageType,
                Payload = payload ?? Array.Empty<byte>(),
                Token = AccessToken,
                RequestId = Guid.NewGuid().ToString("N"),
                Version = ClientVersion,
                Channel = Protocol.CommunicationChannel.Client
            };
            await _transport.SendAsync(_codec.Encode(envelope), cancellationToken).ConfigureAwait(false);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            _disconnectRequested = true;
            var cts = _receiveCts;
            _receiveCts = null;
            if (cts != null) cts.Cancel();
            await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            FailPending(new IOException("Client disconnected."));
            if (cts != null) cts.Dispose();
            NetworkLogHub.Information("Connection", "Disconnected.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception exception) { NetworkLogHub.Warning("Connection", "Dispose disconnect failed.", exception: exception); }
            _transport.Dispose();
            _connectGate.Dispose();
            _tokenRefreshGate.Dispose();
        }

        private async Task<NetworkEnvelope> SendRequestAsync(
            string messageType,
            byte[] payload,
            bool allowAuthRetry,
            CancellationToken cancellationToken)
        {
            if (_tokenRefreshDepth == 0)
                await TryRefreshAccessTokenAsync(false, cancellationToken).ConfigureAwait(false);

            var response = await SendOnceAsync(messageType, payload, cancellationToken).ConfigureAwait(false);
            if (allowAuthRetry
                && response.ErrorCode == ProtocolError.AuthenticationRequired
                && TokenRefreshHandler != null
                && _tokenRefreshDepth == 0
                && await TryRefreshAccessTokenAsync(true, cancellationToken).ConfigureAwait(false))
            {
                return await SendOnceAsync(messageType, payload, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }

        private async Task<NetworkEnvelope> SendOnceAsync(
            string messageType,
            byte[] payload,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!_transport.IsConnected) throw new InvalidOperationException("Client is not connected.");
            if (string.IsNullOrWhiteSpace(messageType)) throw new ArgumentException("Message type is required.", nameof(messageType));
            var requestId = Guid.NewGuid().ToString("N");
            var request = new NetworkEnvelope
            {
                Type = messageType,
                Payload = payload ?? Array.Empty<byte>(),
                Token = AccessToken,
                RequestId = requestId,
                Version = ClientVersion,
                Channel = Protocol.CommunicationChannel.Client
            };
            var completion = new TaskCompletionSource<NetworkEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!_pending.TryAdd(requestId, completion)) throw new InvalidOperationException("Duplicate request id.");

            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(RequestTimeout);
                using (timeout.Token.Register(() =>
                {
                    TaskCompletionSource<NetworkEnvelope> removed;
                    if (_pending.TryRemove(requestId, out removed)) removed.TrySetCanceled();
                }))
                {
                    try
                    {
                        NetworkLogHub.Debug("Protocol", "send " + messageType, EndpointText(), requestId);
                        await _transport.SendAsync(_codec.Encode(request), timeout.Token).ConfigureAwait(false);
                        return await completion.Task.ConfigureAwait(false);
                    }
                    finally
                    {
                        TaskCompletionSource<NetworkEnvelope> ignored;
                        _pending.TryRemove(requestId, out ignored);
                    }
                }
            }
        }

        private async Task<bool> TryRefreshAccessTokenAsync(bool force, CancellationToken cancellationToken)
        {
            if (TokenRefreshHandler == null) return false;
            if (!force && !IsAccessTokenExpiringSoon()) return false;

            await _tokenRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!force && !IsAccessTokenExpiringSoon()) return false;
                Interlocked.Increment(ref _tokenRefreshDepth);
                try
                {
                    var grant = await TokenRefreshHandler(cancellationToken).ConfigureAwait(false);
                    if (grant == null || string.IsNullOrWhiteSpace(grant.AccessToken))
                        return false;
                    ApplyAccessToken(grant);
                    return true;
                }
                finally
                {
                    Interlocked.Decrement(ref _tokenRefreshDepth);
                }
            }
            finally
            {
                _tokenRefreshGate.Release();
            }
        }

        private bool IsAccessTokenExpiringSoon()
        {
            var expireUnix = _accessTokenGrant != null ? _accessTokenGrant.ExpireUnix : 0;
            string tokenId;
            string userId;
            long parsedExpire;
            if (expireUnix <= 0 && AccessTokenGrant.TryRead(AccessToken, out tokenId, out userId, out parsedExpire))
                expireUnix = parsedExpire;
            if (expireUnix <= 0) return false;

            var lead = _accessTokenGrant != null && _accessTokenGrant.RecommendedRefreshSeconds > 0
                ? _accessTokenGrant.RecommendedRefreshSeconds
                : Math.Max(1, (int)TokenRefreshLeadTime.TotalSeconds);
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expireUnix - lead;
        }

        private async Task<ServiceEndpoint> ConnectCoreAsync(ServiceQuery query, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_transport.IsConnected) return _transport.CurrentEndpoint;
                var endpoints = await _discovery.ResolveAsync(query ?? new ServiceQuery(), cancellationToken).ConfigureAwait(false);
                var ordered = ServiceEndpointSelector.OrderForConnection(endpoints, query == null ? string.Empty : query.RoutingKey);
                if (ordered.Count == 0) throw new InvalidOperationException("Service discovery returned no endpoint.");

                Exception last = null;
                foreach (var endpoint in ordered)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (RequireCompatibleVersion
                        && !ProtocolCompatibility.AreCompatible(ClientVersion, endpoint.Version))
                    {
                        last = new GameProtocolException(
                            ProtocolError.VersionMismatch,
                            "Protocol version mismatch. Client " + ClientVersion + ", server " + endpoint.Version,
                            string.Empty);
                        NetworkLogHub.Warning("Connection", last.Message, endpoint.ToString());
                        continue;
                    }

                    try
                    {
                        NetworkLogHub.Information("Connection", "Connecting to service.", endpoint.ToString());
                        await _transport.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                        StartReceiveLoop();
                        StartHeartbeat(endpoint);
                        NetworkLogHub.Information("Connection", "Connected.", endpoint.ToString());
                        return endpoint;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception exception)
                    {
                        last = exception;
                        NetworkLogHub.Warning("Connection", "Endpoint connection failed.", endpoint.ToString(), exception: exception);
                    }
                }
                throw new InvalidOperationException("All discovered endpoints failed.", last);
            }
            finally
            {
                _connectGate.Release();
            }
        }

        private void StartReceiveLoop()
        {
            var old = _receiveCts;
            if (old != null) old.Cancel();
            _receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCts.Token);
        }

        private void StartHeartbeat(ServiceEndpoint endpoint)
        {
            if (!AutoHeartbeat || endpoint == null || endpoint.HeartbeatIntervalSeconds <= 0 || _receiveCts == null)
                return;
            _ = HeartbeatLoopAsync(endpoint.HeartbeatIntervalSeconds, _receiveCts.Token);
        }

        private async Task HeartbeatLoopAsync(int intervalSeconds, CancellationToken cancellationToken)
        {
            var delay = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    var response = await RequestRawAsync(
                        ClientHeartbeatCodec.RequestType,
                        ClientHeartbeatCodec.EncodeRequest(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                        cancellationToken).ConfigureAwait(false);
                    if (response.ErrorCode == ProtocolError.UnknownMessageType)
                        return;
                    var recommended = ClientHeartbeatCodec.ReadRecommendedIntervalSeconds(response.Payload);
                    if (recommended > 0) delay = TimeSpan.FromSeconds(recommended);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                NetworkLogHub.Debug("Heartbeat", "Heartbeat canceled.");
            }
            catch (Exception exception)
            {
                NetworkLogHub.Warning("Heartbeat", "Heartbeat failed.", exception: exception);
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            Exception failure = null;
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var bytes = await _transport.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                    var envelope = _codec.Decode(bytes);
                    NetworkLogHub.Debug("Protocol", "recv " + envelope.Type, EndpointText(), envelope.RequestId);
                    TaskCompletionSource<NetworkEnvelope> completion;
                    if (!string.IsNullOrEmpty(envelope.RequestId) && _pending.TryRemove(envelope.RequestId, out completion))
                        completion.TrySetResult(envelope);
                    else
                        SafeNotify(envelope);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                NetworkLogHub.Debug("Connection", "Receive loop canceled.", EndpointText());
            }
            catch (Exception exception)
            {
                failure = exception;
                NetworkLogHub.Error("Connection", "Receive loop stopped unexpectedly.", EndpointText(), exception: exception);
            }
            finally
            {
                if (failure != null)
                {
                    FailPending(failure);
                    try { await _transport.DisconnectAsync(CancellationToken.None).ConfigureAwait(false); }
                    catch (Exception exception)
                    {
                        NetworkLogHub.Warning("Connection", "Cleanup disconnect failed.", exception: exception);
                    }
                    SafeConnectionClosed(failure);
                    _ = TryReconnectAsync();
                }
            }
        }

        private async Task TryReconnectAsync()
        {
            if (_disconnectRequested || !AutoReconnect || _lastQuery == null || _disposed) return;
            if (Interlocked.CompareExchange(ref _reconnecting, 1, 0) != 0) return;
            try
            {
                for (var attempt = 0; attempt < Math.Max(1, MaxReconnectAttempts); attempt++)
                {
                    if (_disconnectRequested || _disposed) return;
                    await Task.Delay(ReconnectDelay).ConfigureAwait(false);
                    try
                    {
                        await ConnectCoreAsync(_lastQuery, CancellationToken.None).ConfigureAwait(false);
                        var handler = Reconnected;
                        if (handler != null) DispatchEvent(() => handler());
                        return;
                    }
                    catch (Exception exception)
                    {
                        NetworkLogHub.Warning("Connection", "Reconnect attempt failed.", exception: exception);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _reconnecting, 0);
            }
        }

        private void SafeNotify(NetworkEnvelope envelope)
        {
            List<Action<NetworkEnvelope>> typed;
            if (_typedHandlers.TryGetValue(envelope.Type, out typed))
            {
                Action<NetworkEnvelope>[] typedSnapshot;
                lock (typed) typedSnapshot = typed.ToArray();
                DispatchEvent(() =>
                {
                    for (var i = 0; i < typedSnapshot.Length; i++)
                    {
                        try { typedSnapshot[i](envelope); }
                        catch (Exception exception) { NetworkLogHub.Warning("Notification", "Typed handler failed.", exception: exception); }
                    }
                });
            }

            var handler = NotificationReceived;
            if (handler == null) return;
            var subscribers = handler.GetInvocationList().Cast<Action<NetworkEnvelope>>().ToArray();
            DispatchEvent(() =>
            {
                foreach (var subscriber in subscribers)
                {
                    try { subscriber(envelope); }
                    catch (Exception exception) { NetworkLogHub.Warning("Notification", "Notification handler failed.", exception: exception); }
                }
            });
        }

        private void SafeConnectionClosed(Exception failure)
        {
            var handler = ConnectionClosed;
            if (handler == null) return;
            var subscribers = handler.GetInvocationList().Cast<Action<Exception>>().ToArray();
            DispatchEvent(() =>
            {
                foreach (var subscriber in subscribers)
                {
                    try { subscriber(failure); }
                    catch (Exception exception) { NetworkLogHub.Warning("Connection", "ConnectionClosed handler failed.", exception: exception); }
                }
            });
        }

        private void DispatchEvent(Action callback)
        {
            try { _eventDispatcher.Post(callback); }
            catch (Exception exception) { NetworkLogHub.Warning("Events", "Client event dispatch failed.", exception: exception); }
        }

        private void FailPending(Exception exception)
        {
            foreach (var item in _pending.ToArray())
            {
                TaskCompletionSource<NetworkEnvelope> completion;
                if (_pending.TryRemove(item.Key, out completion)) completion.TrySetException(exception);
            }
        }

        private string EndpointText()
        {
            var endpoint = _transport.CurrentEndpoint;
            return endpoint == null ? string.Empty : endpoint.ToString();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GameNetworkClient));
        }

        private sealed class Subscription : IDisposable
        {
            private readonly Action _dispose;
            public Subscription(Action dispose) { _dispose = dispose; }
            public void Dispose() { _dispose(); }
        }
    }
}
