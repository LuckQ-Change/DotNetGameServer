using Core;
using DotNetGameServer;
using Google.Protobuf;

namespace BattleServer;

/// <summary>
/// 开始战斗消息处理器
/// </summary>
public class StartBattleMessageHandler : IMessageHandler
{
    public string MessageType => "StartBattleRequest";

    public async Task<byte[]?> HandleAsync(byte[] payload, IServerContext context)
    {
        var request = StartBattleRequest.Parser.ParseFrom(payload);
        
        var battleManager = context.GetComponent<IBattleManagerComponent>();
        if (battleManager == null)
        {
            context.Log("战斗管理器组件未找到");
            return null;
        }

        var battle = await battleManager.StartBattleAsync(request.PlayerId, request.BattleType);
        
        context.Log($"玩家 {request.PlayerId} 开始战斗，类型: {request.BattleType}");
        
        var response = new StartBattleResponse
        {
            Success = battle != null,
            Message = battle != null ? "战斗开始" : "无法开始战斗",
            BattleId = battle?.BattleId ?? "",
            PlayerHp = battle?.PlayerHp ?? 0,
            EnemyHp = battle?.EnemyHp ?? 0
        };
        
        return response.ToByteArray();
    }
}

/// <summary>
/// 攻击消息处理器
/// </summary>
public class AttackMessageHandler : IMessageHandler
{
    public string MessageType => "AttackRequest";

    public async Task<byte[]?> HandleAsync(byte[] payload, IServerContext context)
    {
        var request = AttackRequest.Parser.ParseFrom(payload);
        
        var battleManager = context.GetComponent<IBattleManagerComponent>();
        if (battleManager == null)
        {
            context.Log("战斗管理器组件未找到");
            return null;
        }

        var result = await battleManager.ProcessAttackAsync(request.BattleId, request.PlayerId, request.Damage);
        
        context.Log($"战斗 {request.BattleId} 攻击处理，伤害: {request.Damage}");
        
        var response = new AttackResponse
        {
            Success = result != null,
            Message = result?.Message ?? "攻击失败",
            PlayerHp = result?.PlayerHp ?? 0,
            EnemyHp = result?.EnemyHp ?? 0,
            BattleEnded = result?.BattleEnded ?? false,
            PlayerWon = result?.PlayerWon ?? false
        };
        
        return response.ToByteArray();
    }
}