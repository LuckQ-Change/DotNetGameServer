using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;

using DotNetGameServer;

using Google.Protobuf;

var opt = Options.Parse(args);
if (opt.ShowHelp)
{
    Console.WriteLine(Options.HelpText());
    return;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
if (opt.DurationSeconds > 0) cts.CancelAfter(TimeSpan.FromSeconds(opt.DurationSeconds));

var stats = new Stats();
var started = Stopwatch.StartNew();

var workers = new List<Task>(opt.Users);
for (var i = 0; i < opt.Users; i++)
{
    var idx = i;
    workers.Add(Task.Run(async () =>
    {
        var delayMs = opt.Users <= 1 ? 0 : (int)(idx * (opt.RampSeconds * 1000.0 / opt.Users));
        if (delayMs > 0) await Task.Delay(delayMs, cts.Token);

        var userId = $"{opt.UserPrefix}{idx:D6}";
        try
        {
            if (opt.DoLogin)
            {
                var loginReq = new c2s_login { Username = opt.Username, Password = opt.Password };
                var login = await ProtobufEnvelopeClient.SendAsync<c2s_login, s2c_login>(
                    opt.LoginHost, opt.LoginPort, loginReq, token: null, timeoutMs: opt.TimeoutMs, cts.Token);
                stats.Login.RecordOk(login.LatencyMs);
            }

            var qReq = new c2s_query_servers { Strategy = opt.Strategy, Region = opt.Region, Limit = opt.QueryLimit };
            var q = await ProtobufEnvelopeClient.SendAsync<c2s_query_servers, s2c_query_servers>(
                opt.DirectoryHost, opt.DirectoryPort, qReq, token: null, timeoutMs: opt.TimeoutMs, cts.Token);
            stats.Query.RecordOk(q.LatencyMs);

            var picked = q.Response.Servers.FirstOrDefault();
            if (picked == null)
            {
                stats.Switch.RecordFail("no_server");
                return;
            }

            var swReq = new c2s_switch_server { ServerId = picked.Id, UserId = userId };
            var sw = await ProtobufEnvelopeClient.SendAsync<c2s_switch_server, s2c_switch_server>(
                opt.DirectoryHost, opt.DirectoryPort, swReq, token: null, timeoutMs: opt.TimeoutMs, cts.Token);
            if (!sw.Response.Success)
            {
                stats.Switch.RecordFail(sw.Response.Message);
                return;
            }

            stats.Switch.RecordOk(sw.LatencyMs);

            if (!opt.ConnectLogic) return;

            using var client = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(opt.TimeoutMs));
            await client.ConnectAsync(sw.Response.Host, sw.Response.Port, timeoutCts.Token);
            stats.LogicConnect.RecordOk(0);

            if (opt.HoldSeconds > 0)
                await Task.Delay(TimeSpan.FromSeconds(opt.HoldSeconds), cts.Token);
            else
                await Task.Delay(Timeout.InfiniteTimeSpan, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            stats.Errors.Enqueue(ex.GetType().Name + ":" + ex.Message);
        }
    }, cts.Token));
}

var reporter = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
        Console.WriteLine(stats.Render(started.Elapsed));
    }
}, cts.Token);

try
{
    await Task.WhenAll(workers);
}
catch (OperationCanceledException)
{
    // 忽略
}

cts.Cancel();
try
{
    await reporter;
}
catch
{
}

Console.WriteLine(stats.Render(started.Elapsed));
Console.WriteLine($"errors_sample_count={stats.Errors.Count}");

