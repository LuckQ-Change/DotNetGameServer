using Core.Config;

namespace Core.Components;

/// <summary>
/// 令牌密钥集中提供组件：启动时加载并缓存，避免多处重复读取。
/// </summary>
public interface ITokenSecretProvider : IServerComponent
{
    /// <summary>获取当前签名密钥（若未配置则返回开发环境默认值）。</summary>
    public string GetSecret();
    /// <summary>获取用于校验的全部候选密钥（主/备），用于密钥轮换过渡期。</summary>
    public IEnumerable<string> GetVerifySecrets();
}

public class TokenSecretProviderComponent(string? baseDir = null) : ITokenSecretProvider
{
    private string? _secret;
    private string? _secondarySecret;
    public string Name => "TokenSecretProvider";

    public Task InitializeAsync()
    {
        // 启动时读取并缓存密钥；环境变量优先，其次配置文件。
        var dir = baseDir ?? AppContext.BaseDirectory;
        var sec = SimpleConfigLoader.LoadDirectoryTokenSecret(dir);
        // 允许通过环境变量提供备用密钥以支持轮换（不写入配置文件）
        var secondaryEnv = Environment.GetEnvironmentVariable("DIRECTORY_TOKEN_SECRET_SECONDARY");
        if (string.IsNullOrWhiteSpace(sec))
        {
            if (IsDevelopment())
            {
                _secret = "dev-secret";
                // 仅开发环境允许使用默认密钥。
                Console.WriteLine("[TokenSecretProvider] 使用开发默认密钥 'dev-secret'。请在生产环境配置 directory.tokenSecret 或环境变量。");
            }
            else
            {
                throw new InvalidOperationException("Token secret 未配置。请通过环境变量或 config.json(directory.tokenSecret) 配置密钥。");
            }
        }
        else
        {
            _secret = sec;
        }
        if (!string.IsNullOrWhiteSpace(secondaryEnv))
        {
            _secondarySecret = secondaryEnv;
        }
        return Task.CompletedTask;
    }

    public Task DestroyAsync() => Task.CompletedTask;

    public string GetSecret()
    {
        if (string.IsNullOrWhiteSpace(_secret))
        {
            // 防御：若初始化失败且当前为开发环境，仍回退到默认值；否则抛出异常。
            if (IsDevelopment()) return "dev-secret";
            throw new InvalidOperationException("Token secret 不可用。请检查服务启动日志与配置。");
        }
        return _secret!;
    }

    public IEnumerable<string> GetVerifySecrets()
    {
        if (!string.IsNullOrWhiteSpace(_secret)) yield return _secret!;
        if (!string.IsNullOrWhiteSpace(_secondarySecret)) yield return _secondarySecret!;
        if (string.IsNullOrWhiteSpace(_secret) && IsDevelopment()) yield return "dev-secret";
    }

    private static bool IsDevelopment()
    {
        var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                  ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                  ?? string.Empty;
        return string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase);
    }
}
