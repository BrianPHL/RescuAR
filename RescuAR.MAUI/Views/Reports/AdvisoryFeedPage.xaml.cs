using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Reports;

namespace RescuAR.App.Views.Reports;

public partial class AdvisoryFeedPage : ContentPage
{
    public AdvisoryFeedViewModel ViewModel { get; }

    public AdvisoryFeedPage()
    {
        ViewModel = new AdvisoryFeedViewModel();
        InitializeComponent();
        BindingContext = ViewModel;
    }
}
