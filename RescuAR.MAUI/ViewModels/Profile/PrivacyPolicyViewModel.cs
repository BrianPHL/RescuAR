using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Models;
using RescuAR.App.Services.Profile;

namespace RescuAR.App.ViewModels.Profile;

public partial class PrivacyPolicyViewModel : ObservableObject
{
    public IReadOnlyList<InformationSection> PolicySections => AppInformationContent.Privacy;
    [RelayCommand] private async Task Back() { if (Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
