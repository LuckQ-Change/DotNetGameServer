using System;
using Microsoft.Extensions.Logging;

namespace Window.Logging;

public class UiLoggerProvider : Microsoft.Extensions.Logging.ILoggerProvider
{
    private readonly Action<string> _output;

    public UiLoggerProvider(Action<string> output)
    {
        _output = output;
    }

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName)
    {
        return new UiLogger(categoryName, _output);
    }

    public void Dispose()
    {
    }

    private class UiLogger : Microsoft.Extensions.Logging.ILogger
    {
        private readonly string _category;
        private readonly Action<string> _output;

        public UiLogger(string category, Action<string> output)
        {
            _category = category;
            _output = output;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel)
        {
            // 简化：全部级别输出到 UI
            return true;
        }

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var msg = formatter(state, exception);
            if (string.IsNullOrEmpty(msg)) return;
            // 保持简单输出；BaseGameServer 会生成 "[Server] message" 模式
            var line = msg;
            if (exception != null)
            {
                line += $" | {exception.GetType().Name}: {exception.Message}";
            }
            _output(line);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();
            public void Dispose() { }
        }
    }
}