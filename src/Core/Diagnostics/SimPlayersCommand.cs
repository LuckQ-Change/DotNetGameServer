using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using DotNetGameServer;
using Google.Protobuf;

namespace Core.Diagnostics;

public sealed class SimPlayersCommand : ICommand
{
    public string Name => "sim";

    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length == 0)
            return Task.FromResult<object?>(new { ok = false, usage = Usage() });

        var sub = args[0].ToLowerInvariant();
        if (sub == "start")
        {
            var opt = SimOptions.Parse(args.Skip(1).ToArray());
            if (!opt.IsOk) return Task.FromResult<object?>(new { ok = false, error = opt.Error, usage = Usage() });
            var job = SimJobManager.Start(opt.Value!, context);
            return Task.FromResult<object?>(new { ok = true, jobId = job.JobId, startedAt = job.StartedAtUtc.ToString("O") });
        }

        if (sub == "status")
        {
            var jobId = args.Length >= 2 ? args[1] : null;
            return Task.FromResult<object?>(SimJobManager.Status(jobId));
        }

        if (sub == "stop")
        {
            if (args.Length < 2) return Task.FromResult<object?>(new { ok = false, error = "缺少 jobId", usage = Usage() });
            return Task.FromResult<object?>(SimJobManager.Stop(args[1]));
        }

        return Task.FromResult<object?>(new { ok = false, error = "未知子命令", usage = Usage() });
    }

    private static object Usage()
    {
        return new
        {
            start = "sim start --users 100 --ramp 10 --hold 60 --host 127.0.0.1 --loginPort 5501 --directoryPort 5500 --logicId logic:5503 --battleId battle:5502",
            status = "sim status [jobId]",
            stop = "sim stop <jobId>"
        };
    }
}

internal sealed class SimOptions
{
    public int Users { get; init; } = 50;
    public int RampSeconds { get; init; } = 10;
    public int HoldSeconds { get; init; } = 60;
    public int TimeoutMs { get; init; } = 3000;
    public string Host { get; init; } = "127.0.0.1";
    public int LoginPort { get; init; } = 5501;
    public int DirectoryPort { get; init; } = 5500;
    public string Username { get; init; } = "test";
    public string Password { get; init; } = "password";
    public string LogicId { get; init; } = "logic:5503";
    public string BattleId { get; init; } = "battle:5502";

    public static ParseResult<SimOptions> Parse(string[] args)
    {
        int GetInt(string name, int def)
        {
            var idx = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || idx + 1 >= args.Length) return def;
            return int.TryParse(args[idx + 1], out var v) ? v : def;
        }

        string GetStr(string name, string def)
        {
            var idx = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            if (idx < 0 || idx + 1 >= args.Length) return def;
            return args[idx + 1];
        }

        var users = GetInt("--users", 50);
        if (users <= 0) return ParseResult<SimOptions>.Fail("users 必须 > 0");
        var ramp = GetInt("--ramp", 10);
        var hold = GetInt("--hold", 60);
        var timeoutMs = GetInt("--timeoutMs", 3000);
        var host = GetStr("--host", "127.0.0.1");
        var loginPort = GetInt("--loginPort", 5501);
        var dirPort = GetInt("--directoryPort", 5500);
        var username = GetStr("--username", "test");
        var password = GetStr("--password", "password");
        var logicId = GetStr("--logicId", "logic:5503");
        var battleId = GetStr("--battleId", "battle:5502");

        return ParseResult<SimOptions>.Ok(new SimOptions
        {
            Users = users,
            RampSeconds = Math.Max(0, ramp),
            HoldSeconds = hold,
            TimeoutMs = Math.Max(200, timeoutMs),
            Host = host,
            LoginPort = loginPort,
            DirectoryPort = dirPort,
            Username = username,
            Password = password,
            LogicId = logicId,
            BattleId = battleId
        });
    }
}

internal sealed class ParseResult<T>
{
    public bool IsOk { get; }
    public string? Error { get; }
    public T? Value { get; }

    private ParseResult(bool ok, T? value, string? error)
    {
        IsOk = ok;
        Value = value;
        Error = error;
    }

    public static ParseResult<T> Ok(T value) => new(true, value, null);
    public static ParseResult<T> Fail(string error) => new(false, default, error);
}

internal static class SimJobManager
{
    private static readonly ConcurrentDictionary<string, SimJob> Jobs = new(StringComparer.OrdinalIgnoreCase);

    public static SimJob Start(SimOptions opt, ICommandContext context)
    {
        var jobId = Guid.NewGuid().ToString("N");
        var job = new SimJob(jobId, opt);
        Jobs[jobId] = job;
        _ = job.RunAsync(context);
        return job;
    }

    public static object Status(string? jobId)
    {
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            if (!Jobs.TryGetValue(jobId, out var j)) return new { ok = false, error = "job 不存在" };
            return j.Snapshot();
        }

        var list = Jobs.Values.OrderByDescending(x => x.StartedAtUtc).Select(x => x.Summary()).ToArray();
        return new { ok = true, count = list.Length, jobs = list };
    }

    public static object Stop(string jobId)
    {
        if (!Jobs.TryGetValue(jobId, out var job)) return new { ok = false, error = "job 不存在" };
        job.Stop();
        return new { ok = true, jobId };
    }
}

internal sealed class SimJob
{
    public string JobId { get; }
    public DateTime StartedAtUtc { get; } = DateTime.UtcNow;
    private readonly SimOptions _opt;
    private readonly CancellationTokenSource _cts = new();
    private readonly Metric _login = new();
    private readonly Metric _switchLogic = new();
    private readonly Metric _switchBattle = new();
    private readonly Metric _connectLogic = new();
    private readonly Metric _connectBattle = new();
    private long _onlineLogic;
    private long _onlineBattle;
    private readonly ConcurrentQueue<string> _errors = new();

    public SimJob(string jobId, SimOptions opt)
    {
        JobId = jobId;
        _opt = opt;
    }

    public void Stop()
    {
        try { _cts.Cancel(); } catch { }
    }

    public async Task RunAsync(ICommandContext context)
    {
        var token = _cts.Token;
        var tasks = new List<Task>(_opt.Users);
        for (var i = 0; i < _opt.Users; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(async () =>
            {
                var delayMs = _opt.Users <= 1 ? 0 : (int)(idx * (_opt.RampSeconds * 1000.0 / _opt.Users));
                if (delayMs > 0) await Task.Delay(delayMs, token);
                var userId = $"user_{idx:D6}";

                try
                {
                    var login = await EnvelopeRpc.SendAsync<c2s_login, s2c_login>(_opt.Host, _opt.LoginPort,
                        new c2s_login { Username = _opt.Username, Password = _opt.Password }, token: null, _opt.TimeoutMs, token);
                    if (!login.Response.Success) { _login.RecordFail(login.Response.Message); return; }
                    _login.RecordOk(login.LatencyMs);
                    var loginToken = string.IsNullOrWhiteSpace(login.Response.Token) ? null : login.Response.Token;

                    var swLogic = await EnvelopeRpc.SendAsync<c2s_switch_server, s2c_switch_server>(_opt.Host, _opt.DirectoryPort,
                        new c2s_switch_server { ServerId = _opt.LogicId, UserId = userId }, loginToken, _opt.TimeoutMs, token);
                    if (!swLogic.Response.Success) { _switchLogic.RecordFail(swLogic.Response.Message); return; }
                    _switchLogic.RecordOk(swLogic.LatencyMs);

                    var swBattle = await EnvelopeRpc.SendAsync<c2s_switch_server, s2c_switch_server>(_opt.Host, _opt.DirectoryPort,
                        new c2s_switch_server { ServerId = _opt.BattleId, UserId = userId }, loginToken, _opt.TimeoutMs, token);
                    if (!swBattle.Response.Success) { _switchBattle.RecordFail(swBattle.Response.Message); return; }
                    _switchBattle.RecordOk(swBattle.LatencyMs);

                    var logicOnline = false;
                    var battleOnline = false;
                    using var logic = new TcpClient();
                    using var battle = new TcpClient();
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        await ConnectAsync(logic, swLogic.Response.Host, swLogic.Response.Port, _opt.TimeoutMs, token);
                        sw.Stop();
                        _connectLogic.RecordOk(sw.ElapsedMilliseconds);
                        Interlocked.Increment(ref _onlineLogic);
                        logicOnline = true;

                        sw.Restart();
                        await ConnectAsync(battle, swBattle.Response.Host, swBattle.Response.Port, _opt.TimeoutMs, token);
                        sw.Stop();
                        _connectBattle.RecordOk(sw.ElapsedMilliseconds);
                        Interlocked.Increment(ref _onlineBattle);
                        battleOnline = true;

                        var hold = _opt.HoldSeconds;
                        if (hold <= 0)
                            await Task.Delay(Timeout.InfiniteTimeSpan, token);
                        else
                            await Task.Delay(TimeSpan.FromSeconds(hold), token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        if (!logicOnline) _connectLogic.RecordFail(ex.Message);
                        else if (!battleOnline) _connectBattle.RecordFail(ex.Message);
                        else _errors.Enqueue(ex.GetType().Name + ":" + ex.Message);
                    }
                    finally
                    {
                        if (battleOnline) Interlocked.Decrement(ref _onlineBattle);
                        if (logicOnline) Interlocked.Decrement(ref _onlineLogic);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    _errors.Enqueue(ex.GetType().Name + ":" + ex.Message);
                }
            }, token));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public object Summary()
    {
        return new
        {
            jobId = JobId,
            startedAt = StartedAtUtc.ToString("O"),
            users = _opt.Users,
            rampSeconds = _opt.RampSeconds,
            holdSeconds = _opt.HoldSeconds
        };
    }

    public object Snapshot()
    {
        return new
        {
            ok = true,
            jobId = JobId,
            startedAt = StartedAtUtc.ToString("O"),
            elapsedSeconds = (long)(DateTime.UtcNow - StartedAtUtc).TotalSeconds,
            onlineLogic = Interlocked.Read(ref _onlineLogic),
            onlineBattle = Interlocked.Read(ref _onlineBattle),
            login = _login.Snapshot(),
            switchLogic = _switchLogic.Snapshot(),
            switchBattle = _switchBattle.Snapshot(),
            connectLogic = _connectLogic.Snapshot(),
            connectBattle = _connectBattle.Snapshot(),
            errorsSample = _errors.Take(20).ToArray()
        };
    }

    private static async Task ConnectAsync(TcpClient client, string host, int port, int timeoutMs, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
        await client.ConnectAsync(host, port, timeoutCts.Token);
    }
}

internal sealed class Metric
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
        while (_recent.Count > 1024 && _recent.TryDequeue(out _)) { }
    }

    public void RecordFail(string _)
    {
        Interlocked.Increment(ref _fail);
    }

    public object Snapshot()
    {
        var ok = Interlocked.Read(ref _ok);
        var fail = Interlocked.Read(ref _fail);
        var latSum = Interlocked.Read(ref _latSum);
        var latMax = Interlocked.Read(ref _latMax);
        var avg = ok > 0 ? latSum * 1.0 / ok : 0;
        var p95 = Percentile(0.95);
        return new { ok, fail, avgMs = avg, p95Ms = p95, maxMs = latMax };
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

internal static class InterlockedExtensions
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

internal static class EnvelopeRpc
{
    public static async Task<RpcResult<TResp>> SendAsync<TReq, TResp>(string host, int port, TReq req, string? token,
        int timeoutMs, CancellationToken ct)
        where TReq : class, IMessage<TReq>
        where TResp : class, IMessage<TResp>, new()
    {
        using var tcp = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));
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
        if (respLen <= 0 || respLen > 8 * 1024 * 1024) throw new InvalidOperationException($"invalid_resp_len={respLen}");
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

internal sealed record RpcResult<T>(T Response, long LatencyMs);
