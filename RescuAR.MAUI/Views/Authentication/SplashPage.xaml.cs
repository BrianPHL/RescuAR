using System;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;
using RescuAR.App.ViewModels.Authentication;
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace RescuAR.App.Views.Authentication;

public partial class SplashPage : ContentPage
{
    private IDispatcherTimer? _animationTimer;
    private float _sweepAngle = 0f;
    private float _pulsePhase = 0f;

    // Simulated beacon blip targets on the map (relative offsets normalized to radius)
    private readonly (float relX, float relY, float angleDeg)[] _radarBlips = new[]
    {
        (0.38f, -0.32f, 320f),
        (-0.45f, -0.35f, 218f),
        (-0.25f, 0.52f, 116f),
        (0.55f, 0.28f, 27f),
        (0.12f, 0.62f, 79f)
    };

    public SplashPage(SplashViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 1. Start smooth 60 FPS radar sweep & ping animation
        _animationTimer = Dispatcher.CreateTimer();
        _animationTimer.Interval = TimeSpan.FromMilliseconds(16);
        _animationTimer.Tick += (s, e) =>
        {
            _sweepAngle = (_sweepAngle + 3.2f) % 360f;
            _pulsePhase = (_pulsePhase + 0.016f) % 1f;
            RadarCanvas?.InvalidateSurface();
        };
        _animationTimer.Start();

        // 2. Perform system startup and initialization
        if (BindingContext is SplashViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _animationTimer?.Stop();
        _animationTimer = null;
    }

    private void OnRadarCanvasPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Transparent);

        float width = info.Width;
        float height = info.Height;
        float centerX = width / 2f;
        float centerY = height / 2f;
        float radius = (Math.Min(width, height) / 2f) - 6f;

        if (radius <= 10) return;

        // Clip everything inside the circular radar viewport
        using var clipPath = new SKPath();
        clipPath.AddCircle(centerX, centerY, radius);
        canvas.Save();
        canvas.ClipPath(clipPath, SKClipOperation.Intersect, true);

