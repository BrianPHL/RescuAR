using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare
{
    public partial class ChecklistPage : ContentPage
    {
        public ChecklistViewModel ViewModel { get; }

        public ChecklistPage()
        {
            ViewModel = new ChecklistViewModel();
            InitializeComponent();
            BindingContext = ViewModel;
        }
    }
}
