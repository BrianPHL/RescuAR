using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary;

public partial class SummaryPage : ContentPage
{
    public SummaryPage(SummaryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
