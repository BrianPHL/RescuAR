using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Summary;

namespace RescuAR.App.Views.Summary
{
    public partial class SummaryPage : ContentPage
    {
        public SummaryPage()
        {
            InitializeComponent();
            BindingContext = new SummaryViewModel();
        }
    }
}
