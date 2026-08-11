using System.Text.Json;
using System.Threading.Channels;

using Core.Configuration;

using Microsoft.Extensions.Logging;

namespace Hosting;

/// <summary>非阻塞 JSONL 文件日志，按日期和大小滚动，并按天清理。</summary>
internal sealed class RollingFileLoggerProvider : ILoggerProvider
{
    private readonly string _role;
    private readonly LoggingOptions _options;
    private readonly Channel<FileLogRecord> _records;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _writerTask;
    private int _disposed;

    public RollingFileLoggerProvider(string role, LoggingOptions options)
    {
        _role = Sanitize(role);
        _options = options;
        _records = Channel.CreateBounded<FileLogRecord>(new BoundedChannelOptions(4096)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _records.Writer.TryComplete();
        try { _writerTask.Wait(TimeSpan.FromSeconds(3)); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RollingFileLogger] flush failed: {ex.Message}"); }
        _cts.Cancel();
        _cts.Dispose();
    }

    private void Enqueue(FileLogRecord record) => _records.Writer.TryWrite(record);

    private async Task WriteLoopAsync()
    {
        StreamWriter? writer = null;
        string? currentDate = null;
        var sequence = 0;
        try
        {
            var directory = Path.GetFullPath(_options.FileDirectory);
            Directory.CreateDirectory(directory);
            DeleteExpired(directory);
            await foreach (var record in _records.Reader.ReadAllAsync(_cts.Token))
            {
                var date = record.TimestampUtc.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                var maxBytes = Math.Max(1, _options.FileMaxMegabytes) * 1024L * 1024L;
                if (writer is null || currentDate != date || writer.BaseStream.Length >= maxBytes)
                {
                    writer?.Dispose();
                    if (currentDate != date) { currentDate = date; sequence = 0; }
                    string path;
                    do
                    {
                        path = Path.Combine(directory, $"{_role}-{date}-{sequence++:000}.jsonl");
                    } while (File.Exists(path) && new FileInfo(path).Length >= maxBytes);
                    writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                        new System.Text.UTF8Encoding(false));
                }

                await writer.WriteLineAsync(JsonSerializer.Serialize(record));
                await writer.FlushAsync();
            }
        }
        catch (OperationCanceledException) when (_cts.IsCancellationRequested)
        {
            System.Diagnostics.Debug.WriteLine("[RollingFileLogger] writer stopped.");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RollingFileLogger] {ex}");
        }
        finally
        {
            writer?.Dispose();
        }
    }

    private void DeleteExpired(string directory)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _options.FileRetentionDays));
        foreach (var file in Directory.GetFiles(directory, $"{_role}-*.jsonl"))
        {
            try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[RollingFileLogger] retention cleanup failed: {ex.Message}"); }
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((string.IsNullOrWhiteSpace(value) ? "server" : value)
            .Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }

    private sealed class FileLogger(RollingFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._options.MinimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            Dictionary<string, object?>? properties = null;
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                properties = pairs
                    .Where(pair => pair.Key != "{OriginalFormat}")
                    .ToDictionary(pair => pair.Key, pair => pair.Value);
            }
            provider.Enqueue(new FileLogRecord(
                DateTime.UtcNow,
                logLevel.ToString(),
                category,
                eventId.Id,
                formatter(state, exception),
                exception?.ToString(),
                properties));
        }
    }

    private sealed record FileLogRecord(
        DateTime TimestampUtc,
        string Level,
        string Category,
        int EventId,
        string Message,
        string? Exception,
        IReadOnlyDictionary<string, object?>? Properties);
}
