namespace Core.Ecs;

public interface IEntityComponent { }

public interface ISystem
{
    public string Name { get; }
    public Task InitializeAsync(World world);

    public Task DestroyAsync(World world);
    // 可选的帧更新；当前项目消息驱动，可不调用
    public Task UpdateAsync(World world, TimeSpan delta) => Task.CompletedTask;
}
