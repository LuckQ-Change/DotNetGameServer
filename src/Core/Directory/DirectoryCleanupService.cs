using Core.Configuration;

namespace Core.Directory;

public class DirectoryCleanupService : IServerComponent
{
    private readonly IServerRegistry _registry;
    private readonly DirectoryOptions _options;
    private CancellationTokenSource? _cts;
    private Task? _task;

    public string Name => "DirectoryCleanup";

    public DirectoryCleanupService(IServerRegistry registry, DirectoryOptions options)
    {
        _registry = registry;
        _options = options;
    }

    public Task InitializeAsync()
    {
        _cts = new CancellationTokenSource();
        _task = RunCleanupLoop(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task DestroyAsync()
    {
        await _cts?.CancelAsync()!;
        try { await (_task ?? Task.CompletedTask); } catch (OperationCanceledException) { /* 正常取消 */ } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[DirectoryCleanup] 等待清理任务失败: {ex.Message}"); }
    }

    private async Task RunCleanupLoop(CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.Registry.Redis.CleanupIntervalSeconds));
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token);
                await _registry.CleanupStaleAsync(
                    _options.Registry.Redis.OfflineTtlSeconds,
                    _options.Registry.Redis.RemoveTtlSeconds);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DirectoryCleanup] 清理过期注册失败: {ex.Message}");
            }
        }
    }
}
