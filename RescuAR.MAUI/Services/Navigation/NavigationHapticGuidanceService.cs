using RescuAR.Navigation.Guidance;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>One short pulse near each confirmed turn; never pulses for unavailable guidance.</summary>
public sealed class NavigationHapticGuidanceService
{
    private object? route;
    private double lastTarget = double.NaN;
    private DateTimeOffset lastPulse;

    public bool ShouldPulse(object routeIdentity, bool enabled,
        PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance, double progress, DateTimeOffset now)
    {
        if (!enabled || !guidance.IsAvailable || !RouteDirectionsService.IsTurn(guidance.Instruction) ||
            !double.IsFinite(progress) || !double.IsFinite(guidance.DistanceToTurnMeters) ||
            guidance.DistanceToTurnMeters is < 0 or > 20) return false;
        if (!ReferenceEquals(route, routeIdentity)) { route = routeIdentity; lastTarget = double.NaN; lastPulse = default; }
        double target = progress + guidance.DistanceToTurnMeters;
        if (double.IsFinite(lastTarget) && (Math.Abs(target - lastTarget) < 15 || now - lastPulse < TimeSpan.FromSeconds(5))) return false;
        lastTarget = target;
        lastPulse = now;
        return true;
    }
}
