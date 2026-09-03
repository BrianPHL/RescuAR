using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare
{
    public partial class EvacuationCenterInfoPage : ContentPage
    {
        public EvacuationCenterInfoPage()
        {
            InitializeComponent();

            BindingContext =
                new EvacuationCenterInfoViewModel();
        }
    }
}
