using System.Collections.Concurrent;

namespace Core.Diagnostics;

public sealed class CommandRegistry
{
    private static readonly ConcurrentDictionary<string, ICommand> _commands = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim _sema = new(1, 1);

    public static void Register(ICommand cmd, params string[] aliases)
    {
        _commands[cmd.Name] = cmd;
        foreach (var a in aliases) _commands[a] = cmd;
    }

    public static IReadOnlyCollection<string> List()
    {
        return _commands.Keys.ToArray();
    }

    public static async Task<object?> ExecuteTextAsync(string text, ICommandContext context)
    {
        var tokens = Tokenize(text);
        if (tokens.Length == 0) return new { ok = false, error = "empty" };
        var name = tokens[0];
        if (!_commands.TryGetValue(name, out var cmd)) return new { ok = false, error = "not_found", name };
        await _sema.WaitAsync();
        try
        {
            var args = tokens.Skip(1).ToArray();
            var r = await cmd.ExecuteAsync(args, context);
            return new { ok = true, result = r };
        }
        finally
        {
            _sema.Release();
        }
    }

    private static string[] Tokenize(string text)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        var inQuote = false;
        foreach (var ch in text)
        {
            if (ch == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && char.IsWhiteSpace(ch))
            {
                if (cur.Length > 0) { list.Add(cur.ToString()); cur.Clear(); }
            }
            else cur.Append(ch);
        }
        if (cur.Length > 0) list.Add(cur.ToString());
        return list.ToArray();
    }
}
