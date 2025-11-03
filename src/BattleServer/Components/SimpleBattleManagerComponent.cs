namespace BattleServer;

/// <summary>
/// 简单战斗管理器实现
/// </summary>
public class SimpleBattleManagerComponent : IBattleManagerComponent
{
    private readonly Dictionary<string, BattleInfo> _battles = new();
    private readonly Random _random = new();
    public string Name => "BattleManager";

    public Task InitializeAsync()
    {
        _battles.Clear();
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        _battles.Clear();
        return Task.CompletedTask;
    }

    public async Task<BattleInfo?> StartBattleAsync(string playerId, string battleType)
    {
        await Task.Delay(1); // 模拟异步操作
        
        var battleId = Guid.NewGuid().ToString("N")[..8];
        var playerHp = battleType switch
        {
            "easy" => 100,
            "normal" => 80,
            "hard" => 60,
            _ => 100
        };
        
        var enemyHp = battleType switch
        {
            "easy" => 50,
            "normal" => 80,
            "hard" => 120,
            _ => 50
        };
        
        var battle = new BattleInfo(battleId, playerId, battleType, playerHp, enemyHp, true);
        _battles[battleId] = battle;
        
        return battle;
    }

    public async Task<AttackResult?> ProcessAttackAsync(string battleId, string playerId, int damage)
    {
        await Task.Delay(1); // 模拟异步操作
        
        if (!_battles.TryGetValue(battleId, out var battle) || !battle.IsActive)
        {
            return new AttackResult("战斗不存在或已结束", 0, 0, true, false);
        }
        
        if (battle.PlayerId != playerId)
        {
            return new AttackResult("玩家不匹配", battle.PlayerHp, battle.EnemyHp, false, false);
        }
        
        // 玩家攻击敌人
        var newEnemyHp = Math.Max(0, battle.EnemyHp - damage);
        
        // 敌人反击
        var enemyDamage = _random.Next(10, 25);
        var newPlayerHp = Math.Max(0, battle.PlayerHp - enemyDamage);
        
        var battleEnded = newPlayerHp <= 0 || newEnemyHp <= 0;
        var playerWon = battleEnded && newEnemyHp <= 0 && newPlayerHp > 0;
        
        var updatedBattle = battle with 
        { 
            PlayerHp = newPlayerHp, 
            EnemyHp = newEnemyHp, 
            IsActive = !battleEnded 
        };
        
        _battles[battleId] = updatedBattle;
        
        if (battleEnded)
        {
            await EndBattleAsync(battleId);
        }
        
        var message = battleEnded 
            ? (playerWon ? "战斗胜利！" : "战斗失败！")
            : $"造成 {damage} 伤害，敌人反击 {enemyDamage} 伤害";
            
        return new AttackResult(message, newPlayerHp, newEnemyHp, battleEnded, playerWon);
    }

    public async Task<BattleInfo?> GetBattleAsync(string battleId)
    {
        await Task.Delay(1); // 模拟异步操作
        return _battles.TryGetValue(battleId, out var battle) ? battle : null;
    }

    public async Task EndBattleAsync(string battleId)
    {
        await Task.Delay(1); // 模拟异步操作
        if (_battles.TryGetValue(battleId, out var battle))
        {
            _battles[battleId] = battle with { IsActive = false };
        }
    }
}