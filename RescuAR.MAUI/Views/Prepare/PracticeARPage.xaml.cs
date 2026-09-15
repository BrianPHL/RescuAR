using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class PracticeARPage : ContentPage
{
    public PracticeARPage()
    {
        InitializeComponent();
        BindingContext = new PracticeARViewModel();
    }

    public PracticeARPage(PracticeARViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
