using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class PreparednessGuidePage : ContentPage
{
    public PreparednessGuideViewModel ViewModel { get; }

    public PreparednessGuidePage()
        : this(new PreparednessGuideViewModel())
    {
    }

    public PreparednessGuidePage(PreparednessGuideViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        BindingContext = ViewModel;
    }
}
