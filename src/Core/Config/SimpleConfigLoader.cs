using System.Text.Json;
using Shared.Constants;

namespace Core.Config;

/// <summary>
/// 简易配置加载器：从 <c>config.json</c> 与环境变量读取服务配置。
/// </summary>
public static class SimpleConfigLoader
{
    private const string DefaultFileName = "config.json";

    /// <summary>
    /// 加载目录服务器的令牌签名密钥：环境变量优先，其次配置文件。
    /// </summary>
    /// <param name="baseDirectory">配置文件基准目录，默认应用程序目录。</param>
    /// <returns>密钥字符串；未配置时返回 <c>null</c>。</returns>
    public static string? LoadDirectoryTokenSecret(string? baseDirectory = null)
    {
        // 优先环境变量
        var env = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryTokenSecretEnv);
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var dir = baseDirectory ?? AppContext.BaseDirectory;
        // 其次 appsettings.{Environment}.json / appsettings.json
        try
        {
            var envName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                          ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                          ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(envName))
            {
                var appEnv = Path.Combine(dir, $"appsettings.{envName}.json");
                var sec = TryReadDirectoryTokenSecretFromJson(appEnv);
                if (!string.IsNullOrWhiteSpace(sec)) return sec;
            }

            var appDefault = Path.Combine(dir, "appsettings.json");
            {
                var sec = TryReadDirectoryTokenSecretFromJson(appDefault);
                if (!string.IsNullOrWhiteSpace(sec)) return sec;
            }
        }
        catch (Exception)
        {
            // 保持容错：读取失败时继续尝试下一个来源。
        }

        // 最后兼容读取 config.json（旧配置文件名）
        try
        {
            var path = Path.Combine(dir, DefaultFileName);
            var sec = TryReadDirectoryTokenSecretFromJson(path);
            if (!string.IsNullOrWhiteSpace(sec)) return sec;
        }
        catch (Exception)
        {
            // 忽略
        }

        return null;
    }

    private static string? TryReadDirectoryTokenSecretFromJson(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.TryGetProperty("directory", out var directory) && directory.ValueKind == JsonValueKind.Object
            && directory.TryGetProperty("tokenSecret", out var secProp) && secProp.ValueKind == JsonValueKind.String)
        {
            var sec = secProp.GetString();
            if (!string.IsNullOrWhiteSpace(sec)) return sec;
        }
        return null;
    }

    /// <summary>
    /// 心跳参数设置。
    /// </summary>
    public class HeartbeatSettings
    {
        /// <summary>目录服务器主机。</summary>
        public string DirectoryHost { get; set; } = "127.0.0.1";
        /// <summary>目录服务器端口。</summary>
        public int DirectoryPort { get; set; } = 5500;
        /// <summary>服务区域名。</summary>
        public string Region { get; set; } = "default";
        /// <summary>服务容量（玩家上限）。</summary>
        public int Capacity { get; set; } = 100;
        /// <summary>心跳间隔秒数。</summary>
        public int IntervalSeconds { get; set; } = 5;
        /// <summary>服务对外主机显示。</summary>
        public string Host { get; set; } = "127.0.0.1";
    }

    /// <summary>
    /// 加载心跳配置：从配置文件读取 <c>directory</c> 与 <c>heartbeat</c> 段。
    /// </summary>
    /// <param name="baseDirectory">配置文件基准目录，默认应用程序目录。</param>
    /// <returns>心跳配置对象。</returns>
    public static HeartbeatSettings LoadHeartbeatSettings(string? baseDirectory = null)
    {
        var settings = new HeartbeatSettings();
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, DefaultFileName);
        if (!File.Exists(path)) return settings;
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.TryGetProperty("directory", out var directory) && directory.ValueKind == JsonValueKind.Object)
            {
                if (directory.TryGetProperty("host", out var hostProp) && hostProp.ValueKind == JsonValueKind.String)
                    settings.DirectoryHost = hostProp.GetString() ?? settings.DirectoryHost;
                if (directory.TryGetProperty("port", out var portProp) && portProp.ValueKind == JsonValueKind.Number)
                    settings.DirectoryPort = portProp.GetInt32();
            }
            if (root.TryGetProperty("heartbeat", out var hb) && hb.ValueKind == JsonValueKind.Object)
            {
                if (hb.TryGetProperty("region", out var rProp) && rProp.ValueKind == JsonValueKind.String)
                    settings.Region = rProp.GetString() ?? settings.Region;
                if (hb.TryGetProperty("capacity", out var cProp) && cProp.ValueKind == JsonValueKind.Number)
                    settings.Capacity = cProp.GetInt32();
                if (hb.TryGetProperty("intervalSeconds", out var iProp) && iProp.ValueKind == JsonValueKind.Number)
                    settings.IntervalSeconds = iProp.GetInt32();
                if (hb.TryGetProperty("host", out var hProp) && hProp.ValueKind == JsonValueKind.String)
                    settings.Host = hProp.GetString() ?? settings.Host;
            }
        }
        catch (Exception)
        {
            // 读取/解析配置文件失败时保留环境变量或默认值。
        }
        return settings;
    }

    /// <summary>
    /// 服务器运行参数设置。
    /// </summary>
    public class ServerSettings
    {
        /// <summary>消息大小上限，默认 1MB。</summary>
        public int MaxMessageSizeBytes { get; set; } = 1 * 1024 * 1024;
        /// <summary>并发连接上限，默认 200。</summary>
        public int MaxConcurrentConnections { get; set; } = 200;
        /// <summary>收发超时毫秒，默认 10000。</summary>
        public int ReceiveTimeoutMs { get; set; } = 10000;
        /// <summary>接受失败退避毫秒，默认 200。</summary>
        public int AcceptBackoffMs { get; set; } = 200;
    }

    /// <summary>
    /// 加载服务器运行参数：环境变量优先，其次配置文件。
    /// </summary>
    /// <param name="baseDirectory">配置文件基准目录，默认应用程序目录。</param>
    /// <returns>服务器运行参数对象。</returns>
    public static ServerSettings LoadServerSettings(string? baseDirectory = null)
    {
        var settings = new ServerSettings();

        // 环境变量优先
        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerMaxMessageSizeEnv), out var maxMsg) && maxMsg > 0)
            settings.MaxMessageSizeBytes = maxMsg;
        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerMaxConnectionsEnv), out var maxConn) && maxConn > 0)
            settings.MaxConcurrentConnections = maxConn;
        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerReceiveTimeoutMsEnv), out var rcvTo) && rcvTo > 0)
            settings.ReceiveTimeoutMs = rcvTo;
        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerAcceptBackoffMsEnv), out var backoff) && backoff >= 0)
            settings.AcceptBackoffMs = backoff;

        var dir = baseDirectory ?? AppContext.BaseDirectory;
        var path = Path.Combine(dir, DefaultFileName);
        if (!File.Exists(path)) return settings;

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.TryGetProperty("server", out var server) && server.ValueKind == JsonValueKind.Object)
            {
                if (server.TryGetProperty("maxMessageSizeBytes", out var mProp) && mProp.ValueKind == JsonValueKind.Number)
                    settings.MaxMessageSizeBytes = mProp.GetInt32();
                if (server.TryGetProperty("maxConcurrentConnections", out var cProp) && cProp.ValueKind == JsonValueKind.Number)
                    settings.MaxConcurrentConnections = cProp.GetInt32();
                if (server.TryGetProperty("receiveTimeoutMs", out var tProp) && tProp.ValueKind == JsonValueKind.Number)
                    settings.ReceiveTimeoutMs = tProp.GetInt32();
                if (server.TryGetProperty("acceptBackoffMs", out var aProp) && aProp.ValueKind == JsonValueKind.Number)
                    settings.AcceptBackoffMs = aProp.GetInt32();
            }
        }
        catch (Exception)
        {
            // 读取/解析配置文件失败时保留环境变量或默认值。
        }

        return settings;
    }
}
