namespace Shared.Constants;

/// <summary>
/// 集中定义常用配置键与环境变量名，供各组件统一引用，避免魔法字符串与拼写错误。
/// </summary>
public static class ConfigKeys
{
    // DirectoryServer - 配置路径（IConfiguration 键）
    public const string DirectoryRegistryMode = "Directory:Registry:Mode";
    public const string DirectoryRegistryRedisConnectionString = "Directory:Registry:Redis:ConnectionString";
    public const string DirectoryRegistryRedisOfflineTtlSeconds = "Directory:Registry:Redis:OfflineTtlSeconds";
    public const string DirectoryRegistryRedisRemoveTtlSeconds = "Directory:Registry:Redis:RemoveTtlSeconds";
    public const string DirectoryRegistryRedisCleanupIntervalSeconds = "Directory:Registry:Redis:CleanupIntervalSeconds";
    public const string DirectoryRegistryRedisSsl = "Directory:Registry:Redis:Ssl";
    public const string DirectoryRegistryRedisSslHost = "Directory:Registry:Redis:SslHost";
    public const string DirectoryRegistryRedisClientCertPath = "Directory:Registry:Redis:ClientCertificate:Path";
    public const string DirectoryRegistryRedisClientCertPassword = "Directory:Registry:Redis:ClientCertificate:Password";
    public const string DirectoryRegistryRedisValidateServerCertificate = "Directory:Registry:Redis:ValidateServerCertificate";

    // DirectoryServer - Redis 安全相关
    public const string DirectoryRedisSsl = "DIRECTORY_REDIS_SSL";
    public const string DirectoryRedisSslHost = "DIRECTORY_REDIS_SSL_HOST";
    public const string DirectoryRedisClientCertPath = "DIRECTORY_REDIS_CLIENT_CERT_PATH";
    public const string DirectoryRedisClientCertPassword = "DIRECTORY_REDIS_CLIENT_CERT_PASSWORD";
    public const string DirectoryRedisValidateServerCert = "DIRECTORY_REDIS_VALIDATE_SERVER_CERT";

    // DirectoryServer - 注册中心序列化格式
    public const string DirectoryRegistryUseMsgpack = "DIRECTORY_REGISTRY_USE_MSGPACK";

    // DirectoryServer - 注册组件选择与参数（环境变量名）
    public const string DirectoryRegistryEnv = "DIRECTORY_REGISTRY";
    public const string DirectoryRegistryOfflineTtlSecondsEnv = "DIRECTORY_REGISTRY_OFFLINE_TTL_SECONDS";
    public const string DirectoryRegistryRemoveTtlSecondsEnv = "DIRECTORY_REGISTRY_REMOVE_TTL_SECONDS";
    public const string DirectoryRegistryCleanupIntervalSecondsEnv = "DIRECTORY_REGISTRY_CLEANUP_INTERVAL_SECONDS";
    public const string RedisConnectionEnv = "REDIS_CONNECTION";

    // Core/Base/Server 通用运行参数（环境变量名）
    public const string ServerMaxMessageSizeEnv = "SERVER_MAX_MESSAGE_SIZE";
    public const string ServerMaxConnectionsEnv = "SERVER_MAX_CONNECTIONS";
    public const string ServerReceiveTimeoutMsEnv = "SERVER_RECEIVE_TIMEOUT_MS";
    public const string ServerAcceptBackoffMsEnv = "SERVER_ACCEPT_BACKOFF_MS";
    public const string ServerMaxBpsEnv = "SERVER_MAX_BPS";
    public const string ServerMaxMsgsEnv = "SERVER_MAX_MSGS";
    public const string ServerIdleMsEnv = "SERVER_IDLE_MS";

    // 目录令牌密钥（环境变量名）
    public const string DirectoryTokenSecretEnv = "DIRECTORY_TOKEN_SECRET";

    // ServerHost - 服务、端口与健康检查、遥测
    public const string ServiceConfigKey = "service"; // 注意：该键在 ServerHost 使用小写
    public const string ServiceEnv = "SERVICE";
    public const string PortEnv = "PORT";
    public const string TelemetryMetricsEnabledEnv = "TELEMETRY_METRICS_ENABLED";
    public const string TelemetryMetricsEnabledKeyLower = "telemetry:metrics:enabled";
    public const string OtlpEndpointEnv = "OTLP_ENDPOINT";
    public const string ServiceHealthCheckUrlEnv = "SERVICE__HEALTHCHECKURL"; // ASP.NET Core 双下划线约定
    public const string HealthCheckUrlEnv = "HEALTHCHECK_URL";
    public const string ServiceHealthCheckUrlKeyLower = "service:healthCheckUrl";

    // ServerHost - 小写配置键（与 appsettings casing 保持一致）
    public const string DirectoryRegistryModeLower = "directory:registry:mode";
    public const string DirectoryRegistryRedisConnectionStringLower = "directory:registry:redis:connectionString";
    public const string TelemetryMetricsOtlpEndpointKeyLower = "telemetry:metrics:otlp:endpoint";
    public const string DirectoryTokenSecretKeyLower = "Directory:TokenSecret";

    // Directory registry mode values
    public const string DirectoryRegistryModeValueMemory = "memory";
    public const string DirectoryRegistryModeValueRedis = "redis";

    // 预留：其他服务通用配置键（根据项目需要逐步补充）
}
