using Core.Ecs;
using Shared.Time;

namespace BattleServer.Ecs;

public class BattleStateComponent : IEntityComponent
{
    public string BattleId { get; private set; } = string.Empty;
    public string PlayerId { get; private set; } = string.Empty;
    public string BattleType { get; private set; } = "easy";
    public int PlayerHp { get; private set; }
    public int EnemyHp { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastActiveUtc { get; private set; }

    private readonly Random _random = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DestroyAsync() => Task.CompletedTask;

    public void Start(string battleId, string playerId, string battleType)
    {
        BattleId = battleId;
        PlayerId = playerId;
        BattleType = battleType;
        PlayerHp = battleType switch
        {
            "easy" => 100,
            "normal" => 80,
            "hard" => 60,
            _ => 100
        };
        EnemyHp = battleType switch
        {
            "easy" => 50,
            "normal" => 80,
            "hard" => 120,
            _ => 50
        };
        IsActive = true;
        CreatedAtUtc = TimeManager.UtcNow;
        LastActiveUtc = CreatedAtUtc;
    }

    public (int newPlayerHp, int newEnemyHp, bool ended, bool playerWon, string message) Attack(string playerId, int damage)
    {
        if (!IsActive)
        {
            return (PlayerHp, EnemyHp, true, false, "战斗不存在或已结束");
        }

        if (PlayerId != playerId)
        {
            return (PlayerHp, EnemyHp, false, false, "玩家不匹配");
        }

        var newEnemyHp = Math.Max(0, EnemyHp - damage);
        var enemyDamage = _random.Next(10, 25);
        var newPlayerHp = Math.Max(0, PlayerHp - enemyDamage);

        var ended = newPlayerHp <= 0 || newEnemyHp <= 0;
        var playerWon = ended && newEnemyHp <= 0 && newPlayerHp > 0;

        PlayerHp = newPlayerHp;
        EnemyHp = newEnemyHp;
        IsActive = !ended;
        LastActiveUtc = TimeManager.UtcNow;

        string msg;
        if (ended)
        {
            msg = playerWon ? "战斗胜利" : "战斗失败";
        }
        else
        {
            msg = $"造成 {damage} 伤害，敌人反击 {enemyDamage} 伤害";
        }

        return (newPlayerHp, newEnemyHp, ended, playerWon, msg);
    }
}
