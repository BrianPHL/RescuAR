using RescuAR.App.ViewModels.Profile;

namespace RescuAR.App.Views.Profile;

public partial class PersonalInformationPage : ContentPage
{
    public PersonalInformationPage()
    {
        InitializeComponent();
        BindingContext = new PersonalInformationViewModel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PersonalInformationViewModel model) await model.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        if (BindingContext is PersonalInformationViewModel model) model.StopLoading();
        base.OnDisappearing();
    }
}
