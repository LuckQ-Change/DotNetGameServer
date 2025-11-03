namespace BattleServer;

/// <summary>
/// 攻击结果
/// </summary>
public record AttackResult(string Message, int PlayerHp, int EnemyHp, bool BattleEnded, bool PlayerWon);