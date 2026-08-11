using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DotNetGameServer.UnityClient.Discovery;
using DotNetGameServer.UnityClient.Logging;

namespace DotNetGameServer.UnityClient.Unity
{
    public sealed class UnityHttpServiceDiscoveryProvider : IServiceDiscoveryProvider
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly string[] _bootstrapUrls;
        private readonly int _timeoutSeconds;

        public UnityHttpServiceDiscoveryProvider(IEnumerable<string> bootstrapUrls, int timeoutSeconds = 5)
        {
            _bootstrapUrls = (bootstrapUrls ?? Array.Empty<string>())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url.TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _timeoutSeconds = Math.Max(1, timeoutSeconds);
        }

        public async Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(
            ServiceQuery query,
            CancellationToken cancellationToken)
        {
            query = query ?? new ServiceQuery();
            Exception last = null;
            foreach (var bootstrap in _bootstrapUrls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var url = bootstrap + "/.well-known/dotnet-game-server/services"
                    + "?responsibility=" + Uri.EscapeDataString(query.Responsibility ?? string.Empty)
                    + "&region=" + Uri.EscapeDataString(query.Region ?? string.Empty)
                    + "&limit=" + Math.Max(1, query.Limit);
                try
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
                        using (var response = await HttpClient.SendAsync(request, timeout.Token).ConfigureAwait(false))
                        {
                            response.EnsureSuccessStatusCode();
                            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            var catalog = DeserializeCatalog(json);
                            if (catalog == null || catalog.Services == null)
                                return Array.Empty<ServiceEndpoint>();

                            var fallbackHost = new Uri(bootstrap).Host;
                            return catalog.Services.Select(dto => dto.ToEndpoint(fallbackHost)).ToArray();
                        }
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    last = new TimeoutException(
                        "Discovery HTTP request timed out after " + _timeoutSeconds + " seconds: " + bootstrap);
                    NetworkLogHub.Warning("Discovery", "Bootstrap timed out: " + bootstrap, exception: last);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    last = exception;
                    NetworkLogHub.Warning("Discovery", "Bootstrap failed: " + bootstrap, exception: exception);
                }
            }
            throw new InvalidOperationException("No discovery bootstrap URL succeeded.", last);
        }

        private static ServiceCatalogResponse DeserializeCatalog(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var serializer = new DataContractJsonSerializer(typeof(ServiceCatalogResponse));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as ServiceCatalogResponse;
            }
        }

        [DataContract]
        private sealed class ServiceCatalogResponse
        {
            [DataMember(Name = "schemaVersion")]
            public int SchemaVersion;

            [DataMember(Name = "services")]
            public ServiceDto[] Services;
        }

        [DataContract]
        private sealed class ServiceDto
        {
            [DataMember(Name = "serviceId")]
            public string ServiceId;

            [DataMember(Name = "role")]
            public string Role;

            [DataMember(Name = "host")]
            public string Host;

            [DataMember(Name = "port")]
            public int Port;

            [DataMember(Name = "region")]
            public string Region;

            [DataMember(Name = "version")]
            public string Version;

            [DataMember(Name = "playerCount")]
            public int PlayerCount;

            [DataMember(Name = "capacity")]
            public int Capacity;

            [DataMember(Name = "tlsEnabled")]
            public bool TlsEnabled;

            [DataMember(Name = "tlsServerName")]
            public string TlsServerName;

            [DataMember(Name = "responsibilities")]
            public string[] Responsibilities;

            public ServiceEndpoint ToEndpoint(string fallbackHost)
            {
                return new ServiceEndpoint
                {
                    ServiceId = ServiceId ?? string.Empty,
                    Role = Role ?? string.Empty,
                    Host = string.IsNullOrWhiteSpace(Host) || Host == "0.0.0.0" || Host == "::" ? fallbackHost : Host,
                    Port = Port,
                    Region = Region ?? string.Empty,
                    Version = Version ?? string.Empty,
                    PlayerCount = PlayerCount,
                    Capacity = Capacity,
                    TlsEnabled = TlsEnabled,
                    TlsServerName = TlsServerName ?? string.Empty,
                    Responsibilities = Responsibilities ?? Array.Empty<string>()
                };
            }
        }
    }
}
