using RescuAR.App.ViewModels.Authentication;

namespace RescuAR.App.Views.Authentication;

public partial class RegistrationSuccessPage : ContentPage
{
    public RegistrationSuccessPage(RegistrationSuccessViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
