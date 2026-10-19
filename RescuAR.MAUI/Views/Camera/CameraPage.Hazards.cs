using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Models;

namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private string? routeHazardNotice;
    private long routeHazardNoticeVersion;

    private void ClearRouteHazardNotice()
    {
        long version = Interlocked.Increment(ref routeHazardNoticeVersion);
        Dispatcher.Dispatch(() =>
        {
            if (version != routeHazardNoticeVersion) return;
            routeHazardNotice = null;
            RefreshEmergencyStatusBanner();
        });
    }

    private void UpdateRouteHazardNotice(RouteResult route,
        HazardReroutingService.HazardRouteAssessment assessment, bool findingAlternative = false)
    {
        if (!_hazardReroutingService.IsCurrentAssessment(assessment)) return;
        string? notice = assessment.IsUnsafe
            ? findingAlternative || dynamicRerouteInProgress && lastRerouteResult == "Requesting"
                ? "Finding a safer route…"
                : _hazardReroutingService.IsUnavailable(assessment)
                    ? "No safer route found. Check the map."
                    : "Hazard on this route. Check the map."
            : assessment.EscapingHazard is not null
                ? "Leaving a reported hazard area. Stay alert." : null;
        long destinationVersion = activeDestinationVersion;
        long version = Interlocked.Increment(ref routeHazardNoticeVersion);
        Dispatcher.Dispatch(() =>
        {
            if (version != routeHazardNoticeVersion || !ReferenceEquals(activeRoute, route) ||
                activeDestinationVersion != destinationVersion || safeZoneConfirmed ||
                !_hazardReroutingService.IsCurrentAssessment(assessment)) return;
            routeHazardNotice = notice;
            RefreshEmergencyStatusBanner();
        });
    }

    private bool ApplyRouteHazardStatusBanner()
    {
        if (routeHazardNotice is null) return false;
        cameraModeStatusBanner.BackgroundColor = Color.FromArgb("#FFF4DE");
        cameraModeStatusBanner.Stroke = new SolidColorBrush(Color.FromArgb("#E7B85D"));
        cameraModeStatusTitleLabel.TextColor = Color.FromArgb("#8A4B00");
        cameraModeStatusSubtitleLabel.TextColor = Color.FromArgb("#7B5700");
        cameraModeStatusTitleLabel.Text = "Route hazard reported";
        cameraModeStatusSubtitleLabel.Text = routeHazardNotice;
        cameraModeStatusSubtitleLabel.LineBreakMode = LineBreakMode.WordWrap;
        return true;
    }
}
