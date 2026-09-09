using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Authentication;

namespace RescuAR.App.Views.Authentication
{
    public partial class PermissionsPage : ContentPage
    {
        public PermissionsPage(PermissionsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
