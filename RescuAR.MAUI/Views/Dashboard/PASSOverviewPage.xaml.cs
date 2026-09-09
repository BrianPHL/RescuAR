using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class PASSOverviewPage : ContentView
{
    public PASSOverviewPage()
    {
        InitializeComponent();
        BindingContext = new PASSOverviewViewModel();
    }
}
