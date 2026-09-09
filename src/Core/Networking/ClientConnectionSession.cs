using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Channels;

using Core.Configuration;
using Core.MessagePipeline;

using DotNetGameServer;

using Google.Protobuf;

using Microsoft.Extensions.Logging;

namespace Core.Networking;

/// <summary>
/// 处理一条 TCP 连接从建立、TLS 握手、收包、业务分发到关闭的完整生命周期。
/// </summary>
/// <remarks>
/// 一个实例只服务一条连接，不负责全局连接计数和监听器生命周期；
/// 这些跨连接职责由 <see cref="TcpServerTransport"/> 管理。
/// </remarks>
internal sealed class ClientConnectionSession
{
    private readonly BaseGameServer _server;
    private readonly ServerRuntimeOptions _options;
    private readonly NetworkAccessGuard _accessGuard;
    private readonly MessageDelegate _pipeline;
    private readonly X509Certificate2? _serverCertificate;

    public ClientConnectionSession(
        BaseGameServer server,
        ServerRuntimeOptions options,
        NetworkAccessGuard accessGuard,
        MessageDelegate pipeline,
        X509Certificate2? serverCertificate)
    {
        _server = server;
        _options = options;
        _accessGuard = accessGuard;
        _pipeline = pipeline;
        _serverCertificate = serverCertificate;
    }

    public async Task RunAsync(TcpClient client, CancellationToken serverCancellationToken)
    {
        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(serverCancellationToken);
        var connectionToken = connectionCts.Token;
        var remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "未知";
        var remoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? remoteEndpoint;
        _server.Log($"客户端连接: {remoteEndpoint}", LogLevel.Debug);

        using (client)
        {
            Stream stream = client.GetStream();
            var authenticatedStream = await AuthenticateTransportAsync(stream, remoteEndpoint, connectionToken);
            if (authenticatedStream is null) return;
            stream = authenticatedStream;

            ConfigureSocket(client);
            var rateLimiter = _accessGuard.CreateConnectionLimiter(remoteEndpoint, remoteIp);
            var requestChannel = CreateRequestChannel();
            using var responseWriteGate = new SemaphoreSlim(1, 1);

            // 多个处理任务允许同一连接并行业务计算；响应写锁保证帧不会交叉写入。
            var requestProcessors = Enumerable
                .Range(0, _options.MaxConcurrentRequestsPerConnection)
                .Select(_ => Task.Run(
                    () => ProcessRequestsAsync(
                        requestChannel.Reader,
                        stream,
                        responseWriteGate,
                        remoteEndpoint,
                        connectionToken),
                    connectionToken))
                .ToArray();

            await ReceiveRequestsAsync(
                stream,
                requestChannel.Writer,
                rateLimiter,
                remoteEndpoint,
                connectionToken);

            requestChannel.Writer.TryComplete();
            await CancelRequestProcessorsAsync(connectionCts, requestProcessors, remoteEndpoint, connectionToken);
            _server.Log($"客户端断开: {remoteEndpoint}", LogLevel.Debug);
        }
    }

