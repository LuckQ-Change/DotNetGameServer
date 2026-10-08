using System.Text.Json;

namespace Client.Discovery;

/// <summary>读取 <c>/.well-known/dotnet-game-server/services</c>。</summary>
public sealed class HttpServiceDiscoveryProvider : IServiceDiscoveryProvider
{
    private readonly HttpClient _http;
    private readonly IReadOnlyList<string> _bootstrapUrls;
    private readonly bool _disposeHttp;

    public HttpServiceDiscoveryProvider(IEnumerable<string> bootstrapUrls, HttpClient? http = null)
    {
        _bootstrapUrls = bootstrapUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (_bootstrapUrls.Count == 0)
            throw new ArgumentException("至少需要一个发现地址。", nameof(bootstrapUrls));
        _disposeHttp = http is null;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(
        ServiceQuery query,
        CancellationToken cancellationToken)
    {
        query ??= new ServiceQuery();
        Exception? last = null;
        foreach (var bootstrap in _bootstrapUrls)
        {
            var url = $"{bootstrap}/.well-known/dotnet-game-server/services"
                + $"?responsibility={Uri.EscapeDataString(query.Responsibility ?? string.Empty)}"
                + $"&region={Uri.EscapeDataString(query.Region ?? string.Empty)}"
                + $"&limit={Math.Max(1, query.Limit)}";
            try
            {
                await using var stream = await _http.GetStreamAsync(url, cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var fallbackHost = new Uri(bootstrap).Host;
                return ReadServices(document.RootElement, fallbackHost);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                last = exception;
            }
        }

        throw new InvalidOperationException("没有可用的服务发现地址。", last);
    }

    public void DisposeHttp()
    {
        if (_disposeHttp) _http.Dispose();
    }

    private static IReadOnlyList<ServiceEndpoint> ReadServices(JsonElement root, string fallbackHost)
    {
        if (!root.TryGetProperty("services", out var services) || services.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<ServiceEndpoint>();
        foreach (var item in services.EnumerateArray())
        {
            var host = ReadString(item, "host");
            result.Add(new ServiceEndpoint
            {
                ServiceId = ReadString(item, "serviceId"),
                Role = ReadString(item, "role"),
                Host = string.IsNullOrWhiteSpace(host) || host is "0.0.0.0" or "::" ? fallbackHost : host,
                Port = ReadInt(item, "port"),
                Region = ReadString(item, "region"),
                Version = ReadString(item, "version"),
                PlayerCount = ReadInt(item, "playerCount"),
                Capacity = ReadInt(item, "capacity"),
                TlsEnabled = item.TryGetProperty("tlsEnabled", out var tls) && tls.ValueKind == JsonValueKind.True,
                TlsServerName = ReadString(item, "tlsServerName"),
                Responsibilities = ReadStringArray(item, "responsibilities"),
                HeartbeatIntervalSeconds = ReadInt(item, "heartbeatIntervalSeconds")
            });
        }

        return result;
    }

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : 0;

    private static string[] ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        return value.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => item.Length > 0)
            .ToArray();
    }
}
