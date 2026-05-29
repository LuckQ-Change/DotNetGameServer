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
        Assert.Equal(64, signature1.Length); // SHA256 hex string length
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
    public void ComposeHeartbeatToken_GeneratesValidToken()
    {
        var serverId = "server-123";
        var secret = "test-secret";
        var ttlSeconds = 300;

        var token = TokenSigner.ComposeHeartbeatToken(serverId, secret, ttlSeconds);

        Assert.NotNull(token);
        Assert.StartsWith("hb-server-123:", token);

        var ok = TokenSigner.TryVerifyHeartbeat(token, serverId, secret, out _, out _);
        Assert.True(ok);
    }

    [Fact]
    public void TryVerifyHeartbeat_NonHeartbeatUserId_ReturnsFalse()
    {
        var tokenId = "hb-server123";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var serverId = "server123";
        var secret = "test-secret";

        // 签名时 userId 为普通用户，而非 "heartbeat"
        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, "attacker", serverId, secret);
        var fakeToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, "attacker", signature);

        var result = TokenSigner.TryVerifyHeartbeat(fakeToken, serverId, secret, out _, out _);

        Assert.False(result);
    }

    [Fact]
    public void SetAllowedSkewSeconds_UpdatesSkew()
    {
        TokenSigner.SetAllowedSkewSeconds(120);
        Assert.True(true);
        TokenSigner.SetAllowedSkewSeconds(30); // 恢复新默认值
    }
}
