namespace Core.Diagnostics;

public interface ICommand
{
    public string Name { get; }
    public Task<object?> ExecuteAsync(string[] args, ICommandContext context);
}

public interface ICommandContext
{
    public BaseGameServer? Server { get; }
}

public sealed class DefaultCommandContext(BaseGameServer? server) : ICommandContext
{
    public BaseGameServer? Server { get; } = server;
}
