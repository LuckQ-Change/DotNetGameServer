using Core.Ecs;
using LogicServer.SignIn.Components;
using LogicServer.SignIn.Models;

namespace LogicServer.SignIn.Ecs;

public class SignInStatusComponent : IEntityComponent
{
    private DateOnly _cycleStart = DateOnly.FromDateTime(DateTime.UtcNow.Date);
    private readonly HashSet<int> _claimedDays = new();

    private static readonly IReadOnlyList<(string RewardId, int Amount)> RewardTable = new List<(string, int)>
    {
        ("coin", 10), ("coin", 20), ("gem", 1), ("coin", 30), ("ticket", 1), ("coin", 50), ("gem", 2)
    };

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DestroyAsync() => Task.CompletedTask;

    public SignInStatus GetStatus()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        ResetCycleIfNeeded(today);
        var currentDay = ComputeCurrentDay(today);
        var rewards = new List<SignInReward>(7);
        for (int day = 1; day <= 7; day++)
        {
            var (rid, amt) = RewardTable[day - 1];
            var claimed = _claimedDays.Contains(day);
            rewards.Add(new SignInReward(day, rid, amt, claimed));
        }
        return new SignInStatus(currentDay, rewards);
    }

    public ClaimResult Claim(int day)
    {
        if (day < 1 || day > 7)
            return new ClaimResult(false, "领取天数必须在 1-7 范围", null);

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        ResetCycleIfNeeded(today);
        var currentDay = ComputeCurrentDay(today);

        if (day != currentDay)
            return new ClaimResult(false, $"仅允许领取当天（当前为第 {currentDay} 天）", null);

        if (_claimedDays.Contains(day))
            return new ClaimResult(false, "当天奖励已领取", null);

        _claimedDays.Add(day);
        var (rid, amt) = RewardTable[day - 1];
        var reward = new SignInReward(day, rid, amt, true);
        return new ClaimResult(true, "领取成功", reward);
    }

    private void ResetCycleIfNeeded(DateOnly today)
    {
        var days = today.DayNumber - _cycleStart.DayNumber;
        if (days >= 7)
        {
            _cycleStart = today;
            _claimedDays.Clear();
        }
    }

    private int ComputeCurrentDay(DateOnly today)
    {
        var days = today.DayNumber - _cycleStart.DayNumber;
        if (days < 0) days = 0;
        var current = days + 1;
        if (current > 7) current = 7;
        return current;
    }
}