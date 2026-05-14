using Shared.Time;

namespace Core.Diagnostics;

public static class TimeCommands
{
    public static void RegisterAll()
    {
        CommandRegistry.Register(new TimeCommand());
    }
}

file sealed class TimeCommand : ICommand
{
    public string Name => "time";

    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        if (args.Length == 0)
        {
            return Task.FromResult<object?>(Usage());
        }

        var sub = args[0].ToLowerInvariant();
        if (sub == "status")
        {
            return Task.FromResult<object?>(ToResponse(TimeManager.GetSnapshot()));
        }

        if (sub == "reset")
        {
            TimeManager.UseSystem();
            return Task.FromResult<object?>(new { ok = true, action = "reset", time = ToResponse(TimeManager.GetSnapshot()) });
        }

        if (sub == "advance" && args.Length >= 2)
        {
            if (!double.TryParse(args[1], out var seconds))
            {
                return Task.FromResult<object?>(new { ok = false, error = "invalid_seconds" });
            }

            TimeManager.Advance(TimeSpan.FromSeconds(seconds));
            return Task.FromResult<object?>(new { ok = true, action = "advance", deltaSeconds = seconds, time = ToResponse(TimeManager.GetSnapshot()) });
        }

        if (sub == "offset" && args.Length >= 2)
        {
            if (!double.TryParse(args[1], out var seconds))
            {
                return Task.FromResult<object?>(new { ok = false, error = "invalid_seconds" });
            }

            TimeManager.SetOffset(TimeSpan.FromSeconds(seconds));
            return Task.FromResult<object?>(new { ok = true, action = "offset", offsetSeconds = seconds, time = ToResponse(TimeManager.GetSnapshot()) });
        }

        if (sub == "set" && args.Length >= 2)
        {
            try
            {
                if (long.TryParse(args[1], out var unixSeconds))
                {
                    TimeManager.SetUtc(TimeUtil.FromUnixSeconds(unixSeconds));
                }
                else
                {
                    TimeManager.SetUtc(TimeUtil.ParseIso8601(args[1]));
                }

                return Task.FromResult<object?>(new { ok = true, action = "set", time = ToResponse(TimeManager.GetSnapshot()) });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object?>(new { ok = false, error = "invalid_time", message = ex.Message });
            }
        }

        if (sub == "freeze" && args.Length >= 2)
        {
            try
            {
                if (long.TryParse(args[1], out var unixSeconds))
                {
                    TimeManager.UseManual(TimeUtil.FromUnixSeconds(unixSeconds));
                }
                else
                {
                    TimeManager.UseManual(TimeUtil.ParseIso8601(args[1]));
                }

                return Task.FromResult<object?>(new { ok = true, action = "freeze", time = ToResponse(TimeManager.GetSnapshot()) });
            }
            catch (Exception ex)
            {
                return Task.FromResult<object?>(new { ok = false, error = "invalid_time", message = ex.Message });
            }
        }

        return Task.FromResult<object?>(Usage());
    }

    private static object ToResponse(TimeSnapshot snapshot)
    {
        return new
        {
            mode = snapshot.Mode,
            logicalUtcNow = TimeUtil.FormatIso8601Utc(snapshot.LogicalUtcNow),
            systemUtcNow = TimeUtil.FormatIso8601Utc(snapshot.SystemUtcNow),
            offsetSeconds = snapshot.Offset.TotalSeconds,
            unixSeconds = snapshot.UnixSeconds,
            unixMilliseconds = snapshot.UnixMilliseconds
        };
    }

    private static object Usage()
    {
        return new
        {
            usage = "time status|set <iso8601|unixSeconds>|offset <seconds>|advance <seconds>|freeze <iso8601|unixSeconds>|reset"
        };
    }
}
