using Shared;

namespace Client;

public sealed class AccessTokenGrant
{
    public string AccessToken { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public long ExpireUnix { get; init; }
    public int RecommendedRefreshSeconds { get; init; }

    public static AccessTokenGrant Create(
        string accessToken,
        string userId,
        long expireUnix,
        int recommendedRefreshSeconds = 0) =>
        new()
        {
            AccessToken = accessToken,
            UserId = userId,
            ExpireUnix = expireUnix,
            RecommendedRefreshSeconds = recommendedRefreshSeconds
        };

    public static AccessTokenGrant? FromAccessToken(string? accessToken)
    {
        if (!TokenSigner.TryReadToken(accessToken, out _, out var userId, out var expireUnix))
            return null;
        return Create(accessToken!, userId, expireUnix);
    }
}
