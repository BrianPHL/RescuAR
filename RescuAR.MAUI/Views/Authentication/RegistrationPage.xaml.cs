using RescuAR.App.ViewModels.Authentication;

namespace RescuAR.App.Views.Authentication;

public partial class RegistrationPage : ContentPage
{
    public RegistrationPage(RegistrationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
