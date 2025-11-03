using Core;
using DotNetGameServer;
using Google.Protobuf;
using Core.Ecs;
using LogicServer.SignIn.Ecs;

namespace LogicServer.SignIn.Handlers;

public class QuerySignInStatusMessageHandler : IStrongMessageHandler<QuerySignInStatusRequest, QuerySignInStatusResponse>
{
    public Task<QuerySignInStatusResponse?> HandleAsync(QuerySignInStatusRequest request, IServerContext context)
    {
        // 基于上下文身份的访问控制示例：要求令牌已校验且用户匹配
        if (context.Auth == null)
        {
            context.Log($"[签到][AUTH] 访问拒绝: remote={context.RemoteAddress} user={request.UserId} code=AUTH_REQUIRED");
            return Task.FromResult<QuerySignInStatusResponse?>(new QuerySignInStatusResponse { Success = false, Message = "AUTH_REQUIRED" });
        }
        if (!string.Equals(context.Auth.UserId, request.UserId, StringComparison.Ordinal))
        {
            context.Log($"[签到][AUTH] 用户不匹配: remote={context.RemoteAddress} user={request.UserId} authed={context.Auth.UserId} code=FORBIDDEN");
            return Task.FromResult<QuerySignInStatusResponse?>(new QuerySignInStatusResponse { Success = false, Message = "FORBIDDEN" });
        }

        var ecs = context.GetComponent<EcsWorldComponent>();
        if (ecs == null)
        {
            var fail = new QuerySignInStatusResponse { Success = false, Message = "ECS_NOT_FOUND" };
            return Task.FromResult<QuerySignInStatusResponse?>(fail);
        }

        var entity = ecs.World.GetOrCreateEntity(request.UserId);
        var comp = entity.GetComponent<SignInStatusComponent>() ?? entity.AddComponent(new SignInStatusComponent());
        var status = comp.GetStatus();
        var resp = new QuerySignInStatusResponse { Success = true, Message = "查询成功", CurrentDay = status.CurrentDay };
        foreach (var r in status.Rewards)
        {
            resp.Rewards.Add(new DayReward { Day = r.Day, RewardId = r.RewardId, Amount = r.Amount, Claimed = r.Claimed });
        }
        context.Log($"[签到][ECS] 查询用户={request.UserId} 当前第 {resp.CurrentDay} 天");
        return Task.FromResult<QuerySignInStatusResponse?>(resp);
    }
}