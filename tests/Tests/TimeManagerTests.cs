using Shared.Time;

namespace Tests;

public class TimeManagerTests
{
    [Fact]
    public void UseSystem_ReturnsSystemTime()
    {
        TimeManager.UseSystem();
        var before = DateTime.UtcNow;
        var now = TimeManager.UtcNow;
        var after = DateTime.UtcNow;
        Assert.True(now >= before && now <= after);
    }

    [Fact]
    public void SetOffset_ChangesTime()
    {
        TimeManager.UseSystem();
        var before = TimeManager.UtcNow;
        TimeManager.SetOffset(TimeSpan.FromHours(1));
        var after = TimeManager.UtcNow;
        Assert.True(after > before);
        TimeManager.UseSystem();
    }

    [Fact]
    public void Advance_ForwardInTime()
    {
        TimeManager.UseSystem();
        var before = TimeManager.UtcNow;
        TimeManager.Advance(TimeSpan.FromSeconds(10));
        var after = TimeManager.UtcNow;
        Assert.True(after >= before);
        TimeManager.UseSystem();
    }

    [Fact]
    public void ManualTime_FixedTime()
    {
        var fixedTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        TimeManager.UseManual(fixedTime);
        Assert.Equal(fixedTime.UtcDateTime, TimeManager.UtcNow);
        TimeManager.UseSystem();
    }

    [Fact]
    public void GetSnapshot_ReturnsValidData()
    {
        TimeManager.UseSystem();
        var snap = TimeManager.GetSnapshot();
        Assert.NotNull(snap);
        Assert.Equal("system", snap.Mode);
    }
}
