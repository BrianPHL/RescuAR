using RescuAR.App.ViewModels.Prepare;
using Microsoft.Maui.Controls;

namespace RescuAR.App.Views.Prepare;

public partial class DocumentaryVideosPage : ContentPage
{
    public DocumentaryVideosPage(DocumentaryVideosViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
