using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class QuickActionsPage : ContentView
{
    public QuickActionsPage()
    {
        InitializeComponent();
        BindingContext = new QuickActionsViewModel();
    }
}
