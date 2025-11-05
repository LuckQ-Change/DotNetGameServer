using LogicServer.SignIn.Models;
using System.Collections.Concurrent;

namespace LogicServer.SignIn.Components;

public class SimpleSignInComponent : ISignInComponent
{
    private sealed class UserState
    {
        public DateOnly CycleStart { get; set; }
        public HashSet<int> ClaimedDays { get; } = new();
    }

    private readonly ConcurrentDictionary<string, UserState> _states = new();

    private static readonly IReadOnlyList<(string RewardId, int Amount)> RewardTable = new List<(string, int)>
    {
        ("coin", 10), ("coin", 20), ("gem", 1), ("coin", 30), ("ticket", 1), ("coin", 50), ("gem", 2)
    };

    public string Name => "SimpleSignIn";

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DestroyAsync()
    {
        _states.Clear();
        return Task.CompletedTask;
    }

    public Task<SignInStatus> GetStatusAsync(string userId)
    {
        var state = GetOrInitState(userId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        ResetCycleIfNeeded(state, today);

        var currentDay = ComputeCurrentDay(state, today);
        var rewards = new List<SignInReward>(7);
        for (int day = 1; day <= 7; day++)
        {
            var (rid, amt) = RewardTable[day - 1];
            var claimed = state.ClaimedDays.Contains(day);
            rewards.Add(new SignInReward(day, rid, amt, claimed));
        }

        return Task.FromResult(new SignInStatus(currentDay, rewards));
    }

    public Task<ClaimResult> ClaimAsync(string userId, int day)
    {
        if (day < 1 || day > 7)
            return Task.FromResult(new ClaimResult(false, "领取天数必须在 1-7 范围", null));

        var state = GetOrInitState(userId);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        ResetCycleIfNeeded(state, today);
        var currentDay = ComputeCurrentDay(state, today);

        if (day != currentDay)
            return Task.FromResult(new ClaimResult(false, $"仅允许领取当天（当前为第 {currentDay} 天）", null));

        if (state.ClaimedDays.Contains(day))
            return Task.FromResult(new ClaimResult(false, "当天奖励已领取", null));

        state.ClaimedDays.Add(day);
        var (rid, amt) = RewardTable[day - 1];
        var reward = new SignInReward(day, rid, amt, true);
        return Task.FromResult(new ClaimResult(true, "领取成功", reward));
    }

    private UserState GetOrInitState(string userId)
    {
        return _states.GetOrAdd(userId, _ => new UserState
        {
            CycleStart = DateOnly.FromDateTime(DateTime.UtcNow.Date)
        });
    }

    private static void ResetCycleIfNeeded(UserState state, DateOnly today)
    {
        var days = today.DayNumber - state.CycleStart.DayNumber;
        if (days >= 7)
        {
            state.CycleStart = today;
            state.ClaimedDays.Clear();
        }
    }

    private static int ComputeCurrentDay(UserState state, DateOnly today)
    {
        var days = today.DayNumber - state.CycleStart.DayNumber;
        if (days < 0) days = 0;
        var current = days + 1;
        if (current > 7) current = 7;
        return current;
    }
}