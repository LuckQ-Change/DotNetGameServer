using System.Security.Cryptography;
using System.Text;

using Shared.Time;

namespace Shared;

public static class TokenSigner
{
    public static string ComputeSignature(string tokenId, long expireUnixSec, string userId, string serverId,
        string secret)
    {
        var data = $"{tokenId}|{expireUnixSec}|{userId}|{serverId}";
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = h.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(sig).ToLowerInvariant();
    }

    /// <summary>
    /// 将 tokenId、过期时间、userId 和签名组合为 token 字符串。
    /// 格式：{tokenId}:{expireUnixSec}:{userId}:{signatureHex}
    /// </summary>
    public static string ComposeSigned(string tokenId, long expireUnixSec, string userId, string signatureHex)
    {
        return $"{tokenId}:{expireUnixSec}:{userId}:{signatureHex}";
    }

    /// <summary>
    /// 验证 token，并从 token 中提取 userId。
    /// userId 不再作为输入参数，而是从 token payload 中解析后再参与签名校验。
    /// </summary>
    public static bool TryVerify(string signedToken, string serverId, string secret, out string tokenId,
        out string userId, out long expireUnixSec)
    {
        tokenId = string.Empty;
        userId = string.Empty;
        expireUnixSec = 0;
        var parts = signedToken.Split(':');
        if (parts.Length != 4) return false;
        tokenId = parts[0];
        if (!long.TryParse(parts[1], out expireUnixSec)) return false;
        userId = parts[2];
        var sigHex = parts[3];

        if (string.IsNullOrWhiteSpace(userId)) return false;

        // 先检查是否过期（UTC时间，单位秒），允许有限的时钟偏差容忍窗口
        var nowUnix = TimeManager.UnixSeconds;
        var skew = GetAllowedSkewSeconds();
        if (expireUnixSec + skew < nowUnix) return false;

        var expected = ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(sigHex));
    }

    // 心跳令牌辅助：固定 userId 为 "heartbeat"
    public static string ComposeHeartbeatToken(string serverId, string secret, int ttlSeconds = 300)
    {
        var tokenId = "hb-" + serverId.Replace(":", string.Empty);
        var exp = TimeManager.UnixSeconds + Math.Max(1, ttlSeconds);
        var sig = ComputeSignature(tokenId, exp, "heartbeat", serverId, secret);
        return ComposeSigned(tokenId, exp, "heartbeat", sig);
    }

    public static bool TryVerifyHeartbeat(string signedToken, string serverId, string secret, out string tokenId,
        out long expireUnixSec)
    {
        if (!TryVerify(signedToken, serverId, secret, out tokenId, out var uid, out expireUnixSec))
            return false;
        return string.Equals(uid, "heartbeat", StringComparison.Ordinal);
    }

    /// <summary>
    /// 时钟偏差容忍窗口（秒）。可通过 <see cref="SetAllowedSkewSeconds"/> 修改，默认 30s。
    /// </summary>
    private static int _allowedSkewSeconds = 30;

    public static void SetAllowedSkewSeconds(int seconds)
    {
        if (seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds), "时钟偏差不允许为负值");
        _allowedSkewSeconds = seconds;
    }

    private static int GetAllowedSkewSeconds() => _allowedSkewSeconds;
}
