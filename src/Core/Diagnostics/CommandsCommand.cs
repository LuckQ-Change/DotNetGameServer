namespace Core.Diagnostics;

public sealed class CommandsCommand : ICommand
{
    public string Name => "commands";

    public Task<object?> ExecuteAsync(string[] args, ICommandContext context)
    {
        var items = CommandRegistry.Describe();
        return Task.FromResult<object?>(new { count = items.Count, items });
    }
}

