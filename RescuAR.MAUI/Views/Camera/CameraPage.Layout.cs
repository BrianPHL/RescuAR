namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private bool arrangingArOverlays;

    private void ConfigureCompactArLayout()
    {
        cameraWorkspace.SizeChanged += (_, _) => ArrangeArOverlays();
    }

    private void ArrangeArOverlays()
    {
        if (arrangingArOverlays || cameraWorkspace.Width <= 0 || cameraWorkspace.Height <= 0) return;
        arrangingArOverlays = true;
        try
        {
            double width = cameraWorkspace.Width;
            bool landscape = width > cameraWorkspace.Height;
            double cardWidth = landscape ? Math.Min(360, width * .48) : Math.Min(480, width - 16);
            // Natural layout follows wrapped text and visibility in one pass.
            // Measured heights from the previous frame must not set absolute tops.
            arNavigationOverlays.WidthRequest = cardWidth;
            arNavigationOverlays.HorizontalOptions = landscape ? LayoutOptions.Start : LayoutOptions.Center;
            foreach (var card in new Border[] { cameraModeStatusBanner, lowLightFallbackBanner,
                arTrackingStatusBanner, turnGuidancePanel, routeLocatorPanel })
            {
                card.MaximumWidthRequest = cardWidth;
                card.HorizontalOptions = LayoutOptions.Fill;
                card.VerticalOptions = LayoutOptions.Start;
                card.Margin = 0;
            }
            double buttonWidth = Math.Clamp(width - 144, 120, 204);
            foreach (var button in new Button[] { exploreSafeZonesButton, endNavigationButton, floodSimulationButton })
            {
                button.WidthRequest = buttonWidth;
                button.FontSize = width < 360 ? 12 : 14;
            }
            routeLocatorLabel.FontSize = HasArrivalNoticePriority() ? 13 : 12;
        }
        finally { arrangingArOverlays = false; }
    }
}
