using Core;
using DotNetGameServer;
using LogicServer.SignIn.Components;
using LogicServer.SignIn.Handlers;
using Core.Ecs;

namespace LogicServer;

/// <summary>
/// 逻辑服务器
/// </summary>
public class LogicGameServer : BaseGameServer
{
    /// <summary>
    /// 初始化逻辑服务器并注册相关强类型消息与组件。
    /// </summary>
    /// <param name="log">日志输出委托。</param>
    public LogicGameServer(Action<string> log) : base("Logic", log)
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