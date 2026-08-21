using Microsoft.Maui.Controls;
using RescuAR.App.Services.Reports;
using RescuAR.App.ViewModels.Reports;

namespace RescuAR.App.Views.Reports
{
    public partial class ReportsPage : ContentPage
    {
        public ReportsPage(ReportsViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }

        public ReportsPage() : this(new ReportsViewModel(new CommunityReportService(), new OsmGeocodingService()))
        {
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (BindingContext is ReportsViewModel vm)
            {
                _ = vm.LoadReportsAsync();
            }
        }
    }
}
