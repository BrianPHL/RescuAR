using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class FloodHistoryPage : ContentPage
{
    public FloodHistoryPage()
    {
        InitializeComponent();
        BindingContext = new FloodHistoryViewModel();
    }
}
