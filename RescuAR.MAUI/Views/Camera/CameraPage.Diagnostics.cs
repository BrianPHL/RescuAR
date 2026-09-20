using RescuAR.Diagnostics;

namespace RescuAR.App.Views.Camera;

/// <summary>
/// Diagnostic-build-only camera controls. This file is removed from normal
/// compilation by RescuAR.MAUI.csproj and is included only when
/// RescuArDiagnosticBuild=true defines RESCUAR_DIAGNOSTICS.
/// </summary>
public partial class CameraPage
{
    private Button developerFloodDepthTestButton =
        null!;

    private Button developerSafeZoneTestButton =
        null!;

    private Button developerTurnTestButton =
        null!;

    private Button developerRerouteTestButton =
        null!;

    private Button developerHazardRerouteTestButton =
        null!;

    private Grid floodSimulationConfigurationSheet =
        null!;

    private Slider floodSimulationDepthSlider =
        null!;

    private Label floodSimulationDepthValueLabel =
        null!;

    private bool diagnosticConsentPromptShown;

    private static readonly bool EnableDeveloperOffRouteSimulation =
        true;

    private static readonly bool EnableDeveloperTurnSimulation =
        true;

    private static readonly bool EnableDeveloperSafeZoneValidation =
        true;

    private static readonly bool EnableDeveloperFloodDepthValidation =
        true;

    private static readonly bool EnableDeveloperDynamicHazardValidation =
        true;

    private const double DeveloperSafeZoneTargetAheadMeters =
        20.0;

    private static readonly double?[] DeveloperFloodDepthSequenceMeters =
    {
        0.30,
        0.60,
        1.00,
        1.50,
        null
    };

    private const double DeveloperHazardAheadMeters =
        45.0;

    private const double DeveloperHazardRadiusMeters =
        12.0;

    private const double DeveloperSimulatedCrossTrackMeters =
        50.0;

    private const double DeveloperSimulatedGpsAccuracyMeters =
        5.0;

    private int developerFloodDepthSequenceIndex;
    private bool developerHazardValidationArmed;
    private bool developerSafeZoneValidationArmed;

    private RescuAR.Navigation.Models.GeoCoordinate?
        developerSafeZoneTargetCoordinate;

    private double developerSafeZoneTargetProgressMeters =
        double.NaN;

    private void ConfigureDiagnosticControls()
    {
        diagnosticCameraActionHost.IsVisible =
            true;

        diagnosticCameraActionHost.InputTransparent =
            false;

        developerFloodDepthTestButton =
            CreateDiagnosticButton(
                "Simulate Flood Depth",
                "#0A929C",
                "#08757D");

        developerFloodDepthTestButton.WidthRequest =
            204;

        developerFloodDepthTestButton.Margin =
            new Thickness(
                0,
                0,
                0,
                12);

        developerFloodDepthTestButton.HorizontalOptions =
            LayoutOptions.Center;

        developerFloodDepthTestButton.VerticalOptions =
            LayoutOptions.End;

        developerFloodDepthTestButton.Clicked +=
            OnFloodSimulationConfigureClicked;

        diagnosticCameraActionHost.Children.Add(
            developerFloodDepthTestButton);

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

        diagnosticNavigationControlsHost.Children.Add(
            developerSafeZoneTestButton);

        diagnosticNavigationControlsHost.Children.Add(
            developerTurnTestButton);

        diagnosticNavigationControlsHost.Children.Add(
            developerRerouteTestButton);

        diagnosticNavigationControlsHost.Children.Add(
            developerHazardRerouteTestButton);

        ConfigureFloodSimulationSheet();
    }

    private void ConfigureFloodSimulationSheet()
    {
        diagnosticConfigurationHost.IsVisible =
            true;

        diagnosticConfigurationHost.InputTransparent =
            false;

        floodSimulationConfigurationSheet =
            new Grid
            {
                IsVisible = false,
                VerticalOptions = LayoutOptions.End
            };

        Border panel =
            new()
            {
                Padding =
                    new Thickness(
                        14,
                        12,
                        14,
                        14),
                BackgroundColor =
                    Colors.White,
                Stroke =
                    new SolidColorBrush(
                        Color.FromArgb("#E5E7E9")),
                StrokeThickness =
                    1,
                StrokeShape =
                    new Microsoft.Maui.Controls.Shapes.RoundRectangle
                    {
                        CornerRadius =
                            new CornerRadius(
                                10)
                    }
            };

        VerticalStackLayout content =
            new()
            {
                Spacing = 13
            };

        content.Children.Add(
            new Label
            {
                Text = "Diagnostic Flood Depth Simulation",
                TextColor = Color.FromArgb("#858585"),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold
            });

        content.Children.Add(
            new Label
            {
                Text = "Synthetic local depth in meters",
                TextColor = Color.FromArgb("#171717"),
                FontSize = 12
            });

        Grid sliderRow =
            new()
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition
                    {
                        Width = GridLength.Star
                    },
                    new ColumnDefinition
                    {
                        Width = new GridLength(
                            52)
                    }
                },
                ColumnSpacing = 10
            };

        floodSimulationDepthSlider =
            new Slider
            {
                Minimum = 0.1,
                Maximum = 3.0,
                Value = 0.6,
                MinimumTrackColor = Color.FromArgb("#0A929C"),
                MaximumTrackColor = Color.FromArgb("#D9D9D9"),
                ThumbColor = Color.FromArgb("#0A929C")
            };

        floodSimulationDepthSlider.ValueChanged +=
            OnFloodSimulationSliderChanged;

        floodSimulationDepthValueLabel =
            new Label
            {
                Text = "0.6",
                TextColor = Color.FromArgb("#111111"),
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };

        Grid.SetColumn(
            floodSimulationDepthValueLabel,
            1);

        sliderRow.Children.Add(
            floodSimulationDepthSlider);

        sliderRow.Children.Add(
            floodSimulationDepthValueLabel);

        content.Children.Add(
            sliderRow);

        Button apply =
            CreateDiagnosticButton(
                "Apply Synthetic Depth",
                "#0A929C",
                "#08757D");

        apply.Clicked +=
            OnSaveFloodSimulationClicked;

        content.Children.Add(
            apply);

        Button close =
            CreateDiagnosticButton(
                "Close");

        close.Clicked +=
            OnFloodSimulationSheetCloseButtonClicked;

        content.Children.Add(
            close);

        panel.Content =
            content;

        floodSimulationConfigurationSheet.Children.Add(
            panel);

        diagnosticConfigurationHost.Children.Add(
            floodSimulationConfigurationSheet);
    }

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

    private void OnFloodSimulationSheetCloseButtonClicked(
        object? sender,
        EventArgs e)
    {
        floodSimulationConfigurationSheet.IsVisible =
            false;
    }

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
