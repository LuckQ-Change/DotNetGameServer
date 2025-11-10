using System.Security.Cryptography;
using System.Text;
using Shared.Time;

namespace Shared;

public static class TokenSigner
{
    public static string ComputeSignature(string tokenId, long expireUnixSec, string userId, string serverId, string secret)
    {
        var data = $"{tokenId}|{expireUnixSec}|{userId}|{serverId}";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = h.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(sig).ToLowerInvariant();
    }

    public static string ComposeSigned(string tokenId, long expireUnixSec, string signatureHex)
    {
        return $"{tokenId}:{expireUnixSec}:{signatureHex}";
    }

    public static bool TryVerify(string signedToken, string userId, string serverId, string secret, out string tokenId, out long expireUnixSec)
    {
        tokenId = string.Empty;
        expireUnixSec = 0;
        var parts = signedToken.Split(':');
        if (parts.Length != 3) return false;
        tokenId = parts[0];
        if (!long.TryParse(parts[1], out expireUnixSec)) return false;
        var sigHex = parts[2];
        var expected = ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);

        // 先检查是否过期（UTC时间，单位秒），允许有限的时钟偏差容忍窗口
        var nowUnix = TimeManager.UnixSeconds;
        var skew = GetAllowedSkewSeconds();
        if (expireUnixSec + skew < nowUnix) return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(sigHex));
    }

    // 心跳令牌辅助：固定 userId 为 "heartbeat"
    public static string ComposeHeartbeatToken(string serverId, string secret, int ttlSeconds = 300)
    {
        var tokenId = "hb-" + serverId;
        var exp = TimeManager.UnixSeconds + Math.Max(1, ttlSeconds);
        var sig = ComputeSignature(tokenId, exp, "heartbeat", serverId, secret);
        return ComposeSigned(tokenId, exp, sig);
    }

    public static bool TryVerifyHeartbeat(string signedToken, string serverId, string secret, out string tokenId, out long expireUnixSec)
    {
        return TryVerify(signedToken, "heartbeat", serverId, secret, out tokenId, out expireUnixSec);
    }

    private static int GetAllowedSkewSeconds()
    {
        // 允许通过环境变量调整时钟偏差容忍度（默认 60s）
        var env = Environment.GetEnvironmentVariable("TOKEN_TIME_SKEW_SEC");
        if (int.TryParse(env, out var sec) && sec >= 0 && sec <= 600) return sec;
        return 60;
    }
}
