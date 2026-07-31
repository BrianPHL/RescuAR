using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary;

public partial class NearbySafeLocationsPage : ContentPage
{
    public NearbySafeLocationsPage(NearbySafeLocationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
