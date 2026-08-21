using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare
{
    public partial class PASSPage : ContentPage
    {
        public PASSPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (BindingContext is PASSViewModel vm)
            {
                vm.RefreshScore();
            }
        }
    }
}
