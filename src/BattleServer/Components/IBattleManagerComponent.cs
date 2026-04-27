using Core;

namespace BattleServer;

/// <summary>
/// 战斗管理器接口
/// </summary>
public interface IBattleManagerComponent : IServerComponent
{
    public Task<BattleInfo?> StartBattleAsync(string playerId, string battleType);
    public Task<AttackResult?> ProcessAttackAsync(string battleId, string playerId, int damage);
    public Task<BattleInfo?> GetBattleAsync(string battleId);
    public Task EndBattleAsync(string battleId);
}
