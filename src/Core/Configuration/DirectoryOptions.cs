namespace Core.Configuration;

public class DirectoryOptions
{
    public string TokenSecret { get; set; } = "";
    public RegistryOptions Registry { get; set; } = new();
}

public class RegistryOptions
{
    public string Mode { get; set; } = "memory";

    /// <summary>心跳超时后标记为 offline 的秒数（与存储后端无关）。</summary>
    public int OfflineTtlSeconds { get; set; } = 120;

    /// <summary>超过此时长未心跳则从注册表移除（与存储后端无关）。</summary>
    public int RemoveTtlSeconds { get; set; } = 600;

    /// <summary>清理周期（秒）（与存储后端无关）。</summary>
    public int CleanupIntervalSeconds { get; set; } = 10;

    public RedisRegistryOptions Redis { get; set; } = new();
}

public class RedisRegistryOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public bool Ssl { get; set; }
    public string SslHost { get; set; } = "";
    public bool ValidateServerCertificate { get; set; } = true;
    public ClientCertificateOptions ClientCertificate { get; set; } = new();
    public int OfflineTtlSeconds { get; set; } = 120;
    public int RemoveTtlSeconds { get; set; } = 600;
    public int CleanupIntervalSeconds { get; set; } = 10;
}

public class ClientCertificateOptions
{
    public string Path { get; set; } = "";
    public string Password { get; set; } = "";
}
