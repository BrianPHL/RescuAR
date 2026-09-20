using RescuAR.MAUI.Services.Navigation;

namespace RescuAR.Tests;

public sealed class IdempotentReleaseLeaseTests
{
    [Fact]
    public void ConcurrentDisposeReleasesOnce()
    {
        int releases = 0;
        using IdempotentReleaseLease lease = new(() => Interlocked.Increment(ref releases));
        Parallel.For(0, 16, _ => lease.Dispose());
        Assert.Equal(1, releases);
    }
}
