using Core.Components;

using Xunit;

namespace Tests;

public sealed class ServerLoadTests
{
    [Fact]
    public void PlayerCount_IsClampedToCapacity()
    {
        var provider = new MutableServerLoadProvider(100);
        provider.SetPlayerCount(150);
        Assert.Equal(100, provider.PlayerCount);

        provider.SetCapacity(60);
        Assert.Equal(60, provider.Capacity);
        Assert.Equal(60, provider.PlayerCount);
    }
}
