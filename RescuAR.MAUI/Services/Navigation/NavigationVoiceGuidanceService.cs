using RescuAR.Navigation.Guidance;

namespace RescuAR.MAUI.Services.Navigation;

public interface INavigationSpeech
{
    Task SpeakAsync(string text, CancellationToken cancellationToken);
}

/// <summary>One cancellable speaker; only meaningful guidance changes are spoken.</summary>
public sealed class NavigationVoiceGuidanceService(INavigationSpeech speech)
{
    private readonly object sync = new();
    private readonly SemaphoreSlim speakerGate = new(1, 1);
    private CancellationTokenSource? pending;
    private object? route;
    private Cue? lastCue;
    private DateTimeOffset lastSpokenAt;
    private bool enabled;
    private bool active;
    private string? lastError;

    public bool Enabled { get { lock (sync) return enabled; } }
    public string? LastError { get { lock (sync) return lastError; } }

    public void SetEnabled(bool value)
    {
        lock (sync)
        {
            enabled = value;
            lastError = null;
            lastCue = null;
            CancelPending();
        }
    }

    public void SetActive(bool value)
    {
        lock (sync)
        {
            if (active == value) return;
            active = value;
            lastCue = null;
            CancelPending();
        }
    }

    public void ResetRoute()
    {
        lock (sync)
        {
            route = null;
            lastCue = null;
            lastError = null;
            CancelPending();
        }
    }

    public async Task UpdateAsync(object routeIdentity,
        PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance,
        double progressMeters, string text, DateTimeOffset now)
    {
        CancellationTokenSource request;
        lock (sync)
        {
            if (!enabled || !active || lastError is not null || !guidance.IsAvailable ||
                string.IsNullOrWhiteSpace(text)) return;
            if (!ReferenceEquals(route, routeIdentity))
            {
                route = routeIdentity;
                lastCue = null;
            }
            double distance = guidance.DistanceToTurnMeters;
            bool turn = RouteDirectionsService.IsTurn(guidance.Instruction);
            int band = turn && double.IsFinite(distance) ? distance <= 8 ? 0 : distance <= 20 ? 1 : 2 : 2;
            double target = turn && double.IsFinite(distance) ? progressMeters + distance : double.NaN;
            string orientation = guidance.Instruction == PedestrianTurnGuidanceService.TurnInstruction.FollowRoute
                ? guidance.DisplayText : "";
            Cue cue = new(guidance.Instruction, target, band, orientation);
            bool same = lastCue is { } previous && previous.Instruction == cue.Instruction &&
                previous.Band == cue.Band && previous.Orientation == cue.Orientation &&
                (!double.IsFinite(target) || Math.Abs(previous.Target - target) < 15);
            if (same || (lastCue is not null && now - lastSpokenAt < TimeSpan.FromSeconds(5))) return;
            lastCue = cue;
            lastSpokenAt = now;
            CancelPending();
            pending = request = new CancellationTokenSource();
        }

        bool entered = false;
        try
        {
            await speakerGate.WaitAsync(request.Token).ConfigureAwait(false);
            entered = true;
            request.Token.ThrowIfCancellationRequested();
            await speech.SpeakAsync(text, request.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception)
        {
            lock (sync)
                if (ReferenceEquals(pending, request) && !request.IsCancellationRequested)
                    lastError = "Voice unavailable. Use the written directions.";
        }
        finally
        {
            if (entered) speakerGate.Release();
            lock (sync)
                if (ReferenceEquals(pending, request)) pending = null;
            request.Dispose();
        }
    }

    // Each observed request disposes its own source after its speaker has stopped.
    private void CancelPending() => pending?.Cancel();
    private sealed record Cue(PedestrianTurnGuidanceService.TurnInstruction Instruction,
        double Target, int Band, string Orientation);
}
