using Core;
using System.Collections.Generic;

namespace LoginServer.SignIn.Models;

/// <summary>
/// 签到状态（当前天与七天奖励的状态快照）
/// </summary>
public record SignInStatus(int CurrentDay, IReadOnlyList<SignInReward> Rewards);