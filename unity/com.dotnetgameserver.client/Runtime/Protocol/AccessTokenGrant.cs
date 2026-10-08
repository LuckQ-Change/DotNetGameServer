namespace DotNetGameServer.UnityClient.Protocol
{
    public sealed class AccessTokenGrant
    {
        public string AccessToken = string.Empty;
        public string UserId = string.Empty;
        public long ExpireUnix;
        public int RecommendedRefreshSeconds;

        public static AccessTokenGrant Create(
            string accessToken,
            string userId,
            long expireUnix,
            int recommendedRefreshSeconds)
        {
            return new AccessTokenGrant
            {
                AccessToken = accessToken ?? string.Empty,
                UserId = userId ?? string.Empty,
                ExpireUnix = expireUnix,
                RecommendedRefreshSeconds = recommendedRefreshSeconds
            };
        }

        public static bool TryRead(string signedToken, out string tokenId, out string userId, out long expireUnixSec)
        {
            tokenId = string.Empty;
            userId = string.Empty;
            expireUnixSec = 0;
            if (string.IsNullOrWhiteSpace(signedToken)) return false;
            var parts = signedToken.Split(':');
            if (parts.Length != 4) return false;
            long expire;
            if (!long.TryParse(parts[1], out expire)) return false;
            tokenId = parts[0];
            userId = parts[2];
            expireUnixSec = expire;
            return !string.IsNullOrWhiteSpace(tokenId) && !string.IsNullOrWhiteSpace(userId);
        }
    }
}
