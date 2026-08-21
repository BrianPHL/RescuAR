using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class EvacuationOverviewPage : ContentView
{
    public EvacuationOverviewPage()
    {
        InitializeComponent();
        BindingContext = new EvacuationOverviewViewModel();
    }
}
