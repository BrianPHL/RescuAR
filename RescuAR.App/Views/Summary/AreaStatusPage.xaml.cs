using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary;

public partial class AreaStatusPage : ContentPage
{
    public AreaStatusPage(AreaStatusViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
