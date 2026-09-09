using Microsoft.Maui.Controls;
using RescuAR.App.Services.Reports;
using RescuAR.App.ViewModels.Reports;

namespace RescuAR.App.Views.Reports
{
    public partial class ReportsPage : ContentPage
    {
        public ReportsViewModel ViewModel { get; }

        public ReportsPage(ReportsViewModel viewModel)
        {
            ViewModel = viewModel;
            InitializeComponent();
            BindingContext = ViewModel;
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
