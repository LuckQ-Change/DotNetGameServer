using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetGameServer.UnityClient.Discovery
{
    public sealed class StaticServiceDiscoveryProvider : IServiceDiscoveryProvider
    {
        private readonly IReadOnlyList<ServiceEndpoint> _endpoints;

        public StaticServiceDiscoveryProvider(IEnumerable<ServiceEndpoint> endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
            _endpoints = endpoints.ToArray();
        }

        public Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(ServiceQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            query = query ?? new ServiceQuery();
            var result = _endpoints
                .Where(e => e != null && e.Port > 0 && e.Port <= 65535)
                .Where(e => e.HasResponsibility(query.Responsibility))
                .Where(e => string.IsNullOrWhiteSpace(query.Region)
                    || string.Equals(e.Region, query.Region, StringComparison.OrdinalIgnoreCase))
                .Take(Math.Max(1, query.Limit))
                .ToArray();
            return Task.FromResult<IReadOnlyList<ServiceEndpoint>>(result);
        }
    }

    /// <summary>把现有 Consul、PlayFab、云函数或项目配置中心包装成发现 Provider。</summary>
    public sealed class DelegateServiceDiscoveryProvider : IServiceDiscoveryProvider
    {
        private readonly Func<ServiceQuery, CancellationToken, Task<IReadOnlyList<ServiceEndpoint>>> _resolver;

        public DelegateServiceDiscoveryProvider(
            Func<ServiceQuery, CancellationToken, Task<IReadOnlyList<ServiceEndpoint>>> resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(ServiceQuery query, CancellationToken cancellationToken)
        {
            return _resolver(query ?? new ServiceQuery(), cancellationToken);
        }
    }

    public sealed class CompositeServiceDiscoveryProvider : IServiceDiscoveryProvider
    {
        private readonly IServiceDiscoveryProvider[] _providers;

        public CompositeServiceDiscoveryProvider(params IServiceDiscoveryProvider[] providers)
        {
            _providers = providers == null
                ? Array.Empty<IServiceDiscoveryProvider>()
                : providers.Where(p => p != null).ToArray();
        }

        public async Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(
            ServiceQuery query,
            CancellationToken cancellationToken)
        {
            var unique = new Dictionary<string, ServiceEndpoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var provider in _providers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<ServiceEndpoint> endpoints;
                try
                {
                    endpoints = await provider.ResolveAsync(query, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    Logging.NetworkLogHub.Warning("Discovery", "Discovery provider failed.", exception: exception);
                    continue;
                }

                foreach (var endpoint in endpoints)
                {
                    if (endpoint == null) continue;
                    var key = endpoint.Host + ":" + endpoint.Port;
                    if (!unique.ContainsKey(key)) unique.Add(key, endpoint);
                }
            }

            return unique.Values
                .OrderBy(e => e.Load)
                .Take(Math.Max(1, query == null ? 8 : query.Limit))
                .ToArray();
        }
    }

    public static class ServiceEndpointSelector
    {
        public static IReadOnlyList<ServiceEndpoint> OrderForConnection(
            IReadOnlyList<ServiceEndpoint> endpoints,
            string routingKey)
        {
            if (endpoints == null || endpoints.Count == 0) return Array.Empty<ServiceEndpoint>();
            var ordered = endpoints.OrderBy(e => e.Load).ThenBy(e => e.ServiceId, StringComparer.Ordinal).ToArray();
            if (string.IsNullOrEmpty(routingKey) || ordered.Length == 1) return ordered;

            unchecked
            {
                uint hash = 2166136261;
                for (var i = 0; i < routingKey.Length; i++) hash = (hash ^ routingKey[i]) * 16777619;
                var start = (int)(hash % (uint)ordered.Length);
                return ordered.Skip(start).Concat(ordered.Take(start)).ToArray();
            }
        }
    }
}
