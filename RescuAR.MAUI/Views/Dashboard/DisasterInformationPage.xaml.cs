using System;
using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Dashboard;

public partial class DisasterInformationPage : ContentView
{
    public DisasterInformationPage()
    {
        InitializeComponent();
        BindingContext = new ViewModels.Dashboard.DisasterInformationViewModel();
    }

    private async void OnDisasterUpdatesTapped(object? sender, EventArgs e)
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.Navigation.PushAsync(new Views.Reports.AdvisoryFeedPage());
            }
            catch
            {
                await Shell.Current.GoToAsync("AdvisoryFeedPage");
            }
        }
    }
}
