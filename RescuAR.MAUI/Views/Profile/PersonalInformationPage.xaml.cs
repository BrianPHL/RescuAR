using RescuAR.App.ViewModels.Profile;
using Microsoft.Extensions.DependencyInjection;

namespace RescuAR.App.Views.Profile;

public partial class PersonalInformationPage : ContentPage
{
    public PersonalInformationPage() : this(RescuAR.MAUI.MauiProgram.Services.GetRequiredService<PersonalInfoViewModel>()) { }
    public PersonalInformationPage(PersonalInfoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ((PersonalInfoViewModel)BindingContext).LoadAsync();
    }
}