static class ProtobufEnvelopeClient
{
    public static async Task<RpcResult<TResp>> SendAsync<TReq, TResp>(string host, int port, TReq req, string? token,
        int timeoutMs, CancellationToken ct)
        where TReq : class, IMessage<TReq>
        where TResp : class, IMessage<TResp>, new()
    {
        using var tcp = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (timeoutMs > 0) timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
        await tcp.ConnectAsync(host, port, timeoutCts.Token);

        await using var stream = tcp.GetStream();

        var env = new envelope
        {
            Type = req.Descriptor.Name,
            Payload = ByteString.CopyFrom(req.ToByteArray()),
            RequestId = Guid.NewGuid().ToString("N")
        };
        if (!string.IsNullOrWhiteSpace(token)) env.Token = token;

        var buf = env.ToByteArray();
        var lenBuf = BitConverter.GetBytes(buf.Length);

        var sw = Stopwatch.StartNew();
        await stream.WriteAsync(lenBuf.AsMemory(0, 4), timeoutCts.Token);
        await stream.WriteAsync(buf.AsMemory(), timeoutCts.Token);
        await stream.FlushAsync(timeoutCts.Token);

        var respLenBuf = new byte[4];
        await ReadExactAsync(stream, respLenBuf, timeoutCts.Token);
        var respLen = BitConverter.ToInt32(respLenBuf, 0);
        if (respLen <= 0 || respLen > 8 * 1024 * 1024)
            throw new InvalidOperationException($"invalid_resp_len={respLen}");
        var respBuf = new byte[respLen];
        await ReadExactAsync(stream, respBuf, timeoutCts.Token);
        sw.Stop();

        var respEnv = envelope.Parser.ParseFrom(respBuf);
        if (respEnv.ErrorCode != 0)
            throw new InvalidOperationException($"error_code={respEnv.ErrorCode} msg={respEnv.ErrorMessage}");

        var parser = new MessageParser<TResp>(() => new TResp());
        var resp = parser.ParseFrom(respEnv.Payload);
        return new RpcResult<TResp>(resp, sw.ElapsedMilliseconds);
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buf, CancellationToken ct)
    {
        var off = 0;
        while (off < buf.Length)
        {
            var n = await stream.ReadAsync(buf.AsMemory(off, buf.Length - off), ct);
            if (n == 0) throw new IOException("remote_closed");
            off += n;
        }
    }
}

sealed record RpcResult<T>(T Response, long LatencyMs);

sealed class Options
{
    private const string DefaultHost = "127.0.0.1";
    public bool ShowHelp { get; init; }
    public int Users { get; init; } = 100;
    public int RampSeconds { get; init; } = 10;
    public int DurationSeconds { get; init; } = 0;
    public int HoldSeconds { get; init; } = 60;
    public int TimeoutMs { get; init; } = 3000;

    public bool DoLogin { get; init; } = false;
    public string Username { get; init; } = "test";
    public string Password { get; init; } = "password";
    public string LoginHost { get; init; } = DefaultHost;
    public int LoginPort { get; init; } = 5501;

    public string DirectoryHost { get; init; } = DefaultHost;
    public int DirectoryPort { get; init; } = 5500;
    public string Strategy { get; init; } = "load";
    public string Region { get; init; } = "";
    public int QueryLimit { get; init; } = 1;

    public bool ConnectLogic { get; init; } = true;
    public string UserPrefix { get; init; } = "user_";

    public static Options Parse(string[] args)
    {
        bool Flag(string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        string? Get(string name)
        {
            var idx = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || idx + 1 >= args.Length) return null;
            return args[idx + 1];
        }

        int GetInt(string name, int def)
        {
            var s = Get(name);
            return int.TryParse(s, out var v) ? v : def;
        }

        return new Options
        {
            ShowHelp = Flag("--help") || Flag("-h"),
            Users = GetInt("--users", 100),
            RampSeconds = GetInt("--ramp", 10),
            DurationSeconds = GetInt("--duration", 0),
            HoldSeconds = GetInt("--hold", 60),
            TimeoutMs = GetInt("--timeoutMs", 3000),

            DoLogin = Flag("--login"),
            Username = Get("--username") ?? "test",
            Password = Get("--password") ?? "password",
            LoginHost = Get("--loginHost") ?? DefaultHost,
            LoginPort = GetInt("--loginPort", 5501),

            DirectoryHost = Get("--directoryHost") ?? DefaultHost,
            DirectoryPort = GetInt("--directoryPort", 5500),
            Strategy = Get("--strategy") ?? "load",
            Region = Get("--region") ?? "",
            QueryLimit = GetInt("--queryLimit", 1),

            ConnectLogic = !Flag("--noLogic"),
            UserPrefix = Get("--userPrefix") ?? "user_"
        };
    }

