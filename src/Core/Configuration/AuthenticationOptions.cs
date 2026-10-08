namespace Core.Configuration;

/// <summary>
/// 所有服务器类型共用的消息鉴权配置。
/// 密钥应存放在源码管理之外，并通过外部配置注入。
/// </summary>
public sealed class AuthenticationOptions
{
    /// <summary>HMAC 签名密钥；生产环境至少使用 32 个随机字符。</summary>
    public string TokenSecret { get; set; } = string.Empty;

    /// <summary>期望的令牌受众；留空时使用服务器名称。</summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>允许的令牌时钟偏差，单位为秒。</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>客户端通道是否必须携带有效令牌。</summary>
    public bool RequireClientAuthentication { get; set; }

    /// <summary>GuestLogin 等接口签发的客户端令牌有效期，单位秒。</summary>
    public int ClientTokenTtlSeconds { get; set; } = 3600;

    /// <summary>刷新接口允许过期令牌再使用的宽限秒数。</summary>
    public int ClientTokenRefreshGraceSeconds { get; set; } = 86_400;

    /// <summary>服务器内部通道是否必须携带有效的服务令牌。</summary>
    public bool RequireInternalAuthentication { get; set; } = true;
}
