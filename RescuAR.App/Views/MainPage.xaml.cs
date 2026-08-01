using RescuAR.App.Models;
using RescuAR.App.Models;
using RescuAR.App.Services.Unity;


namespace RescuAR.App.Views;

public partial class MainPage : ContentPage
{
    private readonly List<EvacuationCenter>
        _centers = new();

    private readonly List<EvacuationCenter>
        _centers = new();

    public MainPage()
    {
        InitializeComponent();

        _centers.Add(
            new EvacuationCenter
            {
                Id = 1,
                Name = "Marikina Sports Center",
                Latitude = 14.6358,
                Longitude = 121.0965,
                Capacity = 500,
                Occupancy = 350,
                Status = "OPEN"
            });

        _centers.Add(
            new EvacuationCenter
            {
                Id = 2,
                Name = "Sto. Niño Covered Court",
                Latitude = 14.6412,
                Longitude = 121.1048,
                Capacity = 250,
                Occupancy = 125,
                Status = "OPEN"
            });

        _centers.Add(
            new EvacuationCenter
            {
                Id = 3,
                Name = "Concepcion Gym",
                Latitude = 14.6490,
                Longitude = 121.1090,
                Capacity = 400,
                Occupancy = 400,
                Status = "FULL"
            });

        CenterPicker.ItemsSource =
            _centers.Select(x => x.Name)
                .ToList();

        _centers.Add(
            new EvacuationCenter
            {
                Id = 1,
                Name = "Marikina Sports Center",
                Latitude = 14.6358,
                Longitude = 121.0965,
                Capacity = 500,
                Occupancy = 350,
                Status = "OPEN"
            });

        _centers.Add(
            new EvacuationCenter
            {
                Id = 2,
                Name = "Sto. Niño Covered Court",
                Latitude = 14.6412,
                Longitude = 121.1048,
                Capacity = 250,
                Occupancy = 125,
                Status = "OPEN"
            });

        _centers.Add(
            new EvacuationCenter
            {
                Id = 3,
                Name = "Concepcion Gym",
                Latitude = 14.6490,
                Longitude = 121.1090,
                Capacity = 400,
                Occupancy = 400,
                Status = "FULL"
            });

        CenterPicker.ItemsSource =
            _centers.Select(x => x.Name)
                .ToList();
    }

    private void LaunchUnityClicked(
        object? sender,
        EventArgs e)
    {
#if ANDROID

        if (CenterPicker.SelectedIndex < 0)
            return;

        var selectedCenter =
            _centers[
                CenterPicker.SelectedIndex];


        if (CenterPicker.SelectedIndex < 0)
            return;

        var selectedCenter =
            _centers[
                CenterPicker.SelectedIndex];

        var unityService =
            Handler?
                .MauiContext?
                .Services
                .GetService<IUnityService>();
            Handler?
                .MauiContext?
                .Services
                .GetService<IUnityService>();

        unityService?.LaunchUnity(
            selectedCenter);

        unityService?.LaunchUnity(
            selectedCenter);

#endif
    }

    private async void TestRoutingClicked(object? sender, EventArgs e)
    {
        var routingService = Handler?.MauiContext?.Services.GetService<RescuAR.App.Services.Navigation.RoutingService>();
        var offlineService = Handler?.MauiContext?.Services.GetService<RescuAR.App.Services.Navigation.OfflineRoutingService>();

        if (routingService != null && offlineService != null)
        {
            try
            {
                RoutingResultLabel.Text = "Initializing Map Data...";
                await offlineService.InitializeMapDataAsync();
                
                RoutingResultLabel.Text = "Calculating Route...";
                var result = await routingService.CalculateRouteAsync("start", "end");
                
                RoutingResultLabel.Text = $"Route Calculated! Result: {result.GetType().Name}";
            }
            catch (Exception ex)
            {
                RoutingResultLabel.Text = $"Error: {ex.Message}";
            }
        }
        else
        {
            RoutingResultLabel.Text = "Routing services not found in DI container.";
        }
    }
}
}
