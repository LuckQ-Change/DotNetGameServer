using ControlPlane;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<DirectoryClient>();
builder.Services.AddOptions<ControlPlane.ControlPlaneOptions>()
    .Bind(builder.Configuration.GetSection("ControlPlane"))
    .Validate(o => o.Servers.Count > 0, "ControlPlane:Servers 不能为空");
var app = builder.Build();

const string JsonContentType = "application/json";

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/api/servers", async (IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<ControlPlane.ControlPlaneOptions> options, CancellationToken ct) =>
{
    var servers = options.Value.Servers;
    var http = httpClientFactory.CreateClient();
    http.Timeout = TimeSpan.FromMilliseconds(1500);

    var tasks = servers.Select(async s =>
    {
        var healthPort = s.TcpPort + 10000;
        var url = $"http://{s.Host}:{healthPort}/health";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var resp = await http.GetAsync(url, ct);
            sw.Stop();
            return new ServerStatusDto(
                s.Id,
                s.Type,
                s.Region,
                s.Host,
                s.TcpPort,
                healthPort,
                new ServerHealthDto(resp.IsSuccessStatusCode, (int)resp.StatusCode, sw.ElapsedMilliseconds, null));
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ServerStatusDto(
                s.Id,
                s.Type,
                s.Region,
                s.Host,
                s.TcpPort,
                healthPort,
                new ServerHealthDto(false, 0, sw.ElapsedMilliseconds, ex.Message));
        }
    }).ToArray();

    var items = await Task.WhenAll(tasks);
    var total = items.Length;
    var healthy = items.Count(i => i.Health.Ok);
    return Results.Ok(new { total, healthy, items });
});

app.MapGet("/api/routing/query", async (string? strategy, string? region, int? limit,
    Microsoft.Extensions.Options.IOptions<ControlPlane.ControlPlaneOptions> options, DirectoryClient client,
    CancellationToken ct) =>
{
    var opt = options.Value;
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(2000));
    var req = new DotNetGameServer.c2s_query_servers
    {
        Strategy = strategy ?? "load",
        Region = region ?? string.Empty,
        Limit = limit.GetValueOrDefault(0)
    };
    var resp = await client.QueryServersAsync(opt.Directory.Host, opt.Directory.Port, req, opt.Directory.Token,
        timeoutCts.Token);
    var servers = resp.Servers.Select(s => new
    {
        id = s.Id,
        type = s.Type,
        region = s.Region,
        host = s.Host,
        port = s.Port,
        playerCount = s.PlayerCount,
        capacity = s.Capacity,
        status = s.Status,
        lastHeartbeat = s.LastHeartbeat,
        avgRttMs = s.AvgRttMs
    }).ToArray();
    return Results.Ok(new { count = servers.Length, servers });
});

app.MapGet("/api/gm/commands", async (IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<ControlPlane.ControlPlaneOptions> options, CancellationToken ct) =>
{
    var opt = options.Value;
    var url = opt.Admin.BaseUrl.TrimEnd('/') + "/admin/command";
    var http = httpClientFactory.CreateClient();
    using var req = new HttpRequestMessage(HttpMethod.Post, url);
    if (!string.IsNullOrWhiteSpace(opt.Admin.Token))
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opt.Admin.Token);
    req.Content = new StringContent("{\"cmd\":\"commands\"}", System.Text.Encoding.UTF8, "application/json");
    using var resp = await http.SendAsync(req, ct);
    var body = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(body, JsonContentType);
});

app.MapPost("/api/gm/execute", async (HttpContext ctx, IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<ControlPlane.ControlPlaneOptions> options, CancellationToken ct) =>
{
    using var sr = new StreamReader(ctx.Request.Body);
    var payload = await sr.ReadToEndAsync(ct);
    var opt = options.Value;
    var url = opt.Admin.BaseUrl.TrimEnd('/') + "/admin/command";
    var http = httpClientFactory.CreateClient();
    using var req = new HttpRequestMessage(HttpMethod.Post, url);
    if (!string.IsNullOrWhiteSpace(opt.Admin.Token))
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opt.Admin.Token);
    req.Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
    using var resp = await http.SendAsync(req, ct);
    var body = await resp.Content.ReadAsStringAsync(ct);
    return Results.Content(body, JsonContentType);
});

app.MapGet("/@vite/{**path}", () => Results.NotFound());

app.MapFallbackToFile("index.html");

app.Run();

record ServerHealthDto(bool Ok, int StatusCode, long LatencyMs, string? Error);
record ServerStatusDto(string Id, string Type, string Region, string Host, int TcpPort, int HealthPort,
    ServerHealthDto Health);
