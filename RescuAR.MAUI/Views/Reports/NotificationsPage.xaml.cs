using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Reports;

namespace RescuAR.App.Views.Reports
{
    public partial class NotificationsPage : ContentPage
    {
        public NotificationsPage(ReportsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
