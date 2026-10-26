using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

public partial class EmergencyContactsPage : ContentPage
{
    public EmergencyContactsPage()
    {
        InitializeComponent();
        BindingContext = new EmergencyContactsViewModel();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is EmergencyContactsViewModel { IsPicking: false } model) await model.LoadAsync();
    }
    protected override void OnDisappearing()
    {
        if (BindingContext is EmergencyContactsViewModel { IsPicking: false } model) model.StopLoading();
        base.OnDisappearing();
    }
}
