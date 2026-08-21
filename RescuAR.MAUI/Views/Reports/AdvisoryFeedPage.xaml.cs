using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Reports;

namespace RescuAR.App.Views.Reports;

public partial class AdvisoryFeedPage : ContentPage
{
    public AdvisoryFeedPage()
    {
        InitializeComponent();
        BindingContext = new AdvisoryFeedViewModel();
    }
}
