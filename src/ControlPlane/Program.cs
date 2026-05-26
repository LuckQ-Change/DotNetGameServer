using System.Net.Http.Headers;
using System.Text;

using ControlPlane;

using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddOptions<ControlPlaneOptions>()
    .Bind(builder.Configuration.GetSection("ControlPlane"))
    .Validate(o => o.Servers.Count > 0, "ControlPlane:Servers 不能为空");

var app = builder.Build();

const string JsonContentType = "application/json";

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// 逐个探测各服务的 /health（HealthPort），聚合为 { total, healthy, items }。
// 单个服务探测失败不影响整体——始终返回 200，前端才能渲染其余面板。
app.MapGet("/api/servers", async (IHttpClientFactory httpClientFactory,
    IOptions<ControlPlaneOptions> options, CancellationToken ct) =>
{
    var servers = options.Value.Servers;
    var http = httpClientFactory.CreateClient();
    http.Timeout = TimeSpan.FromMilliseconds(1500);

    var tasks = servers.Select(async s =>
    {
        var url = $"http://{s.Host}:{s.HealthPort}/health";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var resp = await http.GetAsync(url, ct);
            sw.Stop();
            return new ServerStatusDto(s.Id, s.Type, s.Region, s.Host, s.TcpPort, s.HealthPort,
                new ServerHealthDto(resp.IsSuccessStatusCode, (int)resp.StatusCode, sw.ElapsedMilliseconds, null));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServerStatusDto(s.Id, s.Type, s.Region, s.Host, s.TcpPort, s.HealthPort,
                new ServerHealthDto(false, 0, sw.ElapsedMilliseconds, ex.Message));
        }
    }).ToArray();

    var items = await Task.WhenAll(tasks);
    return Results.Ok(new { total = items.Length, healthy = items.Count(i => i.Health.Ok), items });
});

// 代理目录服务的 HTTP 路由查询。目录不可达时返回空列表（200），
// 否则前端 refresh() 的 Promise.all 会整体失败，连服务健康面板也一并空白。
app.MapGet("/api/routing/query", async (string? strategy, string? region, int? limit,
    IHttpClientFactory httpClientFactory, IOptions<ControlPlaneOptions> options, CancellationToken ct) =>
{
    var opt = options.Value;
    var query = new List<string> { $"strategy={Uri.EscapeDataString(strategy ?? "load")}" };
    if (!string.IsNullOrWhiteSpace(region)) query.Add($"region={Uri.EscapeDataString(region)}");
    if (limit is > 0) query.Add($"limit={limit}");
    var url = $"{opt.Directory.BaseUrl.TrimEnd('/')}/api/routing/query?{string.Join('&', query)}";

    var http = httpClientFactory.CreateClient();
    http.Timeout = TimeSpan.FromMilliseconds(2000);
    try
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonContentType));
        if (!string.IsNullOrWhiteSpace(opt.Directory.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.Directory.Token);
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode) return Results.Content(body, JsonContentType);
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[ControlPlane] routing query failed: {ex.Message}");
    }

    return Results.Ok(new { count = 0, servers = Array.Empty<object>(), strategy = strategy ?? "load" });
});

// 通过任意服务的 /admin/command 拉取 GM 指令清单（执行内置 "commands" 指令）。
app.MapGet("/api/gm/commands", (IHttpClientFactory httpClientFactory,
    IOptions<ControlPlaneOptions> options, CancellationToken ct) =>
    ProxyAdminCommandAsync(httpClientFactory, options.Value, "{\"cmd\":\"commands\"}", ct));

// 执行任意 GM 指令，原样转发请求体（{ "cmd": "..." }）到 /admin/command。
app.MapPost("/api/gm/execute", async (HttpContext ctx, IHttpClientFactory httpClientFactory,
    IOptions<ControlPlaneOptions> options, CancellationToken ct) =>
{
    using var sr = new StreamReader(ctx.Request.Body);
    var payload = await sr.ReadToEndAsync(ct);
    return await ProxyAdminCommandAsync(httpClientFactory, options.Value, payload, ct);
});

app.MapFallbackToFile("index.html");

app.Run();
return;

static async Task<IResult> ProxyAdminCommandAsync(IHttpClientFactory httpClientFactory, ControlPlaneOptions opt,
    string payload, CancellationToken ct)
{
    var url = opt.Admin.BaseUrl.TrimEnd('/') + "/admin/command";
    var http = httpClientFactory.CreateClient();
    http.Timeout = TimeSpan.FromMilliseconds(3000);
    try
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(opt.Admin.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", opt.Admin.Token);
        using var resp = await http.SendAsync(req, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (resp.IsSuccessStatusCode && !string.IsNullOrWhiteSpace(body))
            return Results.Content(body, "application/json");
        return Results.Json(new { ok = false, error = $"admin http {(int)resp.StatusCode}" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message });
    }
}
