using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

/// <summary>
/// Code-behind for the Terms &amp; Conditions page.
/// </summary>
public partial class TermsConditionsPage : ContentPage
{
    public TermsConditionsPage(TermsConditionsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
