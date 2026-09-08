using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Dashboard;

namespace RescuAR.App.Views.Dashboard
{
    public partial class AdvisoryFeedPage : ContentPage
    {
        public AdvisoryFeedPage(AdvisoryFeedViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        public AdvisoryFeedPage() : this(new AdvisoryFeedViewModel())
        {
        }
    }
}
