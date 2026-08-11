namespace Core.Components;

/// <summary>提供当前服务器实时负载，心跳、状态页和分流共同使用该数据源。</summary>
public interface IServerLoadProvider : IServerComponent
{
    int PlayerCount { get; }
    int Capacity { get; }
    void SetPlayerCount(int playerCount);
    void SetCapacity(int capacity);
}

public sealed class MutableServerLoadProvider : IServerLoadProvider
{
    private int _playerCount;
    private int _capacity;

    public MutableServerLoadProvider(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    public string Name => "ServerLoad";
    public int PlayerCount => Volatile.Read(ref _playerCount);
    public int Capacity => Volatile.Read(ref _capacity);

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync() => Task.CompletedTask;

    public void SetPlayerCount(int playerCount) =>
        Interlocked.Exchange(ref _playerCount, Math.Clamp(playerCount, 0, Capacity));

    public void SetCapacity(int capacity)
    {
        var normalized = Math.Max(1, capacity);
        Interlocked.Exchange(ref _capacity, normalized);
        if (PlayerCount > normalized)
            Interlocked.Exchange(ref _playerCount, normalized);
    }
}
