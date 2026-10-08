using Core;

namespace BasicServer;

/// <summary>
/// 监视 plugins/HotReloadPlugin.dll。文件更新后复制到临时目录再装入，
/// 避免 Windows 锁住正在监听的那个文件。
/// </summary>
public sealed class HotReloadDirectory : IServerComponent
{
    private const string PluginFileName = "HotReloadPlugin.dll";
    private readonly DemoGameServer _server;
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private FileSystemWatcher? _watcher;
    private int _pending;

    public HotReloadDirectory(DemoGameServer server, string directory)
    {
        _server = server;
        _directory = directory;
    }

    public string Name => nameof(HotReloadDirectory);

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        await LoadLatestAsync(_lifetime.Token);
        _watcher = new FileSystemWatcher(_directory, PluginFileName)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };
        _watcher.Created += (_, _) => QueueLoad();
        _watcher.Changed += (_, _) => QueueLoad();
        _watcher.Renamed += (_, _) => QueueLoad();
        _server.Log($"热更新目录: {_directory}");
    }

    public Task DestroyAsync()
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
        }

        _lifetime.Cancel();
        _gate.Dispose();
        return Task.CompletedTask;
    }

    private void QueueLoad()
    {
        var ticket = Interlocked.Increment(ref _pending);
        _ = LoadAfterQuietPeriodAsync(ticket);
    }

    private async Task LoadAfterQuietPeriodAsync(int ticket)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400), _lifetime.Token);
            if (ticket != Volatile.Read(ref _pending)) return;
            await _gate.WaitAsync(_lifetime.Token);
            try
            {
                if (ticket != Volatile.Read(ref _pending)) return;
                await LoadLatestAsync(_lifetime.Token);
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception exception) when (_lifetime.IsCancellationRequested
            && exception is OperationCanceledException or ObjectDisposedException)
        {
            _server.Log("热更新监视已停止。", LogLevel.Debug);
        }
        catch (Exception exception)
        {
            _server.Log($"热更新失败: {exception.Message}", LogLevel.Warning);
        }
    }

    private async Task LoadLatestAsync(CancellationToken cancellationToken)
    {
        var source = Path.Combine(_directory, PluginFileName);
        if (!File.Exists(source))
        {
            _server.Log($"等待热更新插件: {source}");
            return;
        }

        var shadowDirectory = Path.Combine(Path.GetTempPath(), "dotnetgameserver-hotreload", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shadowDirectory);
        var shadowPath = Path.Combine(shadowDirectory, PluginFileName);
        await CopyWhenReadableAsync(source, shadowPath, cancellationToken);
        var loaded = await _server.LoadPluginAsync(shadowPath, cancellationToken);
        _server.Log($"热更新已生效: {loaded.PluginName}");
    }

    private static async Task CopyWhenReadableAsync(string source, string destination, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (input.Length == 0)
                    throw new IOException("插件文件仍是空的。");
                await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
                await input.CopyToAsync(output, cancellationToken);
                return;
            }
            catch (IOException) when (attempt < 19)
            {
                await Task.Delay(100, cancellationToken);
            }
        }
    }
}
