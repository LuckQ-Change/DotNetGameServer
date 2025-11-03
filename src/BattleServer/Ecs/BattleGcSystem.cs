using Core.Ecs;

namespace BattleServer.Ecs;

public class BattleGcSystem : ISystem
{
    public string Name => "BattleGc";
    private readonly TimeSpan _ttl;

    public BattleGcSystem(TimeSpan ttl)
    {
        _ttl = ttl;
    }

    public Task InitializeAsync(World world)
    {
        return Task.CompletedTask;
    }

    public Task DestroyAsync(World world)
    {
        return Task.CompletedTask;
    }

    public Task UpdateAsync(World world, TimeSpan delta)
    {
        var now = DateTime.UtcNow;
        foreach (var entity in world.Entities.ToArray())
        {
            var comp = entity.GetComponent<BattleStateComponent>();
            if (comp == null) continue;

            var expired = !comp.IsActive || (now - comp.LastActiveUtc) > _ttl;
            if (expired)
            {
                // 移除战斗实体
                world.RemoveEntity(entity.Id);

                // 移除玩家索引
                if (!string.IsNullOrEmpty(comp.PlayerId))
                {
                    var playerEntity = world.GetOrCreateEntity(comp.PlayerId);
                    var index = playerEntity.GetComponent<PlayerBattlesComponent>() ?? playerEntity.AddComponent(new PlayerBattlesComponent());
                    index.Remove(comp.BattleId);
                }
            }
        }
        return Task.CompletedTask;
    }
}