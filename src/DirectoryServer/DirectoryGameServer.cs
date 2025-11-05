using Core;
using DotNetGameServer;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Shared.Constants;

namespace DirectoryServer;

/// <summary>
/// 目录/网关服务器：提供服务器列表、状态查询与切服令牌签发
/// </summary>
public class DirectoryGameServer : BaseGameServer
{
    public DirectoryGameServer(Action<string> logger) : base("Directory", logger)
    {
        // 注册目录组件（内存注册表）
        var registry = new Components.InMemoryServerRegistryComponent();
        AddComponent<Components.IServerRegistryComponent>(registry);

        // 注册指标组件（定期汇总）
        var metrics = new Components.DirectoryMetricsComponent(message => Log(message), () => registry.ListServers().Count());
        AddComponent<Components.DirectoryMetricsComponent>(metrics);

        // 注册消息处理器（强类型）
        RegisterStrongMessageHandler<QueryServersRequest, QueryServersResponse>(new Handlers.QueryServersMessageHandler());
        RegisterStrongMessageHandler<QueryServerStatusRequest, QueryServerStatusResponse>(new Handlers.QueryServerStatusMessageHandler());
        RegisterStrongMessageHandler<SwitchServerRequest, SwitchServerResponse>(new Handlers.SwitchServerMessageHandler());
        RegisterStrongMessageHandler<VerifySwitchTokenRequest, VerifySwitchTokenResponse>(new Handlers.VerifySwitchTokenMessageHandler());
        RegisterStrongMessageHandler<HeartbeatRequest, HeartbeatResponse>(new Handlers.HeartbeatMessageHandler());
    }

    public DirectoryGameServer(ILogger logger) : base("Directory", logger)
    {
        var registry = new Components.InMemoryServerRegistryComponent();
        AddComponent<Components.IServerRegistryComponent>(registry);

        var metrics = new Components.DirectoryMetricsComponent(message => Log(message), () => registry.ListServers().Count());
        AddComponent<Components.DirectoryMetricsComponent>(metrics);

        RegisterStrongMessageHandler<QueryServersRequest, QueryServersResponse>(new Handlers.QueryServersMessageHandler());
        RegisterStrongMessageHandler<QueryServerStatusRequest, QueryServerStatusResponse>(new Handlers.QueryServerStatusMessageHandler());
        RegisterStrongMessageHandler<SwitchServerRequest, SwitchServerResponse>(new Handlers.SwitchServerMessageHandler());
        RegisterStrongMessageHandler<VerifySwitchTokenRequest, VerifySwitchTokenResponse>(new Handlers.VerifySwitchTokenMessageHandler());
        RegisterStrongMessageHandler<HeartbeatRequest, HeartbeatResponse>(new Handlers.HeartbeatMessageHandler());
    }

