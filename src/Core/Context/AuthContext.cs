namespace Core;

/// <summary>
/// 请求级身份信息。
/// </summary>
public class AuthInfo
{
    /// <summary>用户ID（业务侧标识）。</summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>令牌ID（签发侧标识）。</summary>
    public string TokenId { get; set; } = string.Empty;

    /// <summary>令牌过期时间（Unix秒）。</summary>
    public long ExpireUnixSeconds { get; set; } = 0;
}
