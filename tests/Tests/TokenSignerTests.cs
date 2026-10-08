using Shared;

using Xunit;

namespace Tests;

public class TokenSignerTests
{
    [Fact]
    public void ComputeSignature_ReturnsConsistentHash()
    {
        var tokenId = "test-token";
        var expireUnixSec = 1234567890L;
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";

        var signature1 = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signature2 = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);

        Assert.Equal(signature1, signature2);
        Assert.NotNull(signature1);
        Assert.Equal(64, signature1.Length); // SHA-256 十六进制字符串长度
    }

    [Fact]
    public void TryVerify_ValidToken_ReturnsTrue()
    {
        var tokenId = "test-token";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";

        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signedToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, userId, signature);

        var result = TokenSigner.TryVerify(signedToken, serverId, secret, out var outTokenId, out var outUserId, out var outExpire);

        Assert.True(result);
        Assert.Equal(tokenId, outTokenId);
        Assert.Equal(userId, outUserId);
        Assert.Equal(expireUnixSec, outExpire);
    }

    [Fact]
    public void TryVerify_ExpiredToken_ReturnsFalse()
    {
        var tokenId = "test-token";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds();
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";

        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signedToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, userId, signature);

        var result = TokenSigner.TryVerify(signedToken, serverId, secret, out _, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryVerify_TamperedToken_ReturnsFalse()
    {
        var tokenId = "test-token";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";

        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signedToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, userId, signature);
        var tamperedToken = signedToken + "tampered";

        var result = TokenSigner.TryVerify(tamperedToken, serverId, secret, out _, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void TryVerify_EmptyUserId_ReturnsFalse()
    {
        var tokenId = "test-token";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var serverId = "server456";
        var secret = "test-secret";

        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, string.Empty, serverId, secret);
        var signedToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, string.Empty, signature);

        var result = TokenSigner.TryVerify(signedToken, serverId, secret, out _, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void ComposeServiceToken_GeneratesValidInternalIdentity()
    {
        var sourceServerId = "server-123";
        var targetAudience = "target-server";
        var secret = "test-secret-at-least-32-characters";
        var ttlSeconds = 300;

        var token = TokenSigner.ComposeServiceToken(sourceServerId, targetAudience, secret, ttlSeconds);

        Assert.NotNull(token);
        Assert.True(TokenSigner.TryVerify(
            token,
            targetAudience,
            secret,
            out var tokenId,
            out var identity,
            out _));
        Assert.StartsWith("svc-", tokenId);
        Assert.True(TokenSigner.TryGetServiceIdentity(identity, out var parsedSourceServerId));
        Assert.Equal(sourceServerId, parsedSourceServerId);
    }

    [Fact]
    public void ComposeUserToken_CanBeVerifiedAndReadWithoutSecret()
    {
        var token = TokenSigner.ComposeUserToken(
            "player-1",
            "game-server",
            "test-secret-at-least-32-characters",
            ttlSeconds: 600);

        Assert.True(TokenSigner.TryVerify(
            token,
            "game-server",
            "test-secret-at-least-32-characters",
            out var tokenId,
            out var userId,
            out var expireUnix));
        Assert.StartsWith("usr-", tokenId);
        Assert.Equal("player-1", userId);
        Assert.True(expireUnix > DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        Assert.True(TokenSigner.TryReadToken(token, out _, out var readUserId, out var readExpire));
        Assert.Equal(userId, readUserId);
        Assert.Equal(expireUnix, readExpire);
    }

    [Fact]
    public void TryVerify_AllowExpiredWithinWindow_AcceptsRecentlyExpiredToken()
    {
        var expireUnix = DateTimeOffset.UtcNow.AddSeconds(-120).ToUnixTimeSeconds();
        var signature = TokenSigner.ComputeSignature(
            "tok",
            expireUnix,
            "player-1",
            "game-server",
            "test-secret");
        var token = TokenSigner.ComposeSigned("tok", expireUnix, "player-1", signature);

        Assert.False(TokenSigner.TryVerify(token, "game-server", "test-secret", out _, out _, out _));
        Assert.True(TokenSigner.TryVerify(
            token,
            "game-server",
            "test-secret",
            out _,
            out var userId,
            out _,
            allowedClockSkewSeconds: 0,
            allowExpiredWithinSeconds: 180));
        Assert.Equal("player-1", userId);
    }
}
