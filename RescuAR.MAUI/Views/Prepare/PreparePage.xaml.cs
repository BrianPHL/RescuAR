using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class PreparePage : ContentPage
{
    public PreparePage()
    {
        InitializeComponent();
        BindingContext = new PrepareViewModel();
    }

    public PreparePage(PrepareViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PrepareViewModel vm)
        {
            await vm.LoadDataAsync();
        }
    }
}
