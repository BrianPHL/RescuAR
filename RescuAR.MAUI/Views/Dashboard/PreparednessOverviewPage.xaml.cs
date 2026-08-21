using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class PreparednessOverviewPage : ContentView
{
    public PreparednessOverviewPage()
    {
        InitializeComponent();
        BindingContext = new PreparednessOverviewViewModel();
    }
}
