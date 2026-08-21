using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Reports;

namespace RescuAR.App.Views.Reports
{
    public partial class ReportDetailsPage : ContentPage
    {
        public ReportDetailsPage(ReportDetailsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}
