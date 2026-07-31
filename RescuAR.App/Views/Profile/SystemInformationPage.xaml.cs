using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

/// <summary>
/// Code-behind for the System Information page.
/// </summary>
public partial class SystemInformationPage : ContentPage
{
    public SystemInformationPage(SystemInformationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
