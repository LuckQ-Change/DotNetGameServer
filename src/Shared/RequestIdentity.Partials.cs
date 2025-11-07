// 该文件用于为生成的 Protobuf 请求类型添加统一的用户标识接口实现。
// 移除了空的占位类以满足分析器规则（S2094：禁止空类）。

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