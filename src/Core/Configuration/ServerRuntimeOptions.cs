using System.Net;

namespace Core.Configuration;

/// <summary>
/// 服务器启动后使用的不可变配置快照。
/// </summary>
/// <remarks>
/// <see cref="ServerOptions"/> 面向 JSON、环境变量和命令行绑定，因此保留可写属性；
/// 本类型负责一次性完成默认值修正和地址解析，避免网络热路径反复读取、修正可变配置。
/// </remarks>
internal sealed class ServerRuntimeOptions
{
    private ServerRuntimeOptions(ServerOptions source, IPAddress listenAddress)
    {
        ListenAddress = listenAddress;
        NoDelay = source.NoDelay;
        MaxMessageSizeBytes = Math.Max(1024, source.MaxMessageSizeBytes);
        MaxConcurrentConnections = Math.Max(1, source.MaxConcurrentConnections);
        ConnectionQueueCapacity = Math.Max(1, source.ConnectionQueueCapacity);
        MaxConcurrentRequestsPerConnection = Math.Clamp(source.MaxConcurrentRequestsPerConnection, 1, 64);
        ReceiveTimeoutMs = Math.Max(0, source.ReceiveTimeoutMs);
        AcceptBackoffMs = Math.Max(0, source.AcceptBackoffMs);
        RequestTimeoutMs = Math.Max(0, source.RequestTimeoutMs);
        StartupTimeoutMs = Math.Max(1_000, source.StartupTimeoutMs);
        ShutdownTimeoutMs = Math.Max(1_000, source.ShutdownTimeoutMs);
        ListenBacklog = Math.Max(1, source.ListenBacklog);
        MaxMessageTypeLength = Math.Max(1, source.MaxMessageTypeLength);
        MaxRequestIdLength = Math.Max(1, source.MaxRequestIdLength);
        MaxTokenLength = Math.Max(1, source.MaxTokenLength);
        MaxVersionLength = Math.Max(1, source.MaxVersionLength);
        AllowUnspecifiedChannel = source.AllowUnspecifiedChannel;
        MaxBytesPerSecond = Math.Max(1, source.MaxBytesPerSecond);
        MaxMessagesPerSecond = Math.Max(1, source.MaxMessagesPerSecond);
        IdleTimeoutMs = Math.Max(0, source.IdleTimeoutMs);
        IpMaxBytesPerSecond = Math.Max(0, source.IpMaxBytesPerSecond);
        IpMaxMessagesPerSecond = Math.Max(0, source.IpMaxMessagesPerSecond);

        var tls = source.Tls ?? new TlsOptions();
        Tls = new TlsOptions
        {
            Enabled = tls.Enabled,
            CertificatePath = tls.CertificatePath,
            CertificatePassword = tls.CertificatePassword,
            RequireClientCertificate = tls.RequireClientCertificate,
            HandshakeTimeoutMs = Math.Max(1_000, tls.HandshakeTimeoutMs),
            CheckClientCertificateRevocation = tls.CheckClientCertificateRevocation
        };
    }

    public IPAddress ListenAddress { get; }
    public bool NoDelay { get; }
    public int MaxMessageSizeBytes { get; }
    public int MaxConcurrentConnections { get; }
    public int ConnectionQueueCapacity { get; }
    public int MaxConcurrentRequestsPerConnection { get; }
    public int ReceiveTimeoutMs { get; }
    public int AcceptBackoffMs { get; }
    public int RequestTimeoutMs { get; }
    public int StartupTimeoutMs { get; }
    public int ShutdownTimeoutMs { get; }
    public int ListenBacklog { get; }
    public int MaxMessageTypeLength { get; }
    public int MaxRequestIdLength { get; }
    public int MaxTokenLength { get; }
    public int MaxVersionLength { get; }
    public bool AllowUnspecifiedChannel { get; }
    public int MaxBytesPerSecond { get; }
    public int MaxMessagesPerSecond { get; }
    public int IdleTimeoutMs { get; }
    public int IpMaxBytesPerSecond { get; }
    public int IpMaxMessagesPerSecond { get; }
    public TlsOptions Tls { get; }

    /// <summary>
    /// 从外部配置创建运行时快照，并尽早拒绝无法监听的 IP 地址。
    /// </summary>
    public static ServerRuntimeOptions Create(ServerOptions? source, string parameterName)
    {
        var settings = source ?? new ServerOptions();
        if (!IPAddress.TryParse(settings.ListenAddress, out var listenAddress))
            throw new ArgumentException($"监听地址不是有效 IP: {settings.ListenAddress}", parameterName);

        return new ServerRuntimeOptions(settings, listenAddress);
    }
}
