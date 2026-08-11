using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, TaskCompletionSource<NetworkEnvelope>> _pending =
            new ConcurrentDictionary<string, TaskCompletionSource<NetworkEnvelope>>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _connectGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _receiveCts;
        private bool _disposed;

        public GameNetworkClient(
            INetworkTransport transport,
            IServiceDiscoveryProvider discovery,
            IEnvelopeCodec codec = null)
        {
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
            _codec = codec ?? new ProtobufEnvelopeCodec();
        }

        public bool IsConnected { get { return _transport.IsConnected; } }
        public ServiceEndpoint CurrentEndpoint { get { return _transport.CurrentEndpoint; } }
        public string AccessToken { get; set; } = string.Empty;
        public string ClientVersion { get; set; } = string.Empty;
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public event Action<NetworkEnvelope> NotificationReceived;
        public event Action<Exception> ConnectionClosed;

        public async Task<ServiceEndpoint> ConnectAsync(ServiceQuery query, CancellationToken cancellationToken)
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
                    try
                    {
                        NetworkLogHub.Information("Connection", "Connecting to service.", endpoint.ToString());
                        await _transport.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                        StartReceiveLoop();
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

        public async Task<TResponse> RequestAsync<TResponse>(
            IMessage request,
            MessageParser<TResponse> responseParser,
            CancellationToken cancellationToken) where TResponse : IMessage<TResponse>
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var envelope = await RequestRawAsync(
                GoogleProtobufSerializer.GetMessageType(request),
                GoogleProtobufSerializer.Serialize(request),
                cancellationToken).ConfigureAwait(false);
            return GoogleProtobufSerializer.Deserialize(responseParser, envelope.Payload);
        }

        public async Task<NetworkEnvelope> RequestRawAsync(
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
                        var response = await completion.Task.ConfigureAwait(false);
                        if (response.ErrorCode != 0)
                            throw new GameProtocolException(response.ErrorCode, response.ErrorMessage, requestId);
                        return response;
                    }
                    finally
                    {
                        TaskCompletionSource<NetworkEnvelope> ignored;
                        _pending.TryRemove(requestId, out ignored);
                    }
                }
            }
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
            var cts = _receiveCts;
            _receiveCts = null;
            if (cts != null) cts.Cancel();
            await _transport.DisconnectAsync(cancellationToken).ConfigureAwait(false);
            FailPending(new IOException("Client disconnected."));
            cts?.Dispose();
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
        }

        private void StartReceiveLoop()
        {
            var old = _receiveCts;
            if (old != null) old.Cancel();
            _receiveCts = new CancellationTokenSource();
            _ = ReceiveLoopAsync(_receiveCts.Token);
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
                    var handler = ConnectionClosed;
                    if (handler != null) handler(failure);
                }
            }
        }

        private void SafeNotify(NetworkEnvelope envelope)
        {
            var handler = NotificationReceived;
            if (handler == null) return;
            foreach (var subscriber in handler.GetInvocationList().Cast<Action<NetworkEnvelope>>())
            {
                try { subscriber(envelope); }
                catch (Exception exception) { NetworkLogHub.Warning("Notification", "Notification handler failed.", exception: exception); }
            }
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
    }
}
