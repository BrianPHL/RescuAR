using RescuAR.App.ViewModels.Prepare;
using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Prepare;

public partial class FloodTimelinePage : ContentPage
{
    public FloodTimelinePage(FloodTimelineViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