    public DirectoryGameServer(ILogger logger, IConfiguration configuration) : base("Directory", logger)
    {
        // 依据配置选择注册组件实现：优先 Redis，其次内存
        var mode = configuration[ConfigKeys.DirectoryRegistryMode] ??
                   Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRegistryEnv) ?? "memory";
        Components.IServerRegistryComponent registry;
        if (string.Equals(mode, "redis", StringComparison.OrdinalIgnoreCase))
        {
            var conn = configuration[ConfigKeys.DirectoryRegistryRedisConnectionString] ??
                       Environment.GetEnvironmentVariable(ConfigKeys.RedisConnectionEnv) ?? "localhost:6379";
            // 外部化参数：离线 TTL、清理 TTL、清理轮询间隔（秒）
            TimeSpan? offlineTtl = null, removeTtl = null, cleanupInterval = null;
            if (int.TryParse(configuration[ConfigKeys.DirectoryRegistryRedisOfflineTtlSeconds], out var offlineSec) && offlineSec > 0)
                offlineTtl = TimeSpan.FromSeconds(offlineSec);
            else if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRegistryOfflineTtlSecondsEnv), out var offlineEnv) && offlineEnv > 0)
                offlineTtl = TimeSpan.FromSeconds(offlineEnv);

            if (int.TryParse(configuration[ConfigKeys.DirectoryRegistryRedisRemoveTtlSeconds], out var removeSec) && removeSec > 0)
                removeTtl = TimeSpan.FromSeconds(removeSec);
            else if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRegistryRemoveTtlSecondsEnv), out var removeEnv) && removeEnv > 0)
                removeTtl = TimeSpan.FromSeconds(removeEnv);

            if (int.TryParse(configuration[ConfigKeys.DirectoryRegistryRedisCleanupIntervalSeconds], out var cleanupSec) && cleanupSec > 0)
                cleanupInterval = TimeSpan.FromSeconds(cleanupSec);
            else if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.DirectoryRegistryCleanupIntervalSecondsEnv), out var cleanupEnv) && cleanupEnv > 0)
                cleanupInterval = TimeSpan.FromSeconds(cleanupEnv);

            // TLS/mTLS 配置（低优先级增强）：支持 Ssl、SslHost、客户端证书路径/密码
            var sslOpt = configuration[ConfigKeys.DirectoryRegistryRedisSsl];
            var sslHostOpt = configuration[ConfigKeys.DirectoryRegistryRedisSslHost];
            var certPathOpt = configuration[ConfigKeys.DirectoryRegistryRedisClientCertPath];
            var certPwdOpt = configuration[ConfigKeys.DirectoryRegistryRedisClientCertPassword];
            var validateServerCertOpt = configuration[ConfigKeys.DirectoryRegistryRedisValidateServerCertificate];

            // 证书构造拆分，避免嵌套三元表达式（S3358）
            System.Security.Cryptography.X509Certificates.X509Certificate2? clientCert = null;
            if (!string.IsNullOrWhiteSpace(certPathOpt))
            {
                clientCert = string.IsNullOrWhiteSpace(certPwdOpt)
                    ? new System.Security.Cryptography.X509Certificates.X509Certificate2(certPathOpt)
                    : new System.Security.Cryptography.X509Certificates.X509Certificate2(certPathOpt, certPwdOpt);
            }

            var securityOptions = new Components.RedisServerRegistryComponent.RedisSecurityOptions
            {
                Ssl = bool.TryParse(sslOpt, out var sslParsed) ? sslParsed : (bool?)null,
                SslHost = string.IsNullOrWhiteSpace(sslHostOpt) ? null : sslHostOpt,
                ClientCertificate = clientCert,
                CertificateValidationOverride = bool.TryParse(validateServerCertOpt, out var validateFlag)
                    ? ((sender, certificate, chain, errors) =>
                    {
                        if (validateFlag) return errors == System.Net.Security.SslPolicyErrors.None;
                        return true;
                    })
                    : null
            };

            registry = new Components.RedisServerRegistryComponent(conn, "registry", offlineTtl, removeTtl, cleanupInterval, securityOptions);
        }
        else
        {
            registry = new Components.InMemoryServerRegistryComponent();
        }
        AddComponent<Components.IServerRegistryComponent>(registry);

        var metrics = new Components.DirectoryMetricsComponent(message => Log(message), () => registry.ListServers().Count());
        AddComponent<Components.DirectoryMetricsComponent>(metrics);

        RegisterStrongMessageHandler<QueryServersRequest, QueryServersResponse>(new Handlers.QueryServersMessageHandler());
        RegisterStrongMessageHandler<QueryServerStatusRequest, QueryServerStatusResponse>(new Handlers.QueryServerStatusMessageHandler());
        RegisterStrongMessageHandler<SwitchServerRequest, SwitchServerResponse>(new Handlers.SwitchServerMessageHandler());
        RegisterStrongMessageHandler<VerifySwitchTokenRequest, VerifySwitchTokenResponse>(new Handlers.VerifySwitchTokenMessageHandler());
        RegisterStrongMessageHandler<HeartbeatRequest, HeartbeatResponse>(new Handlers.HeartbeatMessageHandler());
    }
}