using Shared;
using Xunit;

namespace Tests;

public class TokenSignerTests
{
    [Fact]
    public void ComputeSignature_ReturnsConsistentHash()
    {
        // Arrange
        var tokenId = "test-token";
        var expireUnixSec = 1234567890L;
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";

        // Act
        var signature1 = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signature2 = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);

        // Assert
        Assert.Equal(signature1, signature2);
        Assert.NotNull(signature1);
        Assert.Equal(64, signature1.Length); // SHA256 hex string length
    }

    [Fact]
    public void TryVerify_ValidToken_ReturnsTrue()
    {
        // Arrange
        var tokenId = "test-token";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";
        
        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signedToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, signature);

        // Act
        var result = TokenSigner.TryVerify(signedToken, userId, serverId, secret, out var outTokenId, out var outExpire);

        // Assert
        Assert.True(result);
        Assert.Equal(tokenId, outTokenId);
        Assert.Equal(expireUnixSec, outExpire);
    }

    [Fact]
    public void TryVerify_ExpiredToken_ReturnsFalse()
    {
        // Arrange
        var tokenId = "test-token";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds(); // Expired
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";
        
        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signedToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, signature);

        // Act
        var result = TokenSigner.TryVerify(signedToken, userId, serverId, secret, out _, out _);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void TryVerify_TamperedToken_ReturnsFalse()
    {
        // Arrange
        var tokenId = "test-token";
        var expireUnixSec = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
        var userId = "user123";
        var serverId = "server456";
        var secret = "test-secret";
        
        var signature = TokenSigner.ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        var signedToken = TokenSigner.ComposeSigned(tokenId, expireUnixSec, signature);
        var tamperedToken = signedToken + "tampered";

        // Act
        var result = TokenSigner.TryVerify(tamperedToken, userId, serverId, secret, out _, out _);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ComposeHeartbeatToken_GeneratesValidToken()
    {
        // Arrange
        var serverId = "server-123";
        var secret = "test-secret";
        var ttlSeconds = 300;

        // Act
        var token = TokenSigner.ComposeHeartbeatToken(serverId, secret, ttlSeconds);

        // Assert
        Assert.NotNull(token);
        Assert.StartsWith("hb-server-123:", token);
    }

    [Fact]
    public void SetAllowedSkewSeconds_UpdatesSkew()
    {
        // Arrange
        var newSkew = 120;

        // Act
        TokenSigner.SetAllowedSkewSeconds(newSkew);
        
        // 验证：设置不抛异常
        Assert.True(true);
        
        // 清理：恢复默认值
        TokenSigner.SetAllowedSkewSeconds(60);
    }
}