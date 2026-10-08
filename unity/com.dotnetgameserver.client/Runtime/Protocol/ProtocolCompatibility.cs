using System;

namespace DotNetGameServer.UnityClient.Protocol
{
    public static class ProtocolCompatibility
    {
        public static bool AreCompatible(string clientVersion, string serverVersion)
        {
            if (string.IsNullOrWhiteSpace(clientVersion) || string.IsNullOrWhiteSpace(serverVersion))
                return true;
            int clientMajor;
            int serverMajor;
            if (TryReadMajor(clientVersion, out clientMajor) && TryReadMajor(serverVersion, out serverMajor))
                return clientMajor == serverMajor;
            return string.Equals(clientVersion.Trim(), serverVersion.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadMajor(string version, out int major)
        {
            major = 0;
            var end = version.IndexOfAny(new[] { '.', '-', '+' });
            var token = end < 0 ? version : version.Substring(0, end);
            return int.TryParse(token, out major);
        }
    }
}
