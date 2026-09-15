using System;
using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class PreparednessGuidePage : ContentPage
{
    public PreparednessGuideViewModel ViewModel { get; }
    public PreparednessGuidePage()
        : this(new PreparednessGuideViewModel())
    {
    }

    public PreparednessGuidePage(PreparednessGuideViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        BindingContext = ViewModel;
    }

    private void OnDoubleTapToggleZoom(object sender, TappedEventArgs e)
    {
        if (sender is View view)
        {
            if (view.Scale < 1.5)
            {
                // Reset any previously zoomed view
                if (_zoomedView != null && _zoomedView != view)
                {
                    _zoomedView.Scale = 1.0;
                    _zoomedView.TranslationX = 0;
                    _zoomedView.TranslationY = 0;
                }

                // Zoom IN to 2.5x
                view.Scale = 2.5;
                view.TranslationX = 0;
                view.TranslationY = 0;
                _zoomedView = view;

                // Disable ScrollView scrolling so PanGesture captures all 2D movement
                PdfScrollView.Orientation = ScrollOrientation.Neither;
            }
            else
            {
                // Zoom OUT & reset position
                view.Scale = 1.0;
                view.TranslationX = 0;
                view.TranslationY = 0;
                _zoomedView = null;

                // Re-enable ScrollView vertical scrolling
                PdfScrollView.Orientation = ScrollOrientation.Vertical;
            }
        }
    }

    private void OnPanUpdated(object sender, PanUpdatedEventArgs e)
    {
        if (sender is View view && view.Scale > 1.05)
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _startTranslationX = view.TranslationX;
                    _startTranslationY = view.TranslationY;
                    break;

                case GestureStatus.Running:
                    double newTx = _startTranslationX + e.TotalX;
                    double newTy = _startTranslationY + e.TotalY;

                    // Flexible 2D pan bounds based on visual scale and element dimensions
                    double width = view.Width > 0 ? view.Width : 350;
                    double height = view.Height > 0 ? view.Height : 500;

                    double maxTx = Math.Max(350, (width * (view.Scale - 1.0)) / 1.1);
                    double maxTy = Math.Max(600, (height * (view.Scale - 1.0)) / 1.1);

                    view.TranslationX = Math.Clamp(newTx, -maxTx, maxTx);
                    view.TranslationY = Math.Clamp(newTy, -maxTy, maxTy);
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    break;
            }
        }
    }
}
