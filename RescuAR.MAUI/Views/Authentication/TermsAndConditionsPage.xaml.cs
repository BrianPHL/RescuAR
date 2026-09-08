using Microsoft.Maui.Controls;
using System;

using RescuAR.App.Services.Authentication;

namespace RescuAR.App.Views.Authentication
{
    public partial class TermsAndConditionsPage : ContentPage
    {
        public TermsAndConditionsPage()
        {
            InitializeComponent();
        }

        private async void OnBackTapped(object sender, EventArgs e)
        {
            if (AuthenticationNavigation.RootPage is NavigationPage navPage)
            {
                await navPage.PopAsync();
            }
        }
    }
}
