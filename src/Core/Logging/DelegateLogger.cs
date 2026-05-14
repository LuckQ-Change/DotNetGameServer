using Microsoft.Extensions.Logging;

namespace Core.Logging;

/// <summary>
/// 适配器：将 Action<string> 包装为 ILogger 以统一日志管道。
/// </summary>
public sealed class DelegateLogger : ILogger
{
    private readonly Action<string> _writer;
    private readonly string _category;

    public DelegateLogger(Action<string> writer, string category = "DotNetGameServer")
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _category = string.IsNullOrWhiteSpace(category) ? "DotNetGameServer" : category;
    }

    private sealed class NoopScope : IDisposable { public void Dispose() { } }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => new NoopScope();

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var msg = formatter(state, exception);
        _writer($"[{_category}] {logLevel}: {msg}");
    }
}
