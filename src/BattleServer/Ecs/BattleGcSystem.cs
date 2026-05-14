using Core.Ecs;
using Shared.Time;

namespace BattleServer.Ecs;

public class BattleGcSystem(TimeSpan ttl) : ISystem
{
    public string Name => nameof(BattleGcSystem);

    public Task InitializeAsync(World world) => Task.CompletedTask;

    public Task DestroyAsync(World world) => Task.CompletedTask;

    public Task UpdateAsync(World world, TimeSpan delta)
    {
        var now = TimeManager.UtcNow;
        foreach (var entity in world.Entities.ToArray())
        {
            var comp = entity.GetComponent<BattleStateComponent>();
            if (comp == null) continue;

            var expired = !comp.IsActive || (now - comp.LastActiveUtc) > ttl;
            if (expired)
            {
                // 移除战斗实体。
                world.RemoveEntity(entity.Id);

                // 移除玩家索引。
                if (!string.IsNullOrEmpty(comp.PlayerId))
                {
                    var playerEntity = world.GetOrCreateEntity(comp.PlayerId);
                    var index = playerEntity.GetComponent<PlayerBattlesComponent>() ??
                                playerEntity.AddComponent(new PlayerBattlesComponent());
                    index.Remove(comp.BattleId);
                }
            }
        }

        return Task.CompletedTask;
    }
}
