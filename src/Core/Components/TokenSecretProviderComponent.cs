using Core.Config;

namespace Core.Components;

/// <summary>
/// 令牌密钥集中提供组件：启动时加载并缓存，避免多处重复读取。
/// </summary>
public interface ITokenSecretProvider : IServerComponent
{
    /// <summary>获取当前签名密钥（若未配置则返回开发环境默认值）。</summary>
    string GetSecret();
}

public class TokenSecretProviderComponent : ITokenSecretProvider
{
    private readonly string _baseDir;
    private string _secret = "dev-secret";
    public string Name => "TokenSecretProvider";

    public TokenSecretProviderComponent(string? baseDir = null)
    {
        _baseDir = baseDir ?? AppContext.BaseDirectory;
    }

    public Task InitializeAsync()
    {
        // 启动时读取并缓存密钥；环境变量优先，其次配置文件；最后回退默认值。
        _secret = SimpleConfigLoader.LoadDirectoryTokenSecret(_baseDir) ?? "dev-secret";
        return Task.CompletedTask;
    }

    public Task DestroyAsync() => Task.CompletedTask;

    public string GetSecret() => _secret;
}