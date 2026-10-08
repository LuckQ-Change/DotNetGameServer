using System;
using System.Collections.Generic;

namespace DotNetGameServer.UnityClient.Discovery
{
    [Serializable]
    public sealed class ServiceEndpoint
    {
        public string ServiceId = string.Empty;
        public string Role = string.Empty;
        public string Host = string.Empty;
        public int Port;
        public string Region = string.Empty;
        public string Version = string.Empty;
        public int PlayerCount;
        public int Capacity;
        public bool TlsEnabled;
        public string TlsServerName = string.Empty;
        public string[] Responsibilities = Array.Empty<string>();
        public int HeartbeatIntervalSeconds;

        public float Load
        {
            get { return Capacity > 0 ? Math.Min(1f, (float)PlayerCount / Capacity) : 0f; }
        }

        public bool HasResponsibility(string responsibility)
        {
            if (string.IsNullOrWhiteSpace(responsibility)) return true;
            for (var i = 0; i < Responsibilities.Length; i++)
            {
                if (string.Equals(Responsibilities[i], responsibility, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return string.Equals(Role, responsibility, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return Role + "@" + Host + ":" + Port + (TlsEnabled ? " (TLS)" : string.Empty);
        }
    }

    public sealed class ServiceQuery
    {
        public string Responsibility { get; set; } = string.Empty;
        public string Region { get; set; } = string.Empty;
        public string RoutingKey { get; set; } = string.Empty;
        public int Limit { get; set; } = 8;
    }

    public interface IServiceDiscoveryProvider
    {
        System.Threading.Tasks.Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(
            ServiceQuery query,
            System.Threading.CancellationToken cancellationToken);
    }
}
