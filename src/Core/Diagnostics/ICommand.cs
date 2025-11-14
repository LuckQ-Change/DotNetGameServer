namespace Core.Diagnostics;

public interface ICommand
{
    string Name { get; }
    Task<object?> ExecuteAsync(string[] args, ICommandContext context);
}

public interface ICommandContext
{
    Core.BaseGameServer? Server { get; }
}

public sealed class DefaultCommandContext : ICommandContext
{
    public DefaultCommandContext(Core.BaseGameServer? server)
    {
        Server = server;
    }

    public Core.BaseGameServer? Server { get; }
}
