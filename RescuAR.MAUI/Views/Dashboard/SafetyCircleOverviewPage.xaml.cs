using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class SafetyCircleOverviewPage : ContentView
{
    public SafetyCircleOverviewPage()
    {
        InitializeComponent();
        BindingContext = new SafetyCircleOverviewViewModel();
    }
}
