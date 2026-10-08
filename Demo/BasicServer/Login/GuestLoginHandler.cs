using BasicServer.Protocol;

using Core;
using Core.Components;

using Shared;

namespace BasicServer;

internal static class ClientSessionTokens
{
    public static GuestLoginResponse Issue(IServerContext context, string userId, string message)
    {
        var secrets = context.GetComponent<ITokenSecretProvider>();
        var secret = secrets?.GetSecret();
        if (string.IsNullOrWhiteSpace(secret))
        {
            return new GuestLoginResponse
            {
                Success = false,
                Code = 503,
                Message = "token_secret_missing"
            };
        }

        var ttl = Math.Max(60, context.ClientTokenTtlSeconds);
        var token = TokenSigner.ComposeUserToken(userId, context.TokenAudience, secret, ttl);
        return new GuestLoginResponse
        {
            Success = true,
            Code = 0,
            Message = message,
            UserId = userId,
            AccessToken = token,
            ExpireUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ttl,
            RecommendedRefreshSeconds = Math.Max(30, ttl / 3)
        };
    }

    public static RefreshSessionResponse ToRefresh(GuestLoginResponse grant) =>
        new()
        {
            Success = grant.Success,
            Code = grant.Code,
            Message = grant.Message,
            UserId = grant.UserId,
            AccessToken = grant.AccessToken,
            ExpireUnix = grant.ExpireUnix,
            RecommendedRefreshSeconds = grant.RecommendedRefreshSeconds
        };
}

public sealed class GuestLoginHandler : IMessageHandler<GuestLoginRequest, GuestLoginResponse>
{
    public Task<GuestLoginResponse?> HandleAsync(GuestLoginRequest request, IServerContext context)
    {
        var displayName = string.IsNullOrWhiteSpace(request.DisplayName)
            ? "guest"
            : request.DisplayName.Trim();
        if (displayName.Length > 32)
        {
            return Task.FromResult<GuestLoginResponse?>(new GuestLoginResponse
            {
                Success = false,
                Code = 400,
                Message = "invalid_display_name"
            });
        }

        var userId = "guest-" + Guid.NewGuid().ToString("N")[..8];
        return Task.FromResult<GuestLoginResponse?>(ClientSessionTokens.Issue(context, userId, displayName));
    }
}

public sealed class RefreshSessionHandler : IMessageHandler<RefreshSessionRequest, RefreshSessionResponse>
{
    public Task<RefreshSessionResponse?> HandleAsync(RefreshSessionRequest request, IServerContext context)
    {
        var token = string.IsNullOrWhiteSpace(request.AccessToken)
            ? context.AccessToken
            : request.AccessToken;
        var secrets = context.GetComponent<ITokenSecretProvider>();
        var secret = secrets?.GetSecret();
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(token))
        {
            return Task.FromResult<RefreshSessionResponse?>(new RefreshSessionResponse
            {
                Success = false,
                Code = 401,
                Message = "authentication_required"
            });
        }

        foreach (var candidate in secrets!.GetVerifySecrets())
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (!TokenSigner.TryVerify(
                    token,
                    context.TokenAudience,
                    candidate,
                    out _,
                    out var userId,
                    out _,
                    allowedClockSkewSeconds: 30,
                    allowExpiredWithinSeconds: context.ClientTokenRefreshGraceSeconds))
                continue;

            var grant = ClientSessionTokens.Issue(context, userId, "refreshed");
            return Task.FromResult<RefreshSessionResponse?>(ClientSessionTokens.ToRefresh(grant));
        }

        return Task.FromResult<RefreshSessionResponse?>(new RefreshSessionResponse
        {
            Success = false,
            Code = 401,
            Message = "authentication_required"
        });
    }
}
