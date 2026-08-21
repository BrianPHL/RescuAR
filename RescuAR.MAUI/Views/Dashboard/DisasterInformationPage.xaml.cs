using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class DisasterInformationPage : ContentView
{
    public DisasterInformationPage()
    {
        InitializeComponent();
        BindingContext = new DisasterInformationViewModel();
    }
}