    public static string HelpText()
    {
        return """
               LoadTester

               Examples:
                 dotnet run --project tools/LoadTester -- --users 100 --ramp 10 --hold 60
                 dotnet run --project tools/LoadTester -- --users 200 --ramp 20 --hold 120 --login
                 dotnet run --project tools/LoadTester -- --users 200 --ramp 10 --hold 0 --noLogic

               Args:
                 --users <n>            虚拟玩家数
                 --ramp <sec>           渐进启动时间（秒）
                 --hold <sec>           连上逻辑服后保持连接时间（秒）；0=直到 Ctrl+C/--duration
                 --duration <sec>       总运行时间（秒）；0=直到 Ctrl+C
                 --timeoutMs <ms>       单次请求超时

                 --login                先压登录服（使用 username/password 发 c2s_login）
                 --username <u>
                 --password <p>
                 --loginHost <host>
                 --loginPort <port>

                 --directoryHost <host>
                 --directoryPort <port>
                 --strategy <load|latency|weighted>
                 --region <region>
                 --queryLimit <n>

                 --noLogic              不连接逻辑服，只压 Directory 的 query/switch
                 --userPrefix <prefix>
               """;
    }
}

sealed class Metric
{
    private long _ok;
    private long _fail;
    private long _latSum;
    private long _latMax;
    private readonly ConcurrentQueue<long> _recent = new();

    public void RecordOk(long latencyMs)
    {
        Interlocked.Increment(ref _ok);
        Interlocked.Add(ref _latSum, latencyMs);
        InterlockedExtensions.Max(ref _latMax, latencyMs);
        _recent.Enqueue(latencyMs);
        while (_recent.Count > 2000 && _recent.TryDequeue(out _))
        {
        }
    }

    public void RecordFail(string _)
    {
        Interlocked.Increment(ref _fail);
    }

    public string Render(string name)
    {
        var ok = Interlocked.Read(ref _ok);
        var fail = Interlocked.Read(ref _fail);
        var latSum = Interlocked.Read(ref _latSum);
        var latMax = Interlocked.Read(ref _latMax);
        var avg = ok > 0 ? latSum * 1.0 / ok : 0;
        var p95 = Percentile(0.95);
        return $"{name}: ok={ok} fail={fail} avg={avg:F1}ms p95={p95}ms max={latMax}ms";
    }

    private long Percentile(double p)
    {
        var arr = _recent.ToArray();
        if (arr.Length == 0) return 0;
        Array.Sort(arr);
        var idx = (int)Math.Clamp(Math.Ceiling(p * arr.Length) - 1, 0, arr.Length - 1);
        return arr[idx];
    }
}

sealed class Stats
{
    public Metric Login { get; } = new();
    public Metric Query { get; } = new();
    public Metric Switch { get; } = new();
    public Metric LogicConnect { get; } = new();
    public ConcurrentQueue<string> Errors { get; } = new();

    public string Render(TimeSpan elapsed)
    {
        var t = (int)elapsed.TotalSeconds;
        return
            $"t={t}s | {Login.Render("login")} | {Query.Render("query")} | {Switch.Render("switch")} | {LogicConnect.Render("logic")}";
    }
}

static class InterlockedExtensions
{
    public static void Max(ref long target, long value)
    {
        while (true)
        {
            var cur = Interlocked.Read(ref target);
            if (value <= cur) return;
            if (Interlocked.CompareExchange(ref target, value, cur) == cur) return;
        }
    }
}
