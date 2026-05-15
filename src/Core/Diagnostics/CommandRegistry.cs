using System.Collections.Concurrent;

namespace Core.Diagnostics;

public static class CommandRegistry
{
    private static readonly ConcurrentDictionary<string, ICommand> Commands = new(StringComparer.OrdinalIgnoreCase);

    public static void Register(ICommand cmd, params string[] aliases)
    {
        Commands[cmd.Name] = cmd;
        foreach (var a in aliases) Commands[a] = cmd;
    }

    public static IReadOnlyCollection<string> List()
    {
        return Commands.Keys.ToArray();
    }

    public static IReadOnlyList<CommandDescriptor> Describe()
    {
        var groups = Commands
            .GroupBy(kv => kv.Value)
            .Select(g =>
            {
                var cmd = g.Key;
                var aliasSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var k in g.Select(x => x.Key))
                {
                    if (string.Equals(k, cmd.Name, StringComparison.OrdinalIgnoreCase)) continue;
                    aliasSet.Add(k);
                }
                return new CommandDescriptor(cmd.Name, aliasSet.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
            })
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return groups;
    }

    public static async Task<object?> ExecuteTextAsync(string text, ICommandContext context)
    {
        var tokens = Tokenize(text);
        if (tokens.Length == 0) return new { ok = false, error = "empty" };
        var name = tokens[0];
        if (!Commands.TryGetValue(name, out var cmd)) return new { ok = false, error = "not_found", name };
        var args = tokens.Skip(1).ToArray();
        var r = await cmd.ExecuteAsync(args, context);
        return new { ok = true, result = r };
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

public sealed record CommandDescriptor(string Name, string[] Aliases);
