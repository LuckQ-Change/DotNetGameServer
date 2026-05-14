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

    public async Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length == 0)
        {
            return Usage();
        }

        var sub = args[0].ToLowerInvariant();
        if (sub == "on")
        {
            Logging.LogCenter.SetGlobalEnabled(true);
            return new { ok = true, global = true };
        }

        if (sub == "off")
        {
            Logging.LogCenter.SetGlobalEnabled(false);
            return new { ok = true, global = false };
        }

        if (sub == "level" && args.Length >= 2)
        {
            if (Enum.TryParse<LogLevel>(args[1], true, out var level))
            {
                Logging.LogCenter.SetMinimumLevel(level);
                return new { ok = true, level = level.ToString() };
            }

            return new { ok = false, error = "invalid_level" };
        }

        if (sub == "heartbeat" && args.Length >= 2)
        {
            var on = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (on) Logging.LogCenter.EnableCategory("Heartbeat");
            else Logging.LogCenter.DisableCategory("Heartbeat");
            return new { ok = true, category = "Heartbeat", enabled = on };
        }

        if (sub == "category" && args.Length >= 3)
        {
            var name = args[1];
            var on = args[2].Equals("on", StringComparison.OrdinalIgnoreCase);
            if (on) Logging.LogCenter.EnableCategory(name);
            else Logging.LogCenter.DisableCategory(name);
            return new { ok = true, category = name, enabled = on };
        }

        if (sub == "recent")
        {
            var count = args.Length >= 2 && int.TryParse(args[1], out var parsed) ? parsed : 100;
            var items = Logging.LogCenter.GetRecent(count);
            return new { count = items.Count, items };
        }

        if (sub == "cache" && args.Length >= 2)
        {
            var action = args[1].ToLowerInvariant();
            if (action == "status")
            {
                return CacheStatus();
            }

            if (action == "clear")
            {
                Logging.LogCenter.ClearCache();
                return new { ok = true, cleared = true };
            }

            if (action == "on")
            {
                Logging.LogCenter.SetCacheEnabled(true);
                return new { ok = true, cacheEnabled = true };
            }

            if (action == "off")
            {
                Logging.LogCenter.SetCacheEnabled(false);
                return new { ok = true, cacheEnabled = false };
            }

            if (action == "size" && args.Length >= 3 && int.TryParse(args[2], out var size))
            {
                Logging.LogCenter.SetCacheCapacity(size);
                return CacheStatus();
            }

            return Usage();
        }

        if (sub == "export")
        {
            string? path = null;
            var count = 0;
            if (args.Length >= 2)
            {
                path = args[1];
            }
            if (args.Length >= 3)
            {
                int.TryParse(args[2], out count);
            }

            var result = await Logging.LogCenter.ExportAsync(path, count);
            return new { ok = true, result.Path, result.Count };
        }

        if (sub == "status")
        {
            return new
            {
                global = Logging.LogCenter.GlobalEnabled,
                level = Logging.LogCenter.MinimumLevel.ToString(),
                cache = CacheStatus()
            };
        }

        return Usage();
    }

    private static object CacheStatus()
    {
        return new
        {
            enabled = Logging.LogCenter.CacheEnabled,
            capacity = Logging.LogCenter.CacheCapacity,
            cachedCount = Logging.LogCenter.GetRecent(int.MaxValue).Count
        };
    }

    private static object Usage()
    {
        return new
        {
            usage =
                "log on|off|level <Trace|Debug|Information|Warning|Error|Critical|None>|heartbeat <on|off>|category <name> <on|off>|recent [count]|cache status|cache clear|cache on|cache off|cache size <count>|export [path] [count]|status"
        };
    }
}
