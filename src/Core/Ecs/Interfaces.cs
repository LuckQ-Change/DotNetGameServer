namespace Core.Ecs;

public interface IEntityComponent { }

public interface ISystem
{
    string Name { get; }
    Task InitializeAsync(World world);
    Task DestroyAsync(World world);
    // 可选的帧更新；当前项目消息驱动，可不调用
    Task UpdateAsync(World world, TimeSpan delta) => Task.CompletedTask;
}