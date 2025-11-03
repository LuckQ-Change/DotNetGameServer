using Core;
using DotNetGameServer;
using LogicServer.SignIn.Components;
using LogicServer.SignIn.Handlers;
using Core.Ecs;
using Core.Db;

namespace LogicServer;

public class LogicGameServer : BaseGameServer
{
    private readonly GameDbContext _dbContext;

    public LogicGameServer(GameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override void Start()
    {
        RegisterStrongMessageHandler<QuerySignInStatusRequest, QuerySignInStatusResponse>(new QuerySignInStatusMessageHandler());
        RegisterStrongMessageHandler<ClaimSignInRewardRequest, ClaimSignInRewardResponse>(new ClaimSignInRewardMessageHandler());
        // 注册 ECS 世界组件
        AddComponent<EcsWorldComponent>(new EcsWorldComponent());
        AddComponent<Core.Components.HeartbeatClientComponent>(
            new Core.Components.HeartbeatClientComponent(
                message => Log(message),
                type: "logic",
                portProvider: () => ListeningPort,
                playerCountProvider: null,
                statusProvider: () => "online",
                baseDir: AppContext.BaseDirectory
            )
        );
    }

    /// <summary>
    /// 工厂方法：创建 <see cref="LogicGameServer"/> 实例。
    /// </summary>
    /// <param name="log">日志输出委托。</param>
    /// <returns>逻辑服务器实例。</returns>
    public static LogicGameServer Create(Action<string> log) => new LogicGameServer(log);
}