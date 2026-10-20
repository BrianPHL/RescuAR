namespace RescuAR.App.Views.Camera;

public partial class CameraPage
{
    private bool arrangingArOverlays;

    private void ConfigureCompactArLayout()
    {
        cameraWorkspace.SizeChanged += (_, _) => ArrangeArOverlays();
        foreach (var view in new VisualElement[] { cameraModeStatusBanner, arTrackingStatusBanner,
            lowLightFallbackBanner, turnGuidancePanel, routeLocatorPanel })
            view.SizeChanged += (_, _) => ArrangeArOverlays();
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
            double top = 8;
            foreach (var card in new Border[] { cameraModeStatusBanner, lowLightFallbackBanner,
                arTrackingStatusBanner, turnGuidancePanel, routeLocatorPanel })
            {
                card.MaximumWidthRequest = cardWidth;
                card.HorizontalOptions = landscape ? LayoutOptions.Start : LayoutOptions.Center;
                card.VerticalOptions = LayoutOptions.Start;
                card.Margin = new Thickness(8, top, 8, 0);
                if (card.IsVisible) top += Math.Max(card.Height, card == turnGuidancePanel ? 64 : 34) + 6;
            }
            double buttonWidth = Math.Clamp(width - 144, 120, 204);
            foreach (var button in new Button[] { exploreSafeZonesButton, endNavigationButton, floodSimulationButton, startRoutingButton })
            {
                button.WidthRequest = buttonWidth;
                button.FontSize = width < 360 ? 12 : 14;
            }
            routeLocatorLabel.FontSize = HasArrivalNoticePriority() ? 13 : 12;
        }
        finally { arrangingArOverlays = false; }
    }
}
