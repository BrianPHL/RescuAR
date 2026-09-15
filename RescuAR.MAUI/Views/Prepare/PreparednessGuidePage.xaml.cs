using System;
using System.ComponentModel;
using Microsoft.Maui.Controls;
using RescuAR.App.ViewModels.Prepare;

namespace RescuAR.App.Views.Prepare;

public partial class PreparednessGuidePage : ContentPage
{
    private View? _zoomedView;

    private double _startTranslationX;

    private double _startTranslationY;

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
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnDisappearing()
    {
        ResetPdfZoomState();
        ViewModel.IsPdfModalVisible = false;
        base.OnDisappearing();
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName ==
                nameof(PreparednessGuideViewModel.IsPdfModalVisible) &&
            !ViewModel.IsPdfModalVisible)
        {
            Dispatcher.Dispatch(
                ResetPdfZoomState);
        }
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
                    ResetViewTransform(
                        _zoomedView);
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
                ResetPdfZoomState();
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

    private void ResetPdfZoomState()
    {
        if (_zoomedView is not null)
        {
            ResetViewTransform(
                _zoomedView);
        }

        _zoomedView =
            null;

        _startTranslationX =
            0.0;

        _startTranslationY =
            0.0;

        PdfScrollView.Orientation =
            ScrollOrientation.Vertical;
    }

    private static void ResetViewTransform(
        View view)
    {
        view.Scale =
            1.0;

        view.TranslationX =
            0.0;

        view.TranslationY =
            0.0;
    }
}
