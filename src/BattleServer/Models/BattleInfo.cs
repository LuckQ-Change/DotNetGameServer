namespace BattleServer;

/// <summary>
/// 战斗信息
/// </summary>
public record BattleInfo(string BattleId, string PlayerId, string BattleType, int PlayerHp, int EnemyHp, bool IsActive);