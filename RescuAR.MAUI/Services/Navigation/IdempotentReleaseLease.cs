namespace RescuAR.MAUI.Services.Navigation;

internal sealed class IdempotentReleaseLease : IDisposable
{
    private Action? release;

    internal IdempotentReleaseLease(Action release)
    {
        ArgumentNullException.ThrowIfNull(release);
        this.release = release;
    }

    public void Dispose() => Interlocked.Exchange(ref release, null)?.Invoke();
}
