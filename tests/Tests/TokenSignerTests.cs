using Shared;
using Shared.Time;

namespace Tests;

public class TokenSignerTests
{
    public TokenSignerTests()
    {
        TimeManager.UseSystem();
    }

    [Fact]
    public void ComputeSignature_ReturnsExpectedHex()
    {
        var sig = TokenSigner.ComputeSignature("tok1", 9999999999, "user1", "srv1", "secret");
        Assert.False(string.IsNullOrWhiteSpace(sig));
        Assert.Matches("^[0-9a-f]{64}$", sig);
    }

    [Fact]
    public void ComposeSigned_ReturnsCorrectFormat()
    {
        var signed = TokenSigner.ComposeSigned("tok1", 9999999999, "abcdef");
        Assert.Equal("tok1:9999999999:abcdef", signed);
    }

    [Fact]
    public void TryVerify_ValidToken_ReturnsTrue()
    {
        var userId = "user1";
        var serverId = "srv1";
        var secret = "test-secret";
        var tokenId = "tok1";
        var exp = TimeManager.UnixSeconds + 3600;
        var sig = TokenSigner.ComputeSignature(tokenId, exp, userId, serverId, secret);
        var signed = TokenSigner.ComposeSigned(tokenId, exp, sig);

        var ok = TokenSigner.TryVerify(signed, userId, serverId, secret, out var tid, out var e);
        Assert.True(ok);
        Assert.Equal(tokenId, tid);
        Assert.Equal(exp, e);
    }

    [Fact]
    public void TryVerify_ExpiredToken_ReturnsFalse()
    {
        var userId = "user1";
        var serverId = "srv1";
        var secret = "test-secret";
        var tokenId = "tok1";
        var exp = TimeManager.UnixSeconds - 3600;
        var sig = TokenSigner.ComputeSignature(tokenId, exp, userId, serverId, secret);
        var signed = TokenSigner.ComposeSigned(tokenId, exp, sig);

        var ok = TokenSigner.TryVerify(signed, userId, serverId, secret, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryVerify_WrongSecret_ReturnsFalse()
    {
        var userId = "user1";
        var serverId = "srv1";
        var tokenId = "tok1";
        var exp = TimeManager.UnixSeconds + 3600;
        var sig = TokenSigner.ComputeSignature(tokenId, exp, userId, serverId, "real-secret");
        var signed = TokenSigner.ComposeSigned(tokenId, exp, sig);

        var ok = TokenSigner.TryVerify(signed, userId, serverId, "wrong-secret", out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryVerify_WrongUserId_ReturnsFalse()
    {
        var secret = "test-secret";
        var tokenId = "tok1";
        var exp = TimeManager.UnixSeconds + 3600;
        var sig = TokenSigner.ComputeSignature(tokenId, exp, "real-user", "srv1", secret);
        var signed = TokenSigner.ComposeSigned(tokenId, exp, sig);

        var ok = TokenSigner.TryVerify(signed, "wrong-user", "srv1", secret, out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void TryVerify_MalformedToken_ReturnsFalse()
    {
        var ok = TokenSigner.TryVerify("invalid-format", "user1", "srv1", "secret", out _, out _);
        Assert.False(ok);
    }

    [Fact]
    public void ComposeHeartbeatToken_ReturnsValidFormat()
    {
        var token = TokenSigner.ComposeHeartbeatToken("srv1:5500", "secret", 300);
        var parts = token.Split(':');
        Assert.Equal(3, parts.Length);
        Assert.StartsWith("hb-", parts[0]);
    }

    [Fact]
    public void TryVerifyHeartbeat_Valid_ReturnsTrue()
    {
        var serverId = "srv1:5500";
        var secret = "test-secret";
        var token = TokenSigner.ComposeHeartbeatToken(serverId, secret, 300);

        var ok = TokenSigner.TryVerifyHeartbeat(token, serverId, secret, out var tid, out var exp);
        Assert.True(ok);
        Assert.StartsWith("hb-", tid);
        Assert.True(exp > 0);
    }
}