        // --- 1. Draw Stylized Map Underlay Background ---
        using (var bgPaint = new SKPaint
        {
            Color = SKColor.Parse("#0A192F"),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            canvas.DrawRect(0, 0, width, height, bgPaint);
        }

        // Draw Map River / Waterway Curve
        using (var riverPath = new SKPath())
        {
            riverPath.MoveTo(centerX - radius * 0.9f, centerY + radius * 0.7f);
            riverPath.CubicTo(
                centerX - radius * 0.3f, centerY + radius * 0.2f,
                centerX + radius * 0.2f, centerY - radius * 0.1f,
                centerX + radius * 0.85f, centerY - radius * 0.8f);

            using var riverPaint = new SKPaint
            {
                Color = new SKColor(14, 74, 92, 110),
                StrokeWidth = radius * 0.14f,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                IsAntialias = true
            };
            canvas.DrawPath(riverPath, riverPaint);
        }

        // Draw Map Street Corridors / Road Contours
        using (var roadPaint = new SKPaint
        {
            Color = new SKColor(24, 59, 94, 120),
            StrokeWidth = 3.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        })
        {
            // Primary Arterial Road 1
            using var road1 = new SKPath();
            road1.MoveTo(centerX - radius * 0.8f, centerY - radius * 0.4f);
            road1.LineTo(centerX + radius * 0.8f, centerY + radius * 0.3f);
            canvas.DrawPath(road1, roadPaint);

            // Primary Arterial Road 2
            using var road2 = new SKPath();
            road2.MoveTo(centerX - radius * 0.2f, centerY - radius * 0.9f);
            road2.CubicTo(centerX + radius * 0.1f, centerY - radius * 0.2f, centerX - radius * 0.4f, centerY + radius * 0.4f, centerX - radius * 0.1f, centerY + radius * 0.9f);
            canvas.DrawPath(road2, roadPaint);

            // Secondary Street Contours
            using var minorRoadPaint = new SKPaint
            {
                Color = new SKColor(20, 49, 82, 90),
                StrokeWidth = 2f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };

            canvas.DrawLine(centerX - radius * 0.6f, centerY + radius * 0.1f, centerX + radius * 0.4f, centerY - radius * 0.7f, minorRoadPaint);
            canvas.DrawLine(centerX - radius * 0.5f, centerY - radius * 0.6f, centerX + radius * 0.7f, centerY - radius * 0.2f, minorRoadPaint);
            canvas.DrawLine(centerX + radius * 0.1f, centerY + radius * 0.7f, centerX + radius * 0.7f, centerY + radius * 0.1f, minorRoadPaint);
        }

        // --- 2. Draw Concentric Radar Grid Rings ---
        float[] ringFractions = { 0.25f, 0.50f, 0.75f, 1.0f };
        for (int i = 0; i < ringFractions.Length; i++)
        {
            float r = radius * ringFractions[i];
            byte alpha = (byte)(i == ringFractions.Length - 1 ? 140 : 65);

            using var ringPaint = new SKPaint
            {
                Color = new SKColor(10, 132, 145, alpha),
                StrokeWidth = i == ringFractions.Length - 1 ? 2.5f : 1.5f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, r, ringPaint);
        }

        // --- 3. Draw Crosshairs with Center Gap ---
        using (var crosshairPaint = new SKPaint
        {
            Color = new SKColor(10, 132, 145, 60),
            StrokeWidth = 1.2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        })
        {
            float gap = radius * 0.12f;
            canvas.DrawLine(centerX - radius, centerY, centerX - gap, centerY, crosshairPaint);
            canvas.DrawLine(centerX + gap, centerY, centerX + radius, centerY, crosshairPaint);
            canvas.DrawLine(centerX, centerY - radius, centerX, centerY - gap, crosshairPaint);
            canvas.DrawLine(centerX, centerY + gap, centerX, centerY + radius, crosshairPaint);
        }

        // --- 4. Draw Expanding Pulsing Ping Ripples ---
        for (int wave = 0; wave < 2; wave++)
        {
            float wavePhase = (_pulsePhase + (wave * 0.5f)) % 1f;
            float waveRadius = wavePhase * radius;
            byte waveAlpha = (byte)((1f - wavePhase) * 150);

            using var wavePaint = new SKPaint
            {
                Color = new SKColor(16, 185, 129, waveAlpha),
                StrokeWidth = 2f,
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
            };
            canvas.DrawCircle(centerX, centerY, waveRadius, wavePaint);
        }

        // --- 5. Draw 360° Rotating Radar Sweep Wedge ---
        canvas.Save();
        canvas.RotateDegrees(_sweepAngle, centerX, centerY);

        float sweepAngleSpan = 75f;
        using (var sweepPath = new SKPath())
        {
            sweepPath.MoveTo(centerX, centerY);
            sweepPath.ArcTo(
                new SKRect(centerX - radius, centerY - radius, centerX + radius, centerY + radius),
                -sweepAngleSpan,
                sweepAngleSpan,
                false);
            sweepPath.Close();

            // Gradient from transparent to vivid brand teal/mint along the sweep arc
            using var sweepShader = SKShader.CreateSweepGradient(
                new SKPoint(centerX, centerY),
                new[] { SKColor.Empty, new SKColor(10, 132, 145, 15), new SKColor(16, 185, 129, 90), new SKColor(16, 185, 129, 210) },
                new[] { 0f, 0.70f, 0.90f, 1f });

            using var sweepPaint = new SKPaint
            {
                Shader = sweepShader,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            canvas.DrawPath(sweepPath, sweepPaint);
        }

        // Leading Edge High-Intensity Ray
        using (var rayPaint = new SKPaint
        {
            Color = new SKColor(52, 211, 153, 240), // Bright mint #34D399
            StrokeWidth = 2.5f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        })
        {
            canvas.DrawLine(centerX, centerY, centerX + radius, centerY, rayPaint);
        }

        canvas.Restore();

        // --- 6. Draw Detected Radar Blips with Sweep Fade ---
        foreach (var blip in _radarBlips)
        {
            float bx = centerX + (blip.relX * radius);
            float by = centerY + (blip.relY * radius);

            // Compute angular distance from current sweep beam
            float diff = (_sweepAngle - blip.angleDeg + 360f) % 360f;
            if (diff < 90f) // Illuminated recently by the sweep
            {
                float intensity = 1f - (diff / 90f);
                byte blipAlpha = (byte)(intensity * 230);

                // Halo glow
                using var haloPaint = new SKPaint
                {
                    Color = new SKColor(52, 211, 153, (byte)(intensity * 100)),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawCircle(bx, by, 7f * (0.8f + intensity * 0.4f), haloPaint);

                // Core dot
                using var dotPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, blipAlpha),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawCircle(bx, by, 3.2f, dotPaint);
            }
            else
            {
                // Idle dormant dot
                using var idlePaint = new SKPaint
                {
                    Color = new SKColor(10, 132, 145, 45),
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                };
                canvas.DrawCircle(bx, by, 2.5f, idlePaint);
            }
        }

        // --- 7. Draw Center Target Beacon Node ---
        using (var centerGlowPaint = new SKPaint
        {
            Color = new SKColor(16, 185, 129, 90),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            canvas.DrawCircle(centerX, centerY, 11f, centerGlowPaint);
        }

        using (var centerRingPaint = new SKPaint
        {
            Color = new SKColor(52, 211, 153, 230),
            StrokeWidth = 2f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        })
        {
            canvas.DrawCircle(centerX, centerY, 6.5f, centerRingPaint);
        }

        using (var centerCorePaint = new SKPaint
        {
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        })
        {
            canvas.DrawCircle(centerX, centerY, 3f, centerCorePaint);
        }

        // Restore canvas from circular clip
        canvas.Restore();

        // Outer Vignette Ring
        using var outerBorderPaint = new SKPaint
        {
            Color = new SKColor(16, 185, 129, 160),
            StrokeWidth = 3f,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };
        canvas.DrawCircle(centerX, centerY, radius, outerBorderPaint);
    }
}


