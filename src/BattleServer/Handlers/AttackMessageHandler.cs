using Core;
using DotNetGameServer;
using Core.Ecs;
using BattleServer.Ecs;

namespace BattleServer;

/// <summary>
/// 攻击消息处理器
/// </summary>
public class AttackMessageHandler : IStrongMessageHandler<AttackRequest, AttackResponse>
{
    public Task<AttackResponse?> HandleAsync(AttackRequest request, IServerContext context)
    {
        // 统一鉴权示例：基于 context.Auth 的访问控制
        if (context.Auth == null)
        {
            context.Log($"[战斗][AUTH] 访问拒绝: remote={context.RemoteAddress} player={request.PlayerId} code=AUTH_REQUIRED");
            return Task.FromResult<AttackResponse?>(new AttackResponse { Success = false, Message = "AUTH_REQUIRED" });
        }
        if (!string.Equals(context.Auth.UserId, request.PlayerId, StringComparison.Ordinal))
        {
            context.Log($"[战斗][AUTH] 用户不匹配: remote={context.RemoteAddress} player={request.PlayerId} authed={context.Auth.UserId} code=FORBIDDEN");
            return Task.FromResult<AttackResponse?>(new AttackResponse { Success = false, Message = "FORBIDDEN" });
        }

        var ecs = context.GetComponent<EcsWorldComponent>();
        if (ecs == null)
        {
            var fail = new AttackResponse { Success = false, Message = "ECS_NOT_FOUND" };
            return Task.FromResult<AttackResponse?>(fail);
        }

        // 按battleId直接定位实体，支持并行多战斗
        var entity = ecs.World.GetOrCreateEntity(request.BattleId);
        var comp = entity.GetComponent<BattleStateComponent>();
        if (comp == null)
        {
            var fail = new AttackResponse { Success = false, Message = "BATTLE_NOT_FOUND" };
            return Task.FromResult<AttackResponse?>(fail);
        }

        var (newPlayerHp, newEnemyHp, ended, playerWon, message) = comp.Attack(request.PlayerId, request.Damage);
        context.Log($"[战斗][ECS] battle={request.BattleId} 攻击，伤害={request.Damage}，结果={message}");

        // 如果战斗结束：移除battle实体，并更新玩家索引
        if (ended)
        {
            // 尝试移除战斗实体
            var removed = ecs.World.RemoveEntity(request.BattleId);
            context.Log($"[战斗][ECS] {request.BattleId} 结束，实体移除: {removed}");

            // 更新玩家索引
            var playerEntity = ecs.World.GetOrCreateEntity(request.PlayerId);
            var index = playerEntity.GetComponent<PlayerBattlesComponent>() ?? playerEntity.AddComponent(new PlayerBattlesComponent());
            index.Remove(request.BattleId);
        }

        var response = new AttackResponse
        {
            Success = true,
            Message = message,
            PlayerHp = newPlayerHp,
            EnemyHp = newEnemyHp,
            BattleEnded = ended,
            PlayerWon = playerWon
        };

        return Task.FromResult<AttackResponse?>(response);
    }
}
