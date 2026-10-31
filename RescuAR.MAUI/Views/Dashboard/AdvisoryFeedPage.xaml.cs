using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard
{
    public partial class AdvisoryFeedPage : ContentPage
    {
        public AdvisoryFeedViewModel ViewModel { get; }

        public AdvisoryFeedPage(AdvisoryFeedViewModel viewModel)
        {
            ViewModel = viewModel ?? new AdvisoryFeedViewModel();
            InitializeComponent();
            BindingContext = ViewModel;
        }

        public AdvisoryFeedPage() : this(new AdvisoryFeedViewModel())
        {
        }
    }
}
