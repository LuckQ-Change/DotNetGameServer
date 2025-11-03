using Core;

namespace LoginServer.SignIn.Models;

/// <summary>
/// 签到奖励模型（演示用）
/// </summary>
public record SignInReward(int Day, string RewardId, int Amount, bool Claimed);