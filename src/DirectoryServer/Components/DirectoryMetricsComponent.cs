using System.Diagnostics.Metrics;
using Core;
using Shared.Time;

namespace DirectoryServer.Components;

public class DirectoryMetricsComponent(Action<string> log, Func<int>? activeServersProvider = null)
    : IServerComponent
{
    private CancellationTokenSource? _cts;

    private long _hbAccepted;
    private long _hbRejected;
    private long _hbError;
    private long _swAccepted;
    private long _swRejected;
    private long _swError;
    private DateTime _periodStartUtc = TimeManager.UtcNow;

    public string Name => "Metrics";

    private Meter? _meter;
    private Counter<long>? _hbAcceptedCounter;
    private Counter<long>? _hbRejectedCounter;
    private Counter<long>? _hbErrorCounter;
    private const string UnitCount = "count";

    public Task InitializeAsync()
    {
        _periodStartUtc = TimeManager.UtcNow;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => SummaryLoop(_cts.Token));

        _meter = new Meter("DirectoryServer", "1.0.0");
        _hbAcceptedCounter = _meter.CreateCounter<long>("directory_heartbeat_accept_total", unit: UnitCount, description: "心跳认证通过次数");
        _hbRejectedCounter = _meter.CreateCounter<long>("directory_heartbeat_rejected_total", unit: UnitCount, description: "心跳认证被拒绝次数");
        _hbErrorCounter = _meter.CreateCounter<long>("directory_heartbeat_error_total", unit: UnitCount, description: "心跳处理异常次数");
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        try { _cts?.Cancel(); } catch (Exception) { }
        _cts = null;
        Reset();
        try { _meter?.Dispose(); } catch (Exception) { }
        _meter = null;
        _hbAcceptedCounter = null;
        _hbRejectedCounter = null;
        _hbErrorCounter = null;
        return Task.CompletedTask;
    }

    public void RecordHeartbeat(bool accepted)
    {
        if (accepted)
        {
            Interlocked.Increment(ref _hbAccepted);
            _hbAcceptedCounter?.Add(1);
        }
        else
        {
            Interlocked.Increment(ref _hbRejected);
            _hbRejectedCounter?.Add(1);
        }
    }

    public void RecordHeartbeatError()
    {
        Interlocked.Increment(ref _hbError);
        _hbErrorCounter?.Add(1);
    }

    public void RecordSwitch(bool accepted)
    {
        if (accepted) Interlocked.Increment(ref _swAccepted);
        else Interlocked.Increment(ref _swRejected);
    }

    public void RecordSwitchError()
    {
        Interlocked.Increment(ref _swError);
    }

    private void Reset()
    {
        Interlocked.Exchange(ref _hbAccepted, 0);
        Interlocked.Exchange(ref _hbRejected, 0);
        Interlocked.Exchange(ref _hbError, 0);
        Interlocked.Exchange(ref _swAccepted, 0);
        Interlocked.Exchange(ref _swRejected, 0);
        Interlocked.Exchange(ref _swError, 0);
        _periodStartUtc = TimeManager.UtcNow;
    }

    private async Task SummaryLoop(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(60);
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                var start = _periodStartUtc;
                var end = TimeManager.UtcNow;
                var hbOk = Interlocked.Read(ref _hbAccepted);
                var hbReject = Interlocked.Read(ref _hbRejected);
                var hbErr = Interlocked.Read(ref _hbError);
                var swOk = Interlocked.Read(ref _swAccepted);
                var swReject = Interlocked.Read(ref _swRejected);
                var swErr = Interlocked.Read(ref _swError);
                var active = activeServersProvider?.Invoke();

                log($"[目录] 指标汇总 [{start:HH:mm:ss} - {end:HH:mm:ss}]: HB ok={hbOk} reject={hbReject} err={hbErr}; SW ok={swOk} reject={swReject} err={swErr}; active={(active?.ToString() ?? "-")}");
                Reset();
            }
            catch (Exception)
            {
                // 汇总日志失败不影响服务。
            }
        }
    }
}
