using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class WeatherInformationPage : ContentView
{
    public WeatherInformationPage()
    {
        InitializeComponent();
        BindingContext = new WeatherInformationViewModel();
    }
}
