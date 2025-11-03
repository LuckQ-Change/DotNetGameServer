namespace Shared
{
    /// <summary>
    /// 为生成的 Protobuf 请求类型添加统一的用户标识提取接口实现。
    /// </summary>
    public static class RequestIdentityPartials
    {
        // 此类型仅用于放置局部类定义，不包含逻辑。
    }
}

namespace DotNetGameServer
{
    // 逻辑服：查询签到状态
    public sealed partial class QuerySignInStatusRequest : Shared.IUserIdentifiedRequest
    {
        public string? GetUserIdOrNull() => string.IsNullOrWhiteSpace(UserId) ? null : UserId;
    }

    // 逻辑服：领取签到奖励
    public sealed partial class ClaimSignInRewardRequest : Shared.IUserIdentifiedRequest
    {
        public string? GetUserIdOrNull() => string.IsNullOrWhiteSpace(UserId) ? null : UserId;
    }

    // 战斗服：开始战斗
    public sealed partial class StartBattleRequest : Shared.IUserIdentifiedRequest
    {
        public string? GetUserIdOrNull() => string.IsNullOrWhiteSpace(PlayerId) ? null : PlayerId;
    }

    // 战斗服：攻击
    public sealed partial class AttackRequest : Shared.IUserIdentifiedRequest
    {
        public string? GetUserIdOrNull() => string.IsNullOrWhiteSpace(PlayerId) ? null : PlayerId;
    }
}