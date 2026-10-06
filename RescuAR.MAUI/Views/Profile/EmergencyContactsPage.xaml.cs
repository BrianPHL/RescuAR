using RescuAR.App.ViewModels.Profile;
using Microsoft.Extensions.DependencyInjection;

namespace RescuAR.App.Views.Profile;

public partial class EmergencyContactsPage : ContentPage
{
    public EmergencyContactsPage() : this(RescuAR.MAUI.MauiProgram.Services.GetRequiredService<EmergencyContactsViewModel>()) { }
    public EmergencyContactsPage(EmergencyContactsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((EmergencyContactsViewModel)BindingContext).LoadAsync();
    }
}
