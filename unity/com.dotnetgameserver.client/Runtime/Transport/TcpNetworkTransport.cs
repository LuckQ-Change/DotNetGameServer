using System;
using System.Buffers.Binary;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

using DotNetGameServer.UnityClient.Discovery;

namespace DotNetGameServer.UnityClient.Transport
{
    public sealed class TcpNetworkTransport : INetworkTransport
    {
        private readonly int _maxFrameBytes;
        private readonly RemoteCertificateValidationCallback _certificateValidation;
        private TcpClient _client;
        private Stream _stream;
        private readonly SemaphoreSlim _writeGate = new SemaphoreSlim(1, 1);

        public TcpNetworkTransport(
            int maxFrameBytes = 1024 * 1024,
            RemoteCertificateValidationCallback certificateValidation = null)
        {
            if (maxFrameBytes < 1024) throw new ArgumentOutOfRangeException(nameof(maxFrameBytes));
            _maxFrameBytes = maxFrameBytes;
            _certificateValidation = certificateValidation;
        }

        public bool IsConnected { get { return _client != null && _client.Connected && _stream != null; } }
        public ServiceEndpoint CurrentEndpoint { get; private set; }

        public async Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken)
        {
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            if (string.IsNullOrWhiteSpace(endpoint.Host) || endpoint.Port <= 0 || endpoint.Port > 65535)
                throw new ArgumentException("Service endpoint is invalid.", nameof(endpoint));

            await DisconnectAsync(CancellationToken.None).ConfigureAwait(false);
            var client = new TcpClient { NoDelay = true };
            using (cancellationToken.Register(client.Close))
            {
                try
                {
                    await client.ConnectAsync(endpoint.Host, endpoint.Port).ConfigureAwait(false);
                    Stream stream = client.GetStream();
                    if (endpoint.TlsEnabled)
                    {
                        var ssl = _certificateValidation == null
                            ? new SslStream(stream, false)
                            : new SslStream(stream, false, _certificateValidation);
                        var target = string.IsNullOrWhiteSpace(endpoint.TlsServerName)
                            ? endpoint.Host
                            : endpoint.TlsServerName;
                        await ssl.AuthenticateAsClientAsync(
                            target,
                            null,
                            SslProtocols.Tls12,
                            true).ConfigureAwait(false);
                        stream = ssl;
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    _client = client;
                    _stream = stream;
                    CurrentEndpoint = endpoint;
                }
                catch
                {
                    client.Close();
                    throw;
                }
            }
        }

        public async Task SendAsync(byte[] envelopeBytes, CancellationToken cancellationToken)
        {
            if (envelopeBytes == null) throw new ArgumentNullException(nameof(envelopeBytes));
            if (envelopeBytes.Length == 0 || envelopeBytes.Length > _maxFrameBytes)
                throw new InvalidDataException("Frame size is invalid.");
            var stream = _stream ?? throw new InvalidOperationException("Transport is not connected.");

            await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var length = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(length, envelopeBytes.Length);
                await WriteAllAsync(stream, length, cancellationToken).ConfigureAwait(false);
                await WriteAllAsync(stream, envelopeBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            var stream = _stream ?? throw new InvalidOperationException("Transport is not connected.");
            var lengthBytes = new byte[4];
            await ReadAllAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
            if (length <= 0 || length > _maxFrameBytes)
                throw new InvalidDataException("Remote frame size is invalid: " + length);
            var payload = new byte[length];
            await ReadAllAsync(stream, payload, cancellationToken).ConfigureAwait(false);
            return payload;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            var stream = _stream;
            var client = _client;
            _stream = null;
            _client = null;
            CurrentEndpoint = null;
            try { if (stream != null) stream.Dispose(); }
            finally { if (client != null) client.Close(); }
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            DisconnectAsync(CancellationToken.None).GetAwaiter().GetResult();
            _writeGate.Dispose();
        }

        private static async Task ReadAllAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset, token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException("Remote endpoint closed the connection.");
                offset += read;
            }
        }

        private static async Task WriteAllAsync(Stream stream, byte[] buffer, CancellationToken token)
        {
            // Stream.WriteAsync must consume the requested range, but the loop keeps adapters honest
            // when this implementation is copied to a partial-write third-party transport.
            await stream.WriteAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
        }
    }
}
