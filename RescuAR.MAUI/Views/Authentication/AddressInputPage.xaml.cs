using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Authentication;

namespace RescuAR.App.Views.Authentication
{
    public partial class AddressInputPage : ContentPage
    {
        public AddressInputPage(AddressInputViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
