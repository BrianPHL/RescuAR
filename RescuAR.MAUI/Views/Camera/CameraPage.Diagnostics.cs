using RescuAR.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using RescuAR.AR;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Models;

namespace RescuAR.App.Views.Camera;

/// <summary>
/// Diagnostic-build-only camera controls. This file is removed from normal
/// compilation by RescuAR.MAUI.csproj and is included only when
/// RescuArDiagnosticBuild=true defines RESCUAR_DIAGNOSTICS.
/// </summary>
public partial class CameraPage
{
    private Button developerSafeZoneTestButton =
        null!;

    private Button developerTurnTestButton =
        null!;

    private Button developerRerouteTestButton =
        null!;

    private Button developerHazardRerouteTestButton =
        null!;

    private Button developerRoadLinesButton = null!;
    private double? roadDiagnosticAccuracyMeters;

    private const double RoadLineDiagnosticRadiusMeters = 40.0;

    private bool diagnosticConsentPromptShown;

    private static readonly bool EnableDeveloperOffRouteSimulation =
        true;

    private static readonly bool EnableDeveloperTurnSimulation =
        true;

    private static readonly bool EnableDeveloperSafeZoneValidation =
        true;

    private static readonly bool EnableDeveloperDynamicHazardValidation =
        true;

    private const double DeveloperSafeZoneTargetAheadMeters =
        20.0;

    private const double DeveloperHazardAheadMeters =
        45.0;

    private const double DeveloperHazardRadiusMeters =
        12.0;

    private const double DeveloperSimulatedCrossTrackMeters =
        50.0;

    private const double DeveloperSimulatedGpsAccuracyMeters =
        5.0;

    private bool developerHazardValidationArmed;
    private bool developerSafeZoneValidationArmed;

    private RescuAR.Navigation.Models.GeoCoordinate?
        developerSafeZoneTargetCoordinate;

    private double developerSafeZoneTargetProgressMeters =
        double.NaN;

    private void ConfigureDiagnosticControls()
    {
        developerSafeZoneTestButton =
            CreateDiagnosticButton(
                "DEV: Arm Safe Zone Test",
                "#EEF8F4",
                "#D5EDE3",
                "#0C6B43");

        developerSafeZoneTestButton.Clicked +=
            OnDeveloperSafeZoneTestClicked;

        developerTurnTestButton =
            CreateDiagnosticButton(
                "DEV: Validate Turn States");

        developerTurnTestButton.Clicked +=
            OnDeveloperTurnTestClicked;

        developerRerouteTestButton =
            CreateDiagnosticButton(
                "DEV: Simulate Off-Route Reroute");

        developerRerouteTestButton.Clicked +=
            OnDeveloperRerouteTestClicked;

        developerHazardRerouteTestButton =
            CreateDiagnosticButton(
                "DEV: Simulate Route Hazard",
                "#FFF4DE",
                "#F1D39B",
                "#8A4B00");

        developerHazardRerouteTestButton.Clicked +=
            OnDeveloperHazardRerouteTestClicked;

        developerRoadLinesButton = CreateDiagnosticButton(
            "DEV: Show GeoJSON roads (not directions)");
        developerRoadLinesButton.Clicked += OnDeveloperRoadLinesClicked;

        diagnosticNavigationControlsHost.Children.Add(
            developerSafeZoneTestButton);

        diagnosticNavigationControlsHost.Children.Add(
            developerTurnTestButton);

        diagnosticNavigationControlsHost.Children.Add(
            developerRerouteTestButton);

        diagnosticNavigationControlsHost.Children.Add(
            developerHazardRerouteTestButton);
        diagnosticNavigationControlsHost.Children.Add(developerRoadLinesButton);

    }

    private CancellationTokenSource? roadDiagnosticCancellation;

    private void CancelRoadDiagnosticWork()
    {
        roadDiagnosticCancellation?.Cancel();
        ARRouteRenderer.HideRoadDiagnostics();
        roadDiagnosticAccuracyMeters = null;
    }

