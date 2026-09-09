using RescuAR.App.ViewModels.Authentication;

namespace RescuAR.App.Views.Authentication;

public partial class GoogleAuthPage : ContentPage
{
    public GoogleAuthPage(GoogleAuthViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
