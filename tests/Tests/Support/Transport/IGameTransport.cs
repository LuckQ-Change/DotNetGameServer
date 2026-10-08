using Client.Discovery;

namespace Client.Transport;

public interface IGameTransport : IAsyncDisposable
{
    bool IsConnected { get; }
    ServiceEndpoint? CurrentEndpoint { get; }
    Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken);
    Task SendAsync(ReadOnlyMemory<byte> envelopeBytes, CancellationToken cancellationToken);
    Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
    Task DisconnectAsync(CancellationToken cancellationToken);
}
