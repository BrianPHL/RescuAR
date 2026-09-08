using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary
{
    public partial class SummaryPage : ContentPage
    {
        public SummaryViewModel ViewModel { get; }

        public SummaryPage()
        {
            ViewModel = new SummaryViewModel();
            InitializeComponent();
            BindingContext = ViewModel;
        }
    }
}
