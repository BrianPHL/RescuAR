using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary;

public partial class ActivityHistoryPage : ContentPage
{
    public ActivityHistoryPage(ActivityHistoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