    /// <summary>
    /// 在读取任何业务字节前完成 TLS 握手；未启用 TLS 时直接返回原始网络流。
    /// </summary>
    private async Task<Stream?> AuthenticateTransportAsync(
        Stream stream,
        string remoteEndpoint,
        CancellationToken cancellationToken)
    {
        if (!_options.Tls.Enabled || _serverCertificate is null) return stream;

        var secureStream = new SslStream(stream, leaveInnerStreamOpen: false);
        try
        {
            using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeCts.CancelAfter(_options.Tls.HandshakeTimeoutMs);
            await secureStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _serverCertificate,
                ClientCertificateRequired = _options.Tls.RequireClientCertificate,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = _options.Tls.CheckClientCertificateRevocation
                    ? X509RevocationMode.Online
                    : X509RevocationMode.NoCheck
            }, handshakeCts.Token);
            _server.Log($"TLS 握手成功: {remoteEndpoint} 协议={secureStream.SslProtocol}", LogLevel.Debug);
            return secureStream;
        }
        catch (Exception exception)
        {
            _server.Log($"TLS 握手失败，断开: {remoteEndpoint} {exception.Message}", LogLevel.Warning);
            await secureStream.DisposeAsync();
            return null;
        }
    }

    /// <summary>设置尽力而为的 TCP 保活和同步套接字超时。</summary>
    private void ConfigureSocket(TcpClient client)
    {
        try
        {
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            }
            catch
            {
                // 某些平台不支持调整 KeepAlive；应用层空闲超时仍会生效。
            }

            client.ReceiveTimeout = _options.IdleTimeoutMs;
            client.SendTimeout = _options.ReceiveTimeoutMs;
        }
        catch (Exception exception)
        {
            _server.Log($"设置接收/发送超时失败: {exception.Message}");
        }
    }

    private Channel<envelope> CreateRequestChannel() =>
        Channel.CreateBounded<envelope>(new BoundedChannelOptions(_options.ConnectionQueueCapacity)
        {
            AllowSynchronousContinuations = false,
            SingleReader = _options.MaxConcurrentRequestsPerConnection == 1,
            SingleWriter = true,
            // 队列满时暂停读取 TCP，利用背压限制单连接内存占用。
            FullMode = BoundedChannelFullMode.Wait
        });

    /// <summary>持续读取完整信封，并把合法帧送入有界业务队列。</summary>
    private async Task ReceiveRequestsAsync(
        Stream stream,
        ChannelWriter<envelope> requestWriter,
        NetworkAccessGuard.ConnectionRateLimiter rateLimiter,
        string remoteEndpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var lengthBuffer = new byte[sizeof(int)];
                var lengthBytes = await ServerEnvelopeTransport.ReadExactlyAsync(
                    stream,
                    lengthBuffer,
                    cancellationToken,
                    _options.IdleTimeoutMs);
                if (lengthBytes == 0) break;
                if (!rateLimiter.TryAddBytes(lengthBytes)) break;

                var frameLength = BinaryPrimitives.ReadInt32LittleEndian(lengthBuffer);
                if (frameLength <= 0 || frameLength > _options.MaxMessageSizeBytes)
                {
                    _server.Log($"非法或过大消息长度: {frameLength}，断开连接");
                    break;
                }

                var payload = new byte[frameLength];
                var payloadBytes = await ServerEnvelopeTransport.ReadExactlyAsync(
                    stream,
                    payload,
                    cancellationToken,
                    _options.ReceiveTimeoutMs);
                if (payloadBytes != frameLength)
                {
                    _server.Log($"消息体未完整读取: expected={frameLength} actual={payloadBytes}，断开连接");
                    break;
                }
                if (!rateLimiter.TryAddBytes(frameLength)) break;

                envelope request;
                try
                {
                    request = envelope.Parser.ParseFrom(payload);
                }
                catch (InvalidProtocolBufferException exception)
                {
                    _server.Log($"协议解析异常(envelope): {exception.Message}，断开");
                    break;
                }

                if (!rateLimiter.TryAddMessage()) break;
                await requestWriter.WriteAsync(request, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _server.Log($"客户端请求被取消: {remoteEndpoint}", LogLevel.Debug);
        }
        catch (IOException exception)
        {
            _server.Log($"客户端网络IO断开: {remoteEndpoint} {exception.Message}", LogLevel.Debug);
        }
        catch (SocketException exception)
        {
            _server.Log($"客户端套接字断开: {remoteEndpoint} {exception.Message}", LogLevel.Debug);
        }
        catch (Exception exception)
        {
            _server.Log($"客户端异常: {exception}", LogLevel.Error);
        }
    }

    /// <summary>消费一条连接上的业务请求；同一方法可由多个任务并行运行。</summary>
    private async Task ProcessRequestsAsync(
        ChannelReader<envelope> requestReader,
        Stream stream,
        SemaphoreSlim responseWriteGate,
        string remoteEndpoint,
        CancellationToken connectionToken)
    {
        await foreach (var request in requestReader.ReadAllAsync(connectionToken))
        {
            try
            {
                await ProcessRequestAsync(
                    request,
                    stream,
                    responseWriteGate,
                    remoteEndpoint,
                    connectionToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                _server.Log($"请求处理异常: {exception}", LogLevel.Error);
            }
        }
    }

    private async Task ProcessRequestAsync(
        envelope request,
        Stream stream,
        SemaphoreSlim responseWriteGate,
        string remoteEndpoint,
        CancellationToken connectionToken)
    {
        var requestId = string.IsNullOrWhiteSpace(request.RequestId)
            ? Guid.NewGuid().ToString("N")
            : request.RequestId;
        _server.Log($"recv route={request.Type} req_id={requestId} ip={remoteEndpoint}", LogLevel.Debug);

        if (!TryValidateMetadata(request, out var metadataError))
        {
            await WriteResponseAsync(
                stream,
                responseWriteGate,
                string.IsNullOrWhiteSpace(request.Type)
                    ? "unknown"
                    : request.Type[..Math.Min(request.Type.Length, _options.MaxMessageTypeLength)],
                [],
                request.RequestId[..Math.Min(request.RequestId.Length, _options.MaxRequestIdLength)],
                ProtocolError.InvalidRequest,
                metadataError,
                request.Channel,
                connectionToken);
            return;
        }

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(connectionToken);
        if (_options.RequestTimeoutMs > 0) requestCts.CancelAfter(_options.RequestTimeoutMs);
        var context = new MessageContext
        {
            Envelope = request,
            Server = _server,
            RemoteAddress = remoteEndpoint,
            RequestId = requestId,
            Token = requestCts.Token
        };

        try
        {
            await _pipeline(context).WaitAsync(requestCts.Token);
        }
        catch (OperationCanceledException)
            when (!connectionToken.IsCancellationRequested && requestCts.IsCancellationRequested)
        {
            context.ResponseType = string.IsNullOrWhiteSpace(request.Type) ? "unknown" : request.Type;
            context.ResponsePayload = [];
            context.ResponseErrorCode = ProtocolError.RequestTimeout;
            context.ResponseErrorMessage = "request_timeout";
        }

        if (!context.HasResponse) return;
        if ((context.ResponsePayload?.Length ?? 0) > _options.MaxMessageSizeBytes)
        {
            context.ResponsePayload = [];
            context.ResponseErrorCode = ProtocolError.ResponseTooLarge;
            context.ResponseErrorMessage = "response_too_large";
        }

        await WriteResponseAsync(
            stream,
            responseWriteGate,
            context.ResponseType!,
            context.ResponsePayload ?? [],
            requestId,
            context.ResponseErrorCode,
            context.ResponseErrorMessage,
            context.EffectiveChannel,
            connectionToken);
        _server.Log(
            $"send route={context.ResponseType} req_id={requestId} ip={remoteEndpoint} error_code={context.ResponseErrorCode}",
            LogLevel.Debug);
    }

    /// <summary>串行化响应写入，防止并行处理器产生交叉帧。</summary>
    private async Task WriteResponseAsync(
        Stream stream,
        SemaphoreSlim responseWriteGate,
        string type,
        byte[] payload,
        string? requestId,
        int errorCode,
        string? errorMessage,
        CommunicationChannel channel,
        CancellationToken cancellationToken)
    {
        await responseWriteGate.WaitAsync(cancellationToken);
        try
        {
            await ServerEnvelopeTransport.WriteResponseAsync(
                stream,
                type,
                payload,
                requestId,
                errorCode,
                errorMessage,
                channel,
                cancellationToken,
                _options.ReceiveTimeoutMs,
                _options.MaxMessageSizeBytes);
        }
        finally
        {
            responseWriteGate.Release();
        }
    }

    private bool TryValidateMetadata(envelope message, out string error)
    {
        if (string.IsNullOrWhiteSpace(message.Type) || message.Type.Length > _options.MaxMessageTypeLength)
            error = "invalid_message_type";
        else if (message.RequestId.Length > _options.MaxRequestIdLength)
            error = "invalid_request_id";
        else if (message.Token.Length > _options.MaxTokenLength)
            error = "invalid_token_length";
        else if (message.Version.Length > _options.MaxVersionLength)
            error = "invalid_version_length";
        else
        {
            error = string.Empty;
            return true;
        }

        return false;
    }

    private async Task CancelRequestProcessorsAsync(
        CancellationTokenSource connectionCts,
        Task[] requestProcessors,
        string remoteEndpoint,
        CancellationToken connectionToken)
    {
        try
        {
            await connectionCts.CancelAsync()
                .WaitAsync(TimeSpan.FromMilliseconds(_options.ShutdownTimeoutMs));
        }
        catch (TimeoutException)
        {
            _server.Log($"取消连接请求处理超时: {remoteEndpoint}", LogLevel.Warning);
        }

        try
        {
            await Task.WhenAll(requestProcessors);
        }
        catch (OperationCanceledException) when (connectionToken.IsCancellationRequested)
        {
            // 连接关闭或服务器停止时的正常取消。
        }
        catch (Exception exception)
        {
            _server.Log($"请求处理器异常: {exception.Message}");
        }
    }
}
