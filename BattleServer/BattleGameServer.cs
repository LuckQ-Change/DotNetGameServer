using Core;

namespace BattleServer;

/// <summary>
/// 战斗游戏服务器
/// </summary>
public class BattleGameServer : BaseGameServer
{
    public BattleGameServer(Action<string> logger) : base("Battle", logger)
    {
        // 注册战斗相关的消息处理器
        RegisterMessageHandler(new StartBattleMessageHandler());
        RegisterMessageHandler(new AttackMessageHandler());
        
        // 添加战斗管理器组件
        AddComponent<IBattleManagerComponent>(new SimpleBattleManagerComponent());
    }
}