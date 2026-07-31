using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary;

public partial class SituationAsessmentPage : ContentPage
{
    public SituationAsessmentPage(SituationAssessmentViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
