using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class PreparednessGuidePage : ContentPage
{
    public PreparednessGuidePage()
    {
        InitializeComponent();
        BindingContext = new PreparednessGuideViewModel();
    }

    public PreparednessGuidePage(PreparednessGuideViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
