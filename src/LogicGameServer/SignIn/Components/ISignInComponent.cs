using Core;
using LogicServer.SignIn.Models;

namespace LogicServer.SignIn.Components;

/// <summary>
/// 七日签到组件接口（演示用，无持久化）
/// </summary>
public interface ISignInComponent : IServerComponent
{
    Task<SignInStatus> GetStatusAsync(string userId);
    Task<ClaimResult> ClaimAsync(string userId, int day);
}

/// <summary>
/// 领取结果
/// </summary>
public record ClaimResult(bool Success, string Message, SignInReward? Reward);