using Core.Components;

using Xunit;

namespace Tests;

public sealed class TokenRevocationTests
{
    [Fact]
    public void IsRevoked_RemovesOnlyTheExpiredTokenThatWasChecked()
    {
        var store = new InMemoryTokenRevocationStore();
        store.Revoke("expired", DateTimeOffset.UtcNow.AddMinutes(-1));
        store.Revoke("active", DateTimeOffset.UtcNow.AddMinutes(5));

        Assert.False(store.IsRevoked("expired"));
        Assert.True(store.IsRevoked("active"));
        Assert.False(store.IsRevoked("missing"));
    }

    [Fact]
    public async Task CleanupTimer_StartsAndStopsWithTheComponent()
    {
        var store = new InMemoryTokenRevocationStore();
        await store.InitializeAsync();
        store.Revoke("active", DateTimeOffset.UtcNow.AddMinutes(5));

        await store.DestroyAsync();

        Assert.True(store.IsRevoked("active"));
    }
}
