using Core.Config;
using Shared;
using System;

namespace ResourceServer;

/// <summary>
/// 简单的URL签名组件：在资源下载URL上附加签名参数。
/// </summary>
public class UrlSignerComponent : Core.IServerComponent
{
    private string? _secret;
    public string Name => "UrlSigner";
    public Task InitializeAsync()
    {
        var sec = SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory);
        if (string.IsNullOrWhiteSpace(sec))
        {
            if (IsDevelopment())
            {
                _secret = "dev-secret";
            }
            else
            {
                throw new InvalidOperationException("UrlSignerComponent 需要配置目录密钥 (directory.tokenSecret)。");
            }
        }
        else
        {
            _secret = sec;
        }
        return Task.CompletedTask;
    }
    public Task DestroyAsync() => Task.CompletedTask;

    /// <summary>
    /// 为给定URL生成带签名的下载地址。
    /// </summary>
    /// <param name="baseUrl">原始URL。</param>
    /// <param name="serverId">当前服务器ID，用于绑定。</param>
    /// <param name="ttl">有效期。</param>
    public string Sign(string baseUrl, string serverId, TimeSpan ttl)
    {
        var secret = _secret ?? "dev-secret"; // 开发环境允许默认值
        var tokenId = Guid.NewGuid().ToString("N");
        var expireSec = new DateTimeOffset(DateTime.UtcNow.Add(ttl)).ToUnixTimeSeconds();
        // 使用空用户ID，为资源下载场景生成仅绑定服务与过期时间的签名。
        var sig = TokenSigner.ComputeSignature(tokenId, expireSec, "", serverId, secret);
        var signed = TokenSigner.ComposeSigned(tokenId, expireSec, sig);
        var separator = baseUrl.Contains('?') ? '&' : '?';
        return $"{baseUrl}{separator}token={Uri.EscapeDataString(signed)}&sid={Uri.EscapeDataString(serverId)}&exp={expireSec}";
    }

    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? string.Empty;
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
