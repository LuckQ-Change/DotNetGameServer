using Core;
using DotNetGameServer;
using Google.Protobuf;
using Core.Ecs;
using LogicServer.SignIn.Ecs;

namespace LogicServer.SignIn.Handlers;

public class ClaimSignInRewardMessageHandler : IStrongMessageHandler<ClaimSignInRewardRequest, ClaimSignInRewardResponse>
{
    public Task<ClaimSignInRewardResponse?> HandleAsync(ClaimSignInRewardRequest request, IServerContext context)
    {
        // 基于上下文身份的访问控制示例：要求令牌已校验且用户匹配
        if (context.Auth == null)
        {
            context.Log($"[签到][AUTH] 访问拒绝: remote={context.RemoteAddress} user={request.UserId} code=AUTH_REQUIRED");
            return Task.FromResult<ClaimSignInRewardResponse?>(new ClaimSignInRewardResponse { Success = false, Message = "AUTH_REQUIRED" });
        }
        if (!string.Equals(context.Auth.UserId, request.UserId, StringComparison.Ordinal))
        {
            context.Log($"[签到][AUTH] 用户不匹配: remote={context.RemoteAddress} user={request.UserId} authed={context.Auth.UserId} code=FORBIDDEN");
            return Task.FromResult<ClaimSignInRewardResponse?>(new ClaimSignInRewardResponse { Success = false, Message = "FORBIDDEN" });
        }

        var ecs = context.GetComponent<EcsWorldComponent>();
        if (ecs == null)
        {
            var fail = new ClaimSignInRewardResponse { Success = false, Message = "ECS_NOT_FOUND" };
            return Task.FromResult<ClaimSignInRewardResponse?>(fail);
        }

        var entity = ecs.World.GetOrCreateEntity(request.UserId);
        var comp = entity.GetComponent<SignInStatusComponent>() ?? entity.AddComponent(new SignInStatusComponent());
        var result = comp.Claim(request.Day);
        var resp = new ClaimSignInRewardResponse { Success = result.Success, Message = result.Message };
        if (result.Success && result.Reward != null)
        {
            resp.Reward = new DayReward { Day = result.Reward.Day, RewardId = result.Reward.RewardId, Amount = result.Reward.Amount, Claimed = result.Reward.Claimed };
        }
        context.Log($"[签到][ECS] 用户={request.UserId} 领取第 {request.Day} 天：{resp.Success}, {resp.Message}");
        return Task.FromResult<ClaimSignInRewardResponse?>(resp);
    }
}