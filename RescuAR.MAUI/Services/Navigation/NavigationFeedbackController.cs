using RescuAR.Navigation.Guidance;

namespace RescuAR.MAUI.Services.Navigation;

public sealed record NavigationFeedbackFrame(long Trip, object? RouteIdentity, bool Active, bool Qualified,
    bool ArrivalConfirmed, PedestrianTurnGuidanceService.TurnGuidanceSnapshot Guidance,
    double ProgressMeters, string Text, string DestinationName, DateTimeOffset Now);

/// <summary>Display-only consumer. Call from the page dispatcher; speech is serialized by its cancellable speaker.</summary>
public sealed class NavigationFeedbackController(INavigationSpeech speech, Action<TimeSpan> vibrate)
{
    private readonly NavigationVoiceGuidanceService voice = new(speech);
    private readonly NavigationHapticGuidanceService haptics = new();
    private object owner = new();
    private object? route;
    private long trip;
    private bool arrived;
    private bool hapticsEnabled;
    private string? hapticsError;
    public bool VoiceEnabled => voice.Enabled;
    public string? LastError => voice.LastError ?? hapticsError;

    public void Configure(bool spoken, bool pulses)
    {
        if (voice.Enabled != spoken) voice.SetEnabled(spoken);
        if (hapticsEnabled != pulses) { hapticsEnabled = pulses; hapticsError = null; haptics.Reset(); }
    }
    public void Stop() => voice.SetActive(false);
    public async Task UpdateAsync(NavigationFeedbackFrame frame)
    {
        bool changed = trip != frame.Trip || (!frame.ArrivalConfirmed && !ReferenceEquals(route, frame.RouteIdentity));
        if (changed)
        {
            trip = frame.Trip; route = frame.RouteIdentity; owner = new(); arrived = false;
            voice.ResetRoute(); haptics.Reset();
        }
        bool active = frame.Active && frame.Trip > 0 && (frame.ArrivalConfirmed ||
            (frame.Qualified && frame.RouteIdentity is not null && frame.Guidance.IsAvailable &&
             double.IsFinite(frame.ProgressMeters) && frame.ProgressMeters >= 0));
        if (frame.ArrivalConfirmed && !arrived) { voice.ResetRoute(); haptics.Reset(); arrived = true; }
        voice.SetActive(active);
        if (!active) return;
        var guidance = frame.ArrivalConfirmed
            ? new PedestrianTurnGuidanceService.TurnGuidanceSnapshot(true, PedestrianTurnGuidanceService.TurnInstruction.Arrive, 0, 0, 0, "Arrival confirmed")
            : frame.Guidance;
        string text = frame.ArrivalConfirmed ? $"Arrival confirmed at {frame.DestinationName}." : frame.Text;
        if (!frame.ArrivalConfirmed && hapticsError is null && haptics.ShouldPulse(owner, hapticsEnabled, guidance, frame.ProgressMeters, frame.Now))
        {
            try { vibrate(TimeSpan.FromMilliseconds(180)); }
            catch { hapticsError = "Turn vibration unavailable. Use the written directions."; }
        }
        await voice.UpdateAsync(owner, guidance, frame.ArrivalConfirmed ? 0 : frame.ProgressMeters, text, frame.Now);
    }
}
