using System.Buffers.Binary;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;

using Client.Discovery;

namespace Client.Transport;

public sealed class TcpGameTransport : IGameTransport
{
    private readonly int _maxFrameBytes;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public TcpGameTransport(int maxFrameBytes = 1024 * 1024)
    {
        _maxFrameBytes = maxFrameBytes;
    }
    private TcpClient? _client;
    private Stream? _stream;

    public bool IsConnected => _client is { Connected: true } && _stream is not null;
    public ServiceEndpoint? CurrentEndpoint { get; private set; }

    public async Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        await DisconnectAsync(CancellationToken.None);
        var client = new TcpClient { NoDelay = true };
        using var register = cancellationToken.Register(client.Close);
        try
        {
            await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
            Stream stream = client.GetStream();
            if (endpoint.TlsEnabled)
            {
                var ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                var target = string.IsNullOrWhiteSpace(endpoint.TlsServerName) ? endpoint.Host : endpoint.TlsServerName;
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = target,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13
                }, cancellationToken);
                stream = ssl;
            }

            _client = client;
            _stream = stream;
            CurrentEndpoint = endpoint;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> envelopeBytes, CancellationToken cancellationToken)
    {
        if (envelopeBytes.Length is <= 0 || envelopeBytes.Length > _maxFrameBytes)
            throw new InvalidDataException("帧长度不合法。");
        var stream = _stream ?? throw new InvalidOperationException("尚未连接。");
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var length = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(length, envelopeBytes.Length);
            await stream.WriteAsync(length, cancellationToken);
            await stream.WriteAsync(envelopeBytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
    {
        var stream = _stream ?? throw new InvalidOperationException("尚未连接。");
        var lengthBytes = new byte[4];
        await ReadExactAsync(stream, lengthBytes, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
        if (length <= 0 || length > _maxFrameBytes)
            throw new InvalidDataException($"远端帧长度不合法: {length}");
        var payload = new byte[length];
        await ReadExactAsync(stream, payload, cancellationToken);
        return payload;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var stream = _stream;
        var client = _client;
        _stream = null;
        _client = null;
        CurrentEndpoint = null;
        stream?.Dispose();
        client?.Dispose();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync(CancellationToken.None);
        _writeGate.Dispose();
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken);
            if (read == 0) throw new EndOfStreamException("对端关闭了连接。");
            offset += read;
        }
    }
}
