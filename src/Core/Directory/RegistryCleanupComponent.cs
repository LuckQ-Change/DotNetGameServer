using Core.Configuration;

namespace Core.Directory;

/// <summary>定期将失联注册项标记为离线，并移除已经过期的条目。</summary>
public sealed class RegistryCleanupComponent(
    IServerRegistry registry,
    ServiceDiscoveryOptions options) : IServerComponent
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    public string Name => nameof(RegistryCleanupComponent);

    public Task InitializeAsync()
    {
        if (_task is not null) return Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _task = RunAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DestroyAsync()
    {
        var cts = _cts;
        var task = _task;
        if (cts is null || task is null) return;

        await cts.CancelAsync();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // 正常关闭。
        }
        finally
        {
            cts.Dispose();
            _cts = null;
            _task = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(1, options.CleanupIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await registry.CleanupStaleAsync(
                Math.Max(1, options.OfflineTtlSeconds),
                Math.Max(options.OfflineTtlSeconds + 1, options.RemoveTtlSeconds),
                cancellationToken);
        }
    }
}
