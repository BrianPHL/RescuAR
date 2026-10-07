using RescuAR.App.ViewModels.Authentication;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace RescuAR.App.Views.Authentication;

public partial class SplashPage : ContentPage
{
    private readonly IDispatcherTimer radarTimer;
    private float sweep;

    public SplashPage(SplashViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        radarTimer = Dispatcher.CreateTimer();
        radarTimer.Interval = TimeSpan.FromMilliseconds(50);
        radarTimer.Tick += (_, _) => { sweep = (sweep + 4) % 360; RadarCanvas.InvalidateSurface(); };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        radarTimer.Start();
        if (BindingContext is SplashViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    protected override void OnDisappearing()
    {
        radarTimer.Stop();
        base.OnDisappearing();
    }

    private void OnRadarCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        float size = Math.Min(e.Info.Width, e.Info.Height);
        canvas.Translate(e.Info.Width / 2f, e.Info.Height / 2f);
        canvas.Scale(size / 280f);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, Color = SKColor.Parse("#245165") };
        foreach (float radius in new[] { 42f, 80f, 120f }) canvas.DrawCircle(0, 0, radius, paint);
        canvas.DrawLine(-120, 0, 120, 0, paint);
        canvas.DrawLine(0, -120, 0, 120, paint);
        canvas.Save();
        canvas.RotateDegrees(sweep);
        using var sector = new SKPath();
        sector.MoveTo(0, 0);
        sector.ArcTo(new SKRect(-120, -120, 120, 120), -40, 40, false);
        sector.Close();
        paint.Style = SKPaintStyle.Fill;
        paint.Color = SKColor.Parse("#185B6570");
        canvas.DrawPath(sector, paint);
        paint.Color = SKColor.Parse("#18B6BC");
        paint.StrokeWidth = 2;
        canvas.DrawLine(0, 0, 120, 0, paint);
        canvas.Restore();
        paint.Color = SKColor.Parse("#5EEAD4");
        canvas.DrawCircle(0, 0, 7, paint);
        canvas.DrawCircle(-51, -32, 4, paint);
        canvas.DrawCircle(47, 55, 4, paint);
    }
}