    private async void OnDeveloperRoadLinesClicked(object? sender, EventArgs e)
    {
        if (ARRouteRenderer.CurrentRoadDiagnosticPlacement.Active)
        {
            CancelRoadDiagnosticWork();
            developerRoadLinesButton.Text = "DEV: Show GeoJSON roads (not directions)";
            RefreshArTrackingStatusBanner();
            UpdateTurnGuidance();
            return;
        }

        using var work = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        roadDiagnosticCancellation?.Cancel();
        roadDiagnosticCancellation = work;
        developerRoadLinesButton.IsEnabled = false;
        long session = ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration;
        try
        {
            if (!CanInspectRoads(session)) return;
            var reading = await TryGetRouteLocationAsync(work.Token);
            if (!IsCurrentRouteStartupLocationAcceptable(reading))
            {
                await DisplayAlert("GeoJSON roads",
                    "Wait for a recent GPS fix with accuracy of 30 m or better.", "OK");
                return;
            }
            var heading = await _headingAlignmentService.CaptureAsync(
                reading!.Coordinate, reading.AltitudeMeters, work.Token);
            if (!heading.HasValue || !heading.Value.IsAvailable || !heading.Value.IsStable)
            {
                if (CanInspectRoads(session))
                    await DisplayAlert("GeoJSON roads", "Hold the phone steady to align road data.", "OK");
                return;
            }
            var roads = await NavigationDataBootstrap.GetRoadFeaturesAsync(work.Token);
            if (!IsCurrentRouteStartupLocationAcceptable(reading))
                reading = await TryGetRouteLocationAsync(work.Token);
            work.Token.ThrowIfCancellationRequested();
            if (!CanInspectRoads(session)) return;
            if (!IsCurrentRouteStartupLocationAcceptable(reading))
            {
                await DisplayAlert("GeoJSON roads", "A recent GPS fix is required to place the overlay.", "OK");
                return;
            }
            var lines = NearbyRoadLineProjector.Project(roads, reading!.Coordinate,
                RoadLineDiagnosticRadiusMeters, heading.Value.MapToArYawDegrees);
            if (lines.Count == 0 || lines.Count > ARRouteRenderer.MaximumDiagnosticSegments)
            {
                await DisplayAlert("GeoJSON roads", lines.Count == 0
                    ? "No GeoJSON lines are within 40 m of this location."
                    : $"Found {lines.Count} lines; the display limit was exceeded. No partial overlay was shown.", "OK");
                return;
            }
            var frame = ARCameraPoseBridge.CurrentFrame;
            if (!CanInspectRoads(session) || !frame.IsFresh || !frame.IsTracking ||
                !frame.Pose.IsTracking || !frame.Anchor.IsAvailable ||
                heading.Value.SessionGeneration != frame.Generation.SessionGeneration ||
                frame.Anchor.ReferenceGeneration <= 0)
            {
                await DisplayAlert("GeoJSON roads", "Wait for camera tracking and a ground reference.", "OK");
                return;
            }
            roadDiagnosticAccuracyMeters = reading.AccuracyMeters;
            RescuAR.Diagnostics.AndroidLog.Info("RescuAR-Routing",
                $"ROAD DIAGNOSTICS PLACEMENT: session={frame.Generation.SessionGeneration}, " +
                $"gpsAccuracy={reading.AccuracyMeters:F1}m, " +
                $"fixAge={Math.Max(0, (DateTimeOffset.UtcNow - reading.Timestamp).TotalSeconds):F1}s, " +
                $"headingRepeatability={heading.Value.MaxSampleDeviationDegrees:F2}deg, " +
                $"segments={lines.Count}, radius={RoadLineDiagnosticRadiusMeters:0}m, basis=EUS.");
            ARRouteRenderer.ShowRoadDiagnostics(lines, frame.Pose.PositionX, frame.Pose.PositionZ,
                frame.Anchor.ReferenceGeneration, frame.Generation.SessionGeneration);
            developerRoadLinesButton.Text = "DEV: Hide GeoJSON roads";
            turnGuidancePanel.IsVisible = false;
            routeLocatorPanel.IsVisible = false;
            navigationAwarenessSheet.IsVisible = false;
            RefreshArTrackingStatusBanner();
            await DisplayAlert("GeoJSON roads",
                $"Showing all {lines.Count} raw line segments within 40 m. " +
                $"GPS accuracy: ±{reading.AccuracyMeters:0} m. " +
                "Cyan lines are an estimated map overlay. A steady compass can still be wrong. " +
                "GPS and heading errors can shift the lines away from the real road. " +
                "Tap the same button to restore navigation.", "OK");
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ARRouteRenderer.HideRoadDiagnostics();
            developerRoadLinesButton.Text = "DEV: Show GeoJSON roads (not directions)";
            if (CanInspectRoads(session)) await DisplayAlert("GeoJSON roads", exception.Message, "OK");
        }
        finally
        {
            if (ReferenceEquals(roadDiagnosticCancellation, work)) roadDiagnosticCancellation = null;
            developerRoadLinesButton.IsEnabled = true;
        }
    }

    private bool CanInspectRoads(long session) =>
        session > 0 && pageIsVisible && !_arCoreService.IsSessionPaused &&
        currentCameraModuleView == CameraModuleViewMode.ArCamera &&
        ARCameraPoseBridge.CurrentFrame.Generation.SessionGeneration == session;

    private static Button CreateDiagnosticButton(
        string text,
        string background = "#F3F4F6",
        string border = "#D1D5DB",
        string foreground = "#171717") =>
        new()
        {
            Text = text,
            HeightRequest = 38,
            Padding = new Thickness(10, 4),
            BackgroundColor = Color.FromArgb(background),
            BorderColor = Color.FromArgb(border),
            BorderWidth = 1,
            TextColor = Color.FromArgb(foreground),
            FontSize = 10,
            CornerRadius = 6
        };



    private async void RequestDiagnosticLocationConsent()
    {
        if (diagnosticConsentPromptShown)
        {
            return;
        }

        diagnosticConsentPromptShown =
            true;

        bool consent =
            await DisplayAlert(
                "Diagnostic location logging",
                "This diagnostic build can include approximate location " +
                "cells in exported field logs. Coordinates are rounded to " +
                "about 11 meters. Export only with tester approval and " +
                "delete the bundle after evidence review and no later than " +
                $"{DiagnosticPrivacyPolicy.MaximumRetentionDays} days.",
                "Allow approximate logging",
                "Keep locations redacted");

        DiagnosticPrivacyPolicy.SetLocationLoggingConsent(
            consent);

        AndroidLog.Info(
            "RescuAR-Privacy",
            "DIAGNOSTIC_LOCATION_CONSENT " +
            $"granted={DiagnosticPrivacyPolicy.HasLocationLoggingConsent}; " +
            "coordinatePolicy=quantized-F4; " +
            $"retention='{DiagnosticPrivacyPolicy.LocationRetentionPolicy}'");
    }
}
