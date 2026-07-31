using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary;

public partial class RecommendedActionPage : ContentPage
{
    public RecommendedActionPage(RecommendedActionViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
