using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard;

public partial class QuickActionsPage : ContentView
{
    public QuickActionsViewModel ViewModel { get; }

    public QuickActionsPage()
    {
        ViewModel = new QuickActionsViewModel();
        InitializeComponent();
        BindingContext = ViewModel;
    }
}
