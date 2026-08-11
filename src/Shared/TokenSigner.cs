using System.Security.Cryptography;
using System.Text;

namespace Shared;

public static class TokenSigner
{
    public const string ServiceIdentityPrefix = "service.";
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
        out string userId, out long expireUnixSec, int allowedClockSkewSeconds = 30)
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
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var skew = Math.Max(0, allowedClockSkewSeconds);
        if (expireUnixSec + skew < nowUnix) return false;

        var expected = ComputeSignature(tokenId, expireUnixSec, userId, serverId, secret);
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(sigHex));
    }

    /// <summary>签发服务器内部调用令牌。</summary>
    public static string ComposeServiceToken(
        string sourceServerId,
        string targetAudience,
        string secret,
        int ttlSeconds = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceServerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAudience);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var tokenId = $"svc-{Guid.NewGuid():N}";
        var serviceIdentity = ServiceIdentityPrefix + EncodeBase64Url(sourceServerId);
        var expireUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(1, ttlSeconds);
        var signature = ComputeSignature(tokenId, expireUnix, serviceIdentity, targetAudience, secret);
        return ComposeSigned(tokenId, expireUnix, serviceIdentity, signature);
    }

    /// <summary>从已验证的服务身份中读取来源服务器 ID。</summary>
    public static bool TryGetServiceIdentity(string? identity, out string sourceServerId)
    {
        sourceServerId = string.Empty;
        if (string.IsNullOrWhiteSpace(identity)
            || !identity.StartsWith(ServiceIdentityPrefix, StringComparison.Ordinal))
            return false;
        try
        {
            var encoded = identity[ServiceIdentityPrefix.Length..]
                .Replace('-', '+')
                .Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - encoded.Length % 4) % 4), '=');
            sourceServerId = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return !string.IsNullOrWhiteSpace(sourceServerId);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string EncodeBase64Url(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

}
