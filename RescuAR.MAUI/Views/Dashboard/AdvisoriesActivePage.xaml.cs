using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class AdvisoriesActivePage : ContentView
{
    public AdvisoriesActivePage()
    {
        InitializeComponent();
        BindingContext = new AreaStatusOverviewViewModel();
    }
}
