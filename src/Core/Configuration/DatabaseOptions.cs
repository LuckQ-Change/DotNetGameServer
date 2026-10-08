namespace Core.Configuration;

/// <summary>ADO.NET 数据库配置。具体驱动由业务项目注册，框架不绑定 ORM 或数据库厂商。</summary>
public sealed class DatabaseOptions
{
    public bool Enabled { get; set; }
    public string ProviderInvariantName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public int CommandTimeoutSeconds { get; set; } = 30;
    public int RollbackTimeoutSeconds { get; set; } = 5;
    public int HealthCheckTimeoutSeconds { get; set; } = 3;

    /// <summary>
    /// 就绪探针执行的语句。默认 <c>SELECT 1</c>，SQLite、SQL Server、MySQL 和 PostgreSQL 都可用。
    /// 置空时只打开连接。
    /// </summary>
    public string HealthCheckCommand { get; set; } = "SELECT 1";
}
