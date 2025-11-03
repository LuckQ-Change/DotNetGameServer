using Core;
using DotNetGameServer;
using Google.Protobuf;
using Core.Ecs;
using System;
using BattleServer.Ecs;

namespace BattleServer;

/// <summary>
/// 开始战斗消息处理器
/// </summary>
public class StartBattleMessageHandler : IStrongMessageHandler<StartBattleRequest, StartBattleResponse>
{
public Task<StartBattleResponse?> HandleAsync(StartBattleRequest request, IServerContext context)
{
    // 统一鉴权示例：基于 context.Auth 的访问控制
    if (context.Auth == null)
    {
        context.Log($"[战斗][AUTH] 访问拒绝: remote={context.RemoteAddress} player={request.PlayerId} code=AUTH_REQUIRED");
        return Task.FromResult<StartBattleResponse?>(new StartBattleResponse { Success = false, Message = "AUTH_REQUIRED" });
    }
    if (!string.Equals(context.Auth.UserId, request.PlayerId, StringComparison.Ordinal))
    {
        context.Log($"[战斗][AUTH] 用户不匹配: remote={context.RemoteAddress} player={request.PlayerId} authed={context.Auth.UserId} code=FORBIDDEN");
        return Task.FromResult<StartBattleResponse?>(new StartBattleResponse { Success = false, Message = "FORBIDDEN" });
    }

    var ecs = context.GetComponent<EcsWorldComponent>();
    if (ecs == null)
    {
        var fail = new StartBattleResponse { Success = false, Message = "ECS_NOT_FOUND" };
        return Task.FromResult<StartBattleResponse?>(fail);
    }

    // 生成battleId并以其作为实体ID，支持同一玩家并行多战斗
    var battleId = Guid.NewGuid().ToString("N")[..8];
    var entity = ecs.World.GetOrCreateEntity(battleId);
    var comp = entity.GetComponent<BattleStateComponent>() ?? entity.AddComponent(new BattleStateComponent());
    comp.Start(battleId, request.PlayerId, request.BattleType);

    // 维护玩家到战斗的索引，便于查询与管理
    var playerEntity = ecs.World.GetOrCreateEntity(request.PlayerId);
    var index = playerEntity.GetComponent<PlayerBattlesComponent>() ?? playerEntity.AddComponent(new PlayerBattlesComponent());
    index.Add(battleId);

    context.Log($"[战斗][ECS] 玩家={request.PlayerId} 开始战斗，类型: {request.BattleType}, battleId={comp.BattleId}");

    var response = new StartBattleResponse
    {
        Success = true,
        Message = "战斗开始",
        BattleId = comp.BattleId,
        PlayerHp = comp.PlayerHp,
        EnemyHp = comp.EnemyHp
    };

    return Task.FromResult<StartBattleResponse?>(response);
}
}