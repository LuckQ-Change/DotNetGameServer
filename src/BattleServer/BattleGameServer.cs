using Core;
using Core.Ecs;
using DotNetGameServer;
using Microsoft.Extensions.Logging;
using Core.Db;

namespace BattleServer;

public class BattleGameServer : BaseGameServer
{
    private readonly GameDbContext _dbContext;

    public BattleGameServer(GameDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override void Start()
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