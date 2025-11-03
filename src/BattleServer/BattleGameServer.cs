using Core;
using Core.Ecs;
using DotNetGameServer;
using Microsoft.Extensions.Logging;

namespace BattleServer;

/// <summary>
/// 战斗游戏服务器
/// </summary>
public class BattleGameServer : BaseGameServer
{
    /// <summary>
    /// 初始化战斗服务器并注册强类型消息与 ECS 系统。
    /// </summary>
    /// <param name="logger">日志输出委托。</param>
    public BattleGameServer(Action<string> logger) : base("Battle", logger)
    {
        RegisterStrongMessageHandler<StartBattleRequest, StartBattleResponse>(new StartBattleMessageHandler());
        RegisterStrongMessageHandler<AttackRequest, AttackResponse>(new AttackMessageHandler());

        var ecs = new EcsWorldComponent();
        AddComponent<EcsWorldComponent>(ecs);
        // 注册战斗 GC 清理系统（TTL 30 分钟）
        _ = ecs.World.RegisterSystemAsync(new Ecs.BattleGcSystem(TimeSpan.FromMinutes(30)));
        AddComponent<Core.Components.HeartbeatClientComponent>(
            new Core.Components.HeartbeatClientComponent(
                message => Log(message),
                type: "battle",
                portProvider: () => ListeningPort,
                playerCountProvider: null,
                statusProvider: () => "online",
                baseDir: AppContext.BaseDirectory
            )
        );
    }

    public BattleGameServer(ILogger logger) : base("Battle", logger)
    {
        RegisterStrongMessageHandler<StartBattleRequest, StartBattleResponse>(new StartBattleMessageHandler());
        RegisterStrongMessageHandler<AttackRequest, AttackResponse>(new AttackMessageHandler());

        var ecs = new EcsWorldComponent();
        AddComponent<EcsWorldComponent>(ecs);
        _ = ecs.World.RegisterSystemAsync(new Ecs.BattleGcSystem(TimeSpan.FromMinutes(30)));
        AddComponent<Core.Components.HeartbeatClientComponent>(
            new Core.Components.HeartbeatClientComponent(
                message => Log(message),
                type: "battle",
                portProvider: () => ListeningPort,
                playerCountProvider: null,
                statusProvider: () => "online",
                baseDir: AppContext.BaseDirectory
            )
        );
    }
}