using System.Text;

namespace Window;

public enum LogLevel { Info, Warn, Error }

public class LogManager
{
    public record LogEntry(DateTime Time, string Service, LogLevel Level, string Text);

    private readonly List<LogEntry> _entries = new();
    private readonly int _maxEntries;

    public LogManager(int maxEntries = 5000)
    {
        _maxEntries = Math.Max(100, maxEntries);
    }

    public void Append(string service, string text, LogLevel level = LogLevel.Info)
    {
        _entries.Add(new LogEntry(DateTime.Now, service, level, text));
        if (_entries.Count > _maxEntries)
        {
            _entries.RemoveRange(0, _entries.Count - _maxEntries);
        }
    }

    public void Clear() => _entries.Clear();

    public IEnumerable<LogEntry> Filter(string? service)
    {
        if (string.IsNullOrWhiteSpace(service) || service == "All") return _entries;
        return _entries.Where(e => e.Service == service);
    }

    public string ToDisplayText(string? service)
    {
        var sb = new StringBuilder();
        foreach (var e in Filter(service))
        {
            sb.AppendLine($"[{e.Time:HH:mm:ss}] [{e.Service}] {e.Level}: {e.Text}");
        }
        return sb.ToString();
    }

    public string SaveToFile(string? service, string? directory = null)
    {
        var dir = directory ?? AppDomain.CurrentDomain.BaseDirectory;
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"logs-{(service ?? "All")}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(file, ToDisplayText(service));
        return file;
    }
}