using System;
using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Dashboard;

public partial class AdvisoriesActivePage : ContentView
{
    public AdvisoriesActivePage()
    {
        InitializeComponent();
        BindingContext = new ViewModels.Dashboard.AreaStatusOverviewViewModel();
    }

    private async void OnSummaryTapped(object? sender, EventArgs e)
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.Navigation.PushAsync(new Views.Summary.SummaryPage());
            }
            catch
            {
                await Shell.Current.GoToAsync("SummaryPage");
            }
        }
    }
}
