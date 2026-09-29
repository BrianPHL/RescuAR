#if ANDROID
using Android.Util;
#endif
using RescuAR.App.Services.Flood;

namespace RescuAR.App.Views.Camera;

// User-selected flood previews are available in normal and diagnostic builds.
public partial class CameraPage
{
    private Grid floodSimulationConfigurationSheet = null!;
    private Slider floodSimulationDepthSlider = null!;
    private Label floodSimulationDepthValueLabel = null!;

    private void CloseFloodSimulationSheet()
    {
        floodSimulationConfigurationSheet.IsVisible = false;
        floodSimulationConfigurationHost.IsVisible = false;
    }

    private void OnFloodSimulationConfigureClicked(
        object? sender,
        EventArgs e)
    {
        navigationAwarenessSheet.IsVisible =
            false;

        if (currentFloodVisualization.LocalDepthMeters.HasValue)
        {
            floodSimulationDepthSlider.Value =
                Math.Clamp(
                    currentFloodVisualization.LocalDepthMeters.Value,
                    floodSimulationDepthSlider.Minimum,
                    floodSimulationDepthSlider.Maximum);
        }

        floodSimulationDepthValueLabel.Text =
            floodSimulationDepthSlider.Value.ToString("0.0");

        floodSimulationConfigurationSheet.IsVisible = true;
        floodSimulationConfigurationHost.IsVisible = true;
    }

    private void OnFloodSimulationSliderChanged(
        object? sender,
        ValueChangedEventArgs e)
    {
        if (floodSimulationDepthValueLabel is null)
        {
            return;
        }

        floodSimulationDepthValueLabel.Text =
            e.NewValue.ToString("0.0");
    }

    private void OnSaveFloodSimulationClicked(
        object? sender,
        EventArgs e)
    {
        double depth =
            Math.Clamp(
                floodSimulationDepthSlider.Value,
                0.1,
                3.0);

        FloodDepthVisualizationService.FloodVisualizationSnapshot snapshot =
            _floodDepthVisualizationService.FromSimulation(depth);

        ApplyFloodVisualization(
            snapshot,
            "user-selected flood simulation");

        CloseFloodSimulationSheet();

#if ANDROID
        Log.Info(
            FloodDepthLogTag,
            "FLOOD SIMULATION APPLIED: " +
            $"depth={depth:F2}m, groundRelative=True.");
#endif
    }

    private void ConfigureFloodSimulationSheet()
    {
        floodSimulationConfigurationHost.IsVisible = false;

        floodSimulationConfigurationHost.InputTransparent = false;

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
                Text = "Flood simulation",
                TextColor = Color.FromArgb("#858585"),
                FontSize = 12,
                FontAttributes = FontAttributes.Bold
            });

        content.Children.Add(
            new Label
            {
                Text = "Water height above the floor (m)",
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
            CreateFloodSimulationButton(
                "Apply water height",
                "#0A929C",
                "#08757D");

        apply.Clicked +=
            OnSaveFloodSimulationClicked;

        content.Children.Add(
            apply);

        Button close =
            CreateFloodSimulationButton(
                "Close");

        close.Clicked +=
            OnFloodSimulationSheetCloseButtonClicked;

        content.Children.Add(close);
        Button clear = CreateFloodSimulationButton("Clear simulation");
        clear.Clicked += (_, _) =>
        {
            ClearFloodVisualization("user cleared the simulation");
            CloseFloodSimulationSheet();
        };
        content.Children.Add(clear);

        panel.Content =
            content;

        floodSimulationConfigurationSheet.Children.Add(
            panel);

        floodSimulationConfigurationHost.Children.Add(
            floodSimulationConfigurationSheet);
    }

    private void OnFloodSimulationSheetCloseButtonClicked(
        object? sender,
        EventArgs e)
    {
        CloseFloodSimulationSheet();
    }

    private static Button CreateFloodSimulationButton(
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
}
