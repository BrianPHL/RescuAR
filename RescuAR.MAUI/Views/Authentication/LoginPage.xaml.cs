using RescuAR.App.ViewModels.Authentication;

namespace RescuAR.App.Views.Authentication;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        if (BindingContext is LoginViewModel model) model.CancelPasswordReset();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (BindingContext is LoginViewModel { IsResetPasswordModalVisible: true } model)
        {
            model.CancelPasswordReset();
            return true;
        }
        return base.OnBackButtonPressed();
    }
}
