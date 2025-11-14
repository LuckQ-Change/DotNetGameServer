using Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Shared.Constants;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;

namespace DirectoryServer;

/// <summary>
/// 目录/网关服务器：提供服务器列表、状态查询与切服令牌签发
/// </summary>
public class DirectoryGameServer : BaseGameServer
{
    public DirectoryGameServer(ILogger logger, IConfiguration configuration) : base(nameof(DirectoryGameServer), logger)
    {
        var registry = CreateServerRegistry(configuration);
        AddComponent(registry);

        var metrics =
            new Components.DirectoryMetricsComponent(message => Log(message), () => registry.ListServers().Count());
        AddComponent(metrics);

        RegisterMessageHandlers();
    }

    /// <summary>
    /// 创建服务器注册组件
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    private Components.IServerRegistryComponent CreateServerRegistry(IConfiguration configuration)
    {
        var mode = configuration[ConfigKeys.DirectoryRegistryMode] ??
                   Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRegistryEnv) ?? "memory";

        return mode.Equals("redis", StringComparison.OrdinalIgnoreCase)
            ? CreateRedisServerRegistry(configuration)
            : new Components.InMemoryServerRegistryComponent();
    }

    /// <summary>
    /// 创建Redis服务器注册组件
    /// </summary>
    /// <param name="configuration"></param>
    /// <returns></returns>
    private Components.IServerRegistryComponent CreateRedisServerRegistry(IConfiguration configuration)
    {
        var connectionString = configuration[ConfigKeys.DirectoryRegistryRedisConnectionString] ??
                               Environment.GetEnvironmentVariable(ConfigKeys.RedisConnectionEnv) ?? "localhost:6379";

        var (offlineTtl, removeTtl, cleanupInterval) = ParseRedisTtlConfiguration(configuration);
        var securityOptions = BuildRedisSecurityOptions(configuration);

        return new Components.RedisServerRegistryComponent(
            connectionString,
            "registry",
            offlineTtl,
            removeTtl,
            cleanupInterval,
            securityOptions);
    }

    /// <summary>
    /// 解析Redis TTL配置
    /// </summary>
    private (TimeSpan? offlineTtl, TimeSpan? removeTtl, TimeSpan? cleanupInterval) ParseRedisTtlConfiguration(
        IConfiguration configuration)
    {
        TimeSpan? offlineTtl = ParseTimeSpanSeconds(
            configuration[ConfigKeys.DirectoryRegistryRedisOfflineTtlSeconds],
            ConfigKeys.DirectoryRegistryOfflineTtlSecondsEnv);

        TimeSpan? removeTtl = ParseTimeSpanSeconds(
            configuration[ConfigKeys.DirectoryRegistryRedisRemoveTtlSeconds],
            ConfigKeys.DirectoryRegistryRemoveTtlSecondsEnv);

        TimeSpan? cleanupInterval = ParseTimeSpanSeconds(
            configuration[ConfigKeys.DirectoryRegistryRedisCleanupIntervalSeconds],
            ConfigKeys.DirectoryRegistryCleanupIntervalSecondsEnv);

        return (offlineTtl, removeTtl, cleanupInterval);
    }

    /// <summary>
    /// 解析时间间隔
    /// </summary>
    private TimeSpan? ParseTimeSpanSeconds(string? configValue, string environmentVariableKey)
    {
        if (int.TryParse(configValue, out var seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds);

        var envValue = Environment.GetEnvironmentVariable(environmentVariableKey);
        if (int.TryParse(envValue, out var envSeconds) && envSeconds > 0)
            return TimeSpan.FromSeconds(envSeconds);

        return null;
    }

    /// <summary>
    /// 创建Redis安全选项
    /// </summary>
    private Components.RedisServerRegistryComponent.RedisSecurityOptions BuildRedisSecurityOptions(
        IConfiguration configuration)
    {
        var sslOption = configuration[ConfigKeys.DirectoryRegistryRedisSsl];
        var sslHostOption = configuration[ConfigKeys.DirectoryRegistryRedisSslHost];
        var certPathOption = configuration[ConfigKeys.DirectoryRegistryRedisClientCertPath];
        var certPasswordOption = configuration[ConfigKeys.DirectoryRegistryRedisClientCertPassword];
        var validateServerCertOption = configuration[ConfigKeys.DirectoryRegistryRedisValidateServerCertificate];

        X509Certificate2? clientCertificate = null;
        if (!string.IsNullOrWhiteSpace(certPathOption))
        {
            clientCertificate = string.IsNullOrWhiteSpace(certPasswordOption)
                ? new X509Certificate2(certPathOption)
                : new X509Certificate2(certPathOption, certPasswordOption);
        }

        return new Components.RedisServerRegistryComponent.RedisSecurityOptions
        {
            Ssl = bool.TryParse(sslOption, out var sslValue) ? sslValue : (bool?)null,
            SslHost = string.IsNullOrWhiteSpace(sslHostOption) ? null : sslHostOption,
            ClientCertificate = clientCertificate,
            CertificateValidationOverride = CreateCertificateValidationCallback(validateServerCertOption)
        };
    }

    /// <summary>
    /// 创建服务器证书验证回调
    /// </summary>
    /// <param name="validateServerCertOption"></param>
    /// <returns></returns>
    private RemoteCertificateValidationCallback? CreateCertificateValidationCallback(string? validateServerCertOption)
    {
        if (bool.TryParse(validateServerCertOption, out var validateFlag))
        {
            return (sender, certificate, chain, errors) =>
            {
                if (validateFlag)
                    return errors == SslPolicyErrors.None;
                return true;
            };
        }

        return null;
    }

    private void RegisterMessageHandlers()
    {
        RegisterMessageHandler(new Handlers.QueryServersMessageHandler());
        RegisterMessageHandler(new Handlers.QueryServerStatusMessageHandler());
        RegisterMessageHandler(new Handlers.SwitchServerMessageHandler());
        RegisterMessageHandler(new Handlers.VerifySwitchTokenMessageHandler());
        RegisterMessageHandler(new Handlers.HeartbeatMessageHandler());
    }
}
