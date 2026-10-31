using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Prepare;

public partial class FloodTimelineViewModel : ObservableObject
{
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
