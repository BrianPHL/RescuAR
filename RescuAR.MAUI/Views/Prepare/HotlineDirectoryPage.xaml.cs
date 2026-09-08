using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class HotlineDirectoryPage : ContentPage
{
    public HotlineDirectoryViewModel ViewModel { get; }

        public HotlineDirectoryPage()
    {
        ViewModel = new HotlineDirectoryViewModel();
        InitializeComponent();
        BindingContext = ViewModel;
    }
}
