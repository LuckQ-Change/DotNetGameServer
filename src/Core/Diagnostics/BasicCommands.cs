using System.Globalization;

namespace Core.Diagnostics;

public static class BasicCommands
{
    public static void RegisterAll()
    {
        CommandRegistry.Register(new StatusCommand());
        CommandRegistry.Register(new LimitsCommand());
        CommandRegistry.Register(new HandlersCommand(), "handlers list");
        CommandRegistry.Register(new BlacklistCommand());
        LoggingCommands.RegisterAll();
    }
}

file sealed class StatusCommand : ICommand
{
    public string Name => "status";
    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        var s = context.Server;
        if (s == null) return Task.FromResult<object?>(new { error = "no_server" });
        return Task.FromResult<object?>(s.GetRuntimeSnapshot());
    }
}

file sealed class LimitsCommand : ICommand
{
    public string Name => "limits";
    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        var s = context.Server;
        if (s == null) return Task.FromResult<object?>(new { error = "no_server" });
        return Task.FromResult<object?>(s.GetLimitsSnapshot());
    }
}

file sealed class HandlersCommand : ICommand
{
    public string Name => "handlers";
    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        var s = context.Server;
        if (s == null) return Task.FromResult<object?>(new { error = "no_server" });
        var names = s.ListHandlerNames();
        return Task.FromResult<object?>(new { count = names.Length, names });
    }
}

file sealed class BlacklistCommand : ICommand
{
    public string Name => "blacklist";
    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        var s = context.Server;
        if (s == null) return Task.FromResult<object?>(new { error = "no_server" });
        if (args.Length == 0) return Task.FromResult<object?>(new { error = "usage", usage = "blacklist list|add <remote> [seconds]|remove <remote>" });
        var sub = args[0].ToLowerInvariant();
        if (sub == "list")
        {
            var map = s.ListBlacklist();
            var items = map.Select(kv => new { remote = kv.Key, until = kv.Value.ToString("o", CultureInfo.InvariantCulture) }).ToArray();
            return Task.FromResult<object?>(new { count = items.Length, items });
        }
        if (sub == "add" && args.Length >= 2)
        {
            var remote = args[1];
            var seconds = args.Length >= 3 && int.TryParse(args[2], out var sec) ? sec : 120;
            s.BlacklistAdd(remote, TimeSpan.FromSeconds(seconds), "manual");
            return Task.FromResult<object?>(new { ok = true, remote, seconds });
        }
        if (sub == "remove" && args.Length >= 2)
        {
            var remote = args[1];
            var ok = s.BlacklistRemove(remote);
            return Task.FromResult<object?>(new { ok, remote });
        }
        return Task.FromResult<object?>(new { error = "usage", usage = "blacklist list|add <remote> [seconds]|remove <remote>" });
    }
}
