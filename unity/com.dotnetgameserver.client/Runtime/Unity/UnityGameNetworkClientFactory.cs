using System;
using System.Threading;
using DotNetGameServer.UnityClient.Discovery;
using DotNetGameServer.UnityClient.Transport;

namespace DotNetGameServer.UnityClient.Unity
{
    public static class UnityGameNetworkClientFactory
    {
        public static GameNetworkClient CreateDefault()
        {
            var settings = DotNetGameServerSettings.LoadOrTransientDefault();
            var unityContext = SynchronizationContext.Current;
            if (unityContext == null)
                throw new InvalidOperationException(
                    "CreateDefault must be called on the Unity main thread so client events can be dispatched safely.");
            var discovery = new UnityHttpServiceDiscoveryProvider(settings.BootstrapUrls);
            var transport = new TcpNetworkTransport(settings.MaxFrameBytes);
            var eventDispatcher = new SynchronizationContextClientEventDispatcher(unityContext);
            return new GameNetworkClient(transport, discovery, eventDispatcher: eventDispatcher)
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
