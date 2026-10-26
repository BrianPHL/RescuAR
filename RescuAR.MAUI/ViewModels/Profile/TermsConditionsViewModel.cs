using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Models;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class TermsConditionsViewModel : ObservableObject
{
    public IReadOnlyList<InformationSection> TermsSections => AppInformationContent.Terms;
    [RelayCommand] private async Task Back() { if (Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
