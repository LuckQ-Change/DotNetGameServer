using System;
using DotNetGameServer.UnityClient.Discovery;
using DotNetGameServer.UnityClient.Transport;

namespace DotNetGameServer.UnityClient.Unity
{
    public static class UnityGameNetworkClientFactory
    {
        public static GameNetworkClient CreateDefault()
        {
            var settings = DotNetGameServerSettings.LoadOrTransientDefault();
            var discovery = new UnityHttpServiceDiscoveryProvider(settings.BootstrapUrls);
            var transport = new TcpNetworkTransport(settings.MaxFrameBytes);
            return new GameNetworkClient(transport, discovery)
            {
                ClientVersion = settings.ClientVersion,
                RequestTimeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds))
            };
        }

        public static ServiceQuery CreateDefaultQuery(string routingKey = null)
        {
            var settings = DotNetGameServerSettings.LoadOrTransientDefault();
            return new ServiceQuery
            {
                Responsibility = settings.Responsibility,
                Region = settings.Region,
                RoutingKey = routingKey ?? settings.RoutingKey
            };
        }
    }
}
