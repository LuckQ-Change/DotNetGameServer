using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared.Constants;

namespace Core.Config;

/// <summary>
/// 配置桥接器：优先从 IConfiguration 读取配置，回退到 SimpleConfigLoader；用于逐步统一配置体系。
/// </summary>
public static class ConfigurationBridge
{
    /// <summary>
    /// 获取目录令牌签名密钥：配置优先，环境变量次之，最后回退到 SimpleConfigLoader。
    /// </summary>
    public static string? GetDirectoryTokenSecret(IConfiguration? config, string? baseDir = null)
    {
        // IConfiguration 优先
        var fromCfg = config?[Shared.Constants.ConfigKeys.DirectoryTokenSecretKeyLower];
        if (!string.IsNullOrWhiteSpace(fromCfg)) return fromCfg;

        // 环境变量次之
        var env = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryTokenSecretEnv);
        if (!string.IsNullOrWhiteSpace(env)) return env;

        // 回退到兼容加载器
        return SimpleConfigLoader.LoadDirectoryTokenSecret(baseDir);
    }

    /// <summary>
    /// 校验并记录配置链路来源，帮助定位线上配置问题。
    /// </summary>
    public static void ValidateAndLog(IConfiguration config, ILogger? logger)
    {
        var secretFromCfg = config[Shared.Constants.ConfigKeys.DirectoryTokenSecretKeyLower];
        var secretFromEnv = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryTokenSecretEnv);
        var secretFromFile = SimpleConfigLoader.LoadDirectoryTokenSecret(AppContext.BaseDirectory);

        string source;
        if (!string.IsNullOrWhiteSpace(secretFromCfg))
        {
            source = "IConfiguration";
        }
        else if (!string.IsNullOrWhiteSpace(secretFromEnv))
        {
            source = "Environment";
        }
        else if (secretFromFile != null)
        {
            source = "config.json";
        }
        else
        {
            source = "missing";
        }

        var effective = GetDirectoryTokenSecret(config);
        var level = source == "missing" ? LogLevel.Warning : LogLevel.Information;
        logger?.Log(level, "DirectoryTokenSecret source={Source}; effective={Effective}", source, string.IsNullOrWhiteSpace(effective) ? "<null>" : "<redacted>");
    }
}