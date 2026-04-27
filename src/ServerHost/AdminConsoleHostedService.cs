using Core.Diagnostics;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ServerHost;

public sealed class AdminConsoleHostedService(
    ILogger<AdminConsoleHostedService> logger,
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
                catch
                {
                    // ignored
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
        catch
        {
            // ignored
        }

        try
        {
            await (_task ?? Task.CompletedTask);
        }
        catch
        {
            // ignored
        }
    }
}
