using Core;

namespace BattleServer;

/// <summary>
/// 战斗管理器接口
/// </summary>
public interface IBattleManagerComponent : IServerComponent
{
    Task<BattleInfo?> StartBattleAsync(string playerId, string battleType);
    Task<AttackResult?> ProcessAttackAsync(string battleId, string playerId, int damage);
    Task<BattleInfo?> GetBattleAsync(string battleId);
    Task EndBattleAsync(string battleId);
}