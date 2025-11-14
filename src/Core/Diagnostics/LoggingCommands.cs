using Microsoft.Extensions.Logging;

namespace Core.Diagnostics;

public static class LoggingCommands
{
    public static void RegisterAll()
    {
        CommandRegistry.Register(new LogCommand());
    }
}

file sealed class LogCommand : ICommand
{
    public string Name => "log";
    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length == 0) return Task.FromResult<object?>(new { usage = "log on|off|level <Trace|Debug|Information|Warning|Error|Critical|None>|heartbeat <on|off>|category <name> <on|off>|status" });
        var sub = args[0].ToLowerInvariant();
        if (sub == "on")
        {
            Core.Logging.LogCenter.SetGlobalEnabled(true);
            return Task.FromResult<object?>(new { ok = true, global = true });
        }
        if (sub == "off")
        {
            Core.Logging.LogCenter.SetGlobalEnabled(false);
            return Task.FromResult<object?>(new { ok = true, global = false });
        }
        if (sub == "level" && args.Length >= 2)
        {
            if (Enum.TryParse<LogLevel>(args[1], true, out var lvl))
            {
                Core.Logging.LogCenter.SetMinimumLevel(lvl);
                return Task.FromResult<object?>(new { ok = true, level = lvl.ToString() });
            }
            return Task.FromResult<object?>(new { ok = false, error = "invalid_level" });
        }
        if (sub == "heartbeat" && args.Length >= 2)
        {
            var on = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (on) Core.Logging.LogCenter.EnableCategory("Heartbeat"); else Core.Logging.LogCenter.DisableCategory("Heartbeat");
            return Task.FromResult<object?>(new { ok = true, category = "Heartbeat", enabled = on });
        }
        if (sub == "category" && args.Length >= 3)
        {
            var name = args[1];
            var on = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (on) Core.Logging.LogCenter.EnableCategory(name); else Core.Logging.LogCenter.DisableCategory(name);
            return Task.FromResult<object?>(new { ok = true, category = name, enabled = on });
        }
        if (sub == "status")
        {
            return Task.FromResult<object?>(new
            {
                global = Core.Logging.LogCenter.GlobalEnabled,
                level = Core.Logging.LogCenter.MinimumLevel.ToString()
            });
        }
        return Task.FromResult<object?>(new { usage = "log on|off|level <Trace|Debug|Information|Warning|Error|Critical|None>|heartbeat <on|off>|category <name> <on|off>|status" });
    }
}
