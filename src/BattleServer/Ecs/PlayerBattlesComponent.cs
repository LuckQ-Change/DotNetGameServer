using Core.Ecs;

namespace BattleServer.Ecs;

public class PlayerBattlesComponent : IEntityComponent
{
    private readonly HashSet<string> _activeBattleIds = new();

    public IReadOnlyCollection<string> ActiveBattleIds => _activeBattleIds;

    public void Add(string battleId)
    {
        if (!string.IsNullOrEmpty(battleId))
            _activeBattleIds.Add(battleId);
    }

    public bool Remove(string battleId)
    {
        return !string.IsNullOrEmpty(battleId) && _activeBattleIds.Remove(battleId);
    }
}