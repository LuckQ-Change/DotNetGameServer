namespace ControlPlane;

public sealed class ControlPlaneOptions
{
    public DirectoryOptions Directory { get; set; } = new();
    public AdminOptions Admin { get; set; } = new();
    public List<ServerEndpoint> Servers { get; set; } = new();
}

/// <summary>
/// 目录服务的 HTTP 入口（暴露 /api/routing/query）。
/// 注意：这里是健康/HTTP 端口（默认 5101），不是 TCP 游戏端口（5500）。
/// </summary>
public sealed class DirectoryOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5101";
    public string? Token { get; set; }
}

/// <summary>
/// 管理命令入口（任意服务的 /admin/command，默认指向目录服务的 HTTP 端口）。
/// </summary>
public sealed class AdminOptions
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:5101";
    public string? Token { get; set; }
}

public sealed class ServerEndpoint
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>TCP 游戏端口（仅用于展示）。</summary>
    public int TcpPort { get; set; }

    /// <summary>健康检查 HTTP 端口（/health 所在端口，与 scripts/servers.json 的 healthPort 对应）。</summary>
    public int HealthPort { get; set; }
}

public sealed record ServerHealthDto(bool Ok, int StatusCode, long LatencyMs, string? Error);

public sealed record ServerStatusDto(string Id, string Type, string Region, string Host, int TcpPort, int HealthPort,
    ServerHealthDto Health);
