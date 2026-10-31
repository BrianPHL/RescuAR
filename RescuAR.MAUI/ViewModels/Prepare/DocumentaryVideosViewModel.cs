using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Prepare;

public partial class DocumentaryVideosViewModel : ObservableObject
{
    [ObservableProperty]
    private string _ondoyVideoUrl = "https://www.youtube.com/embed/l_Bf5alw3Ps";

    [ObservableProperty]
    private string _ulyssesVideoUrl = "https://www.youtube.com/embed/Qq_nTQXzpmo";

    [RelayCommand]
    private async Task BackAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                await Shell.Current.GoToAsync("//Home");
            }
        }
    }
}
