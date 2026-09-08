using RescuAR.App.ViewModels.Prepare;
using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Prepare;

public partial class HistoricalPhotosPage : ContentPage
{
    public HistoricalPhotosPage(HistoricalPhotosViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
