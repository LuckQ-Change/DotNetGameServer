namespace ServerHost;

public sealed class CurrentServerAccessor
{
    public Core.BaseGameServer? Server { get; private set; }
    public void Set(Core.BaseGameServer? server) { Server = server; }
}
