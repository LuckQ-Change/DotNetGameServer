using System;
using System.Threading;
using System.Threading.Tasks;
using DotNetGameServer.UnityClient.Discovery;
using DotNetGameServer.UnityClient.Transport;

namespace DotNetGameServer.UnityClient.Samples
{
    /// <summary>
    /// 把 YourNetworkPeer 替换为项目现有框架。框架必须保留每条 Envelope 的消息边界。
    /// 如果底层是字节流，应在适配器内使用 4-byte little-endian 长度前缀。
    /// </summary>
    public sealed class ThirdPartyTransportAdapter : INetworkTransport
    {
        public bool IsConnected { get; private set; }
        public ServiceEndpoint CurrentEndpoint { get; private set; }

        public Task ConnectAsync(ServiceEndpoint endpoint, CancellationToken cancellationToken)
        {
            CurrentEndpoint = endpoint;
            // await peer.Connect(endpoint.Host, endpoint.Port, cancellationToken);
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(byte[] envelopeBytes, CancellationToken cancellationToken)
        {
            // return peer.SendMessageAsync(envelopeBytes, cancellationToken);
            throw new NotImplementedException("Connect this method to the third-party framework.");
        }

        public Task<byte[]> ReceiveAsync(CancellationToken cancellationToken)
        {
            // return peer.ReceiveMessageAsync(cancellationToken);
            throw new NotImplementedException("Connect this method to the third-party framework.");
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = false;
            CurrentEndpoint = null;
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
