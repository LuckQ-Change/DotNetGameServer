namespace ControlPlane;

public sealed class ControlPlaneOptions
{
    public List<ServerEndpoint> Servers { get; set; } = new();
}

public sealed class ServerEndpoint
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int TcpPort { get; set; }
}
