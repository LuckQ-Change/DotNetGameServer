using Core.Config;
using Shared;

namespace ResourceServer;

/// <summary>
/// 简单的URL签名组件：在资源下载URL上附加签名参数。
/// </summary>
public class UrlSignerComponent : Core.IServerComponent
{
    public string Name => "UrlSigner";
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync() => Task.CompletedTask;

    /// <summary>
    /// 为给定URL生成带签名的下载地址。
    /// </summary>
    /// <param name="baseUrl">原始URL。</param>
    /// <param name="serverId">当前服务器ID，用于绑定。</param>
    /// <param name="ttl">有效期。</param>
    public string Sign(string baseUrl, string serverId, TimeSpan ttl)
    {
        var secret = SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory) ?? "dev-secret";
        var tokenId = Guid.NewGuid().ToString("N");
        var expireSec = new DateTimeOffset(DateTime.UtcNow.Add(ttl)).ToUnixTimeSeconds();
        // 使用空用户ID，为资源下载场景生成仅绑定服务与过期时间的签名。
        var sig = TokenSigner.ComputeSignature(tokenId, expireSec, "", serverId, secret);
        var signed = TokenSigner.ComposeSigned(tokenId, expireSec, sig);
        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}token={Uri.EscapeDataString(signed)}&sid={Uri.EscapeDataString(serverId)}&exp={expireSec}";
    }
}
