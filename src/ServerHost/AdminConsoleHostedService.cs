using Core.Diagnostics;

using Microsoft.Extensions.Hosting;

namespace ServerHost;

public sealed class AdminConsoleHostedService(
    IHostEnvironment env,
    CurrentServerAccessor accessor)
    : IHostedService
{
    private CancellationTokenSource? _cts;
    private Task? _task;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var enabled = env.IsDevelopment();
        if (!enabled) return Task.CompletedTask;
        if (!Environment.UserInteractive) return Task.CompletedTask;
        _cts = new CancellationTokenSource();
        _task = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = Console.ReadLine();
                if (line == null) break;
                var ctx = new DefaultCommandContext(accessor.Server);
                var res = await CommandRegistry.ExecuteTextAsync(line, ctx);
                try
                {
                    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(res));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AdminConsole] 序列化命令结果失败: {ex.Message}");
                }
            }
        }, _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cts?.CancelAsync()!;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdminConsole] 取消令牌失败: {ex.Message}");
        }

        try
        {
            await (_task ?? Task.CompletedTask);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AdminConsole] 等待控制台任务失败: {ex.Message}");
        }
    }
}
