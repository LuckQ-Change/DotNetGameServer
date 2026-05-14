namespace ControlPlane;

public sealed class ControlPlaneOptions
{
    public DirectoryOptions Directory { get; set; } = new();
    public AdminOptions Admin { get; set; } = new();
    public List<ServerEndpoint> Servers { get; set; } = new();
}

public sealed class DirectoryOptions
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5500;
    public string? Token { get; set; }
}

public sealed class AdminOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5101";
    public string? Token { get; set; }
}

public sealed class ServerEndpoint
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; }
}
