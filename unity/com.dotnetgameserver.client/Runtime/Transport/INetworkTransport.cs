using System;
using System.Threading;
using System.Threading.Tasks;

using DotNetGameServer.UnityClient.Discovery;

namespace DotNetGameServer.UnityClient.Transport
{
    /// <summary>
    /// 可插拔的消息传输层。每次 Send/Receive 对应一条完整 protobuf Envelope，
    /// 第三方框架适配器可自行决定底层使用 TCP、WebSocket、KCP 或框架 packet。
    /// </summary>
    public interface INetworkTransport : IDisposable
    {
        bool IsConnected { get; }
        ServiceEndpoint CurrentEndpoint { get; }
        Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken);
        Task SendAsync(byte[] envelopeBytes, CancellationToken cancellationToken);
        Task<byte[]> ReceiveAsync(CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
