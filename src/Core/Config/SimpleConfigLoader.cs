using System.Text.Json;
using Shared.Constants;

namespace Core.Config;

/// <summary>
/// 简易配置加载器：从 appsettings/config.json 与环境变量读取服务配置。
/// </summary>
public static class SimpleConfigLoader
{
    private const string DefaultFileName = "config.json";

    /// <summary>
    /// 按优先级枚举配置文件路径。
    /// 先读取 appsettings，再兼容旧版 config.json。
    /// </summary>
    private static IEnumerable<string> EnumerateConfigPaths(string dir)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var envName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                      ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                      ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(envName))
        {
            var envPath = Path.Combine(dir, $"appsettings.{envName}.json");
            if (yielded.Add(envPath))
            {
                yield return envPath;
            }
        }

        var appsettingsPath = Path.Combine(dir, "appsettings.json");
        if (yielded.Add(appsettingsPath))
        {
            yield return appsettingsPath;
        }

        var legacyPath = Path.Combine(dir, DefaultFileName);
        if (yielded.Add(legacyPath))
        {
            yield return legacyPath;
        }
    }

    /// <summary>
    /// 加载目录服务的令牌签名密钥：环境变量优先，其次配置文件。
    /// </summary>
    public static string? LoadDirectoryTokenSecret(string? baseDirectory = null)
    {
        var env = Environment.GetEnvironmentVariable(ConfigKeys.DirectoryTokenSecretEnv);
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var dir = baseDirectory ?? AppContext.BaseDirectory;
        try
        {
            foreach (var path in EnumerateConfigPaths(dir))
            {
                var sec = TryReadDirectoryTokenSecretFromJson(path);
                if (!string.IsNullOrWhiteSpace(sec)) return sec;
            }
        }
        catch (Exception)
        {
            // 保持容错：读取失败时继续尝试下一个来源。
        }

        return null;
    }

    private static string? TryReadDirectoryTokenSecretFromJson(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;
        if (root.TryGetProperty("directory", out var directory) &&
            directory.ValueKind == JsonValueKind.Object &&
            directory.TryGetProperty("tokenSecret", out var secProp) &&
            secProp.ValueKind == JsonValueKind.String)
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
        public string DirectoryHost { get; set; } = "127.0.0.1";
        public int DirectoryPort { get; set; } = 5500;
        public string Region { get; set; } = "default";
        public int Capacity { get; set; } = 100;
        public int IntervalSeconds { get; set; } = 5;
        public string Host { get; set; } = "127.0.0.1";
    }

    /// <summary>
    /// 加载心跳配置。
    /// </summary>
    public static HeartbeatSettings LoadHeartbeatSettings(string? baseDirectory = null)
    {
        var settings = new HeartbeatSettings();
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        try
        {
            foreach (var path in EnumerateConfigPaths(dir))
            {
                if (!File.Exists(path)) continue;

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
        }
        catch (Exception)
        {
            // 读取或解析失败时保留默认值。
        }

        return settings;
    }

    /// <summary>
    /// 服务运行参数设置。
    /// </summary>
    public class ServerSettings
    {
        public int MaxMessageSizeBytes { get; set; } = 1 * 1024 * 1024;
        public int MaxConcurrentConnections { get; set; } = 200;
        public int ReceiveTimeoutMs { get; set; } = 10000;
        public int AcceptBackoffMs { get; set; } = 200;
    }

    /// <summary>
    /// 加载服务运行参数：环境变量优先，其次配置文件。
    /// </summary>
    public static ServerSettings LoadServerSettings(string? baseDirectory = null)
    {
        var settings = new ServerSettings();

        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerMaxMessageSizeEnv), out var maxMsg) && maxMsg > 0)
            settings.MaxMessageSizeBytes = maxMsg;
        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerMaxConnectionsEnv), out var maxConn) && maxConn > 0)
            settings.MaxConcurrentConnections = maxConn;
        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerReceiveTimeoutMsEnv), out var rcvTo) && rcvTo > 0)
            settings.ReceiveTimeoutMs = rcvTo;
        if (int.TryParse(Environment.GetEnvironmentVariable(ConfigKeys.ServerAcceptBackoffMsEnv), out var backoff) && backoff >= 0)
            settings.AcceptBackoffMs = backoff;

        var dir = baseDirectory ?? AppContext.BaseDirectory;
        try
        {
            foreach (var path in EnumerateConfigPaths(dir))
            {
                if (!File.Exists(path)) continue;

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
        }
        catch (Exception)
        {
            // 读取或解析失败时保留默认值。
        }

        return settings;
    }
}
