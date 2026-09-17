using Microsoft.Maui.Dispatching;
using RescuAR.AR;
using RescuAR.Diagnostics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System;
using System.Numerics;

namespace RescuAR.App.Views.Camera;

/// <summary>
/// Depth-aware flood compositor.
///
/// V7.3 keeps the V7.2 observer-mode depth classification, but substantially
/// reduces sustained CPU/UI-thread work for better thermals on mobile devices:
/// - occlusion refresh is capped at 10 Hz;
/// - the mask is rasterized at 120 px wide and scaled by Skia;
/// - screen-to-depth sampling/ray data is cached instead of recomputed for
///   every pixel on every depth frame;
/// - only the world-Y component of each reconstructed point is evaluated,
///   avoiding a full quaternion Vector3 transform per mask pixel;
/// - the canvas is invalidated only when a new flood/depth version exists.
/// </summary>
public sealed class FloodDepthOcclusionView : SKCanvasView
{
    private const int TargetMaskWidthPixels = 120;
    private const int TargetRefreshMilliseconds = 100;

    private const string LogTag = "RescuAR-FloodDepth";

    private const ushort MinimumTrustedDepthMillimeters = 180;
    private const ushort MaximumTrustedDepthMillimeters = 8000;

    private const float SurfaceTransitionMeters = 0.06f;

    private const byte EstimatedFloodMaximumAlpha = 56;

    private const float MaximumGroundCorrectionPerDepthFrameMeters =
        0.08f;

    private static readonly SKColor FloodColor =
        new(0x00, 0xA6, 0xC8, 104);

    private static readonly SKColor Transparent =
        new(0, 0, 0, 0);

    private static readonly SKColor[] FloodColorsByAlpha =
        CreateFloodColorsByAlpha();

    private IDispatcherTimer? redrawTimer;
    private SKBitmap? maskBitmap;
    private SKColor[]? maskPixels;

    private int[]? cachedDepthX;
    private int[]? cachedDepthY;
    private float[]? cachedRayX;
    private float[]? cachedRayY;
    private bool[]? cachedSampleValid;

    private int cachedMappingMaskWidth = -1;
    private int cachedMappingMaskHeight = -1;
    private int cachedMappingDepthWidth = -1;
    private int cachedMappingDepthHeight = -1;
    private int cachedMappingTextureWidth = -1;
    private int cachedMappingTextureHeight = -1;
    private float cachedMappingFocalLengthX = float.NaN;
    private float cachedMappingFocalLengthY = float.NaN;
    private float cachedMappingPrincipalPointX = float.NaN;
    private float cachedMappingPrincipalPointY = float.NaN;
    private readonly float[] cachedMappingUv = new float[8];
    private bool hasCachedMappingUv;

    private long renderedDepthVersion = -1;
    private long renderedFloodVersion = -1;
    private int renderedCanvasWidth = -1;
    private int renderedCanvasHeight = -1;

    private long requestedDepthVersion = long.MinValue;
    private long requestedFloodVersion = long.MinValue;

    private bool depthActiveLogged;
    private long lastLoggedFloodVersion = -1;

    private bool hasDisplayedGroundWorldY;
    private float displayedGroundWorldY;
    private bool? lastLoggedGroundWasProvisional;

    public FloodDepthOcclusionView()
    {
        InputTransparent = true;
        IgnorePixelScaling = true;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        PaintSurface += OnPaintSurface;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (redrawTimer is not null)
        {
            return;
        }

        requestedDepthVersion = long.MinValue;
        requestedFloodVersion = long.MinValue;

        redrawTimer = Dispatcher.CreateTimer();
        redrawTimer.Interval = TimeSpan.FromMilliseconds(TargetRefreshMilliseconds);
        redrawTimer.IsRepeating = true;
        redrawTimer.Tick += OnRedrawTimerTick;
        redrawTimer.Start();

        // Ensure a stale mask from a previous load is immediately cleared.
        InvalidateSurface();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (redrawTimer is not null)
        {
            redrawTimer.Stop();
            redrawTimer.Tick -= OnRedrawTimerTick;
            redrawTimer = null;
        }

        DisposeMask();
    }

    private void OnRedrawTimerTick(object? sender, EventArgs e)
    {
        ARFloodDepthBridge.FloodDepthSnapshot flood =
            ARFloodDepthBridge.Current;

        ARDepthOcclusionBridge.DepthSnapshot depth =
            ARDepthOcclusionBridge.Current;

        // Avoid a paint pass when nothing changed. With V7.3 the depth bridge
        // publishes at <=10 Hz, so the UI cannot accidentally spin at the
        // device display refresh rate while the camera itself remains smooth.
        if (flood.Version == requestedFloodVersion &&
            depth.Version == requestedDepthVersion)
        {
            return;
        }

        requestedFloodVersion = flood.Version;
        requestedDepthVersion = depth.Version;
        InvalidateSurface();
    }

    private void OnPaintSurface(
        object? sender,
        SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        ARFloodDepthBridge.FloodDepthSnapshot flood =
            ARFloodDepthBridge.Current;

        if (!flood.IsAvailable ||
            !float.IsFinite(flood.LocalDepthMeters) ||
            flood.LocalDepthMeters <= 0.0f)
        {
            renderedFloodVersion = -1;
            renderedDepthVersion = -1;
            hasDisplayedGroundWorldY = false;
            return;
        }

        ARDepthOcclusionBridge.DepthSnapshot depth =
            ARDepthOcclusionBridge.Current;

        if (!depth.IsAvailable ||
            !depth.GroundAvailable ||
            depth.DepthMillimeters.Length < depth.Width * depth.Height ||
            depth.ViewToTextureUv.Length < 8)
        {
            // Never show an unoccluded fallback. Until a trustworthy ARCore
            // depth snapshot exists, transparent is safer than a false level.
            hasDisplayedGroundWorldY = false;
            return;
        }

        int canvasWidth = Math.Max(1, e.Info.Width);
        int canvasHeight = Math.Max(1, e.Info.Height);

        bool rebuild =
            depth.Version != renderedDepthVersion ||
            flood.Version != renderedFloodVersion ||
            canvasWidth != renderedCanvasWidth ||
            canvasHeight != renderedCanvasHeight;

        if (rebuild)
        {
            BuildMask(
                depth,
                flood.Version,
                flood.LocalDepthMeters,
                canvasWidth,
                canvasHeight);

            renderedDepthVersion = depth.Version;
            renderedFloodVersion = flood.Version;
            renderedCanvasWidth = canvasWidth;
            renderedCanvasHeight = canvasHeight;
        }

        if (maskBitmap is null)
        {
            return;
        }

        canvas.DrawBitmap(
            maskBitmap,
            new SKRect(0, 0, canvasWidth, canvasHeight));
    }

    private void BuildMask(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        long floodVersion,
        float floodDepthMeters,
        int canvasWidth,
        int canvasHeight)
    {
        // 120 px wide is enough for the already low-resolution ARCore depth
        // field while reducing per-frame mask work by roughly half versus the
        // previous 160 px mask on the Galaxy A54 field device.
        int maskWidth =
            Math.Min(
                TargetMaskWidthPixels,
                Math.Max(1, canvasWidth));

        int maskHeight =
            Math.Max(
                1,
                (int)MathF.Round(
                    maskWidth *
                    (canvasHeight / (float)canvasWidth)));

        EnsureMaskStorage(maskWidth, maskHeight);
        EnsureSampleMapping(depth, maskWidth, maskHeight);

        if (maskBitmap is null ||
            maskPixels is null ||
            cachedDepthX is null ||
            cachedDepthY is null ||
            cachedRayX is null ||
            cachedRayY is null ||
            cachedSampleValid is null)
        {
            return;
        }

        float renderedGroundWorldY =
            GetSmoothedGroundWorldY(
                depth.GroundWorldY);

        float waterSurfaceWorldY =
            renderedGroundWorldY + floodDepthMeters;

        byte maximumFloodAlpha =
            depth.GroundIsProvisional
                ? EstimatedFloodMaximumAlpha
                : FloodColor.Alpha;

        Quaternion cameraRotation =
            Quaternion.Normalize(
                new Quaternion(
                    depth.RotationX,
                    depth.RotationY,
                    depth.RotationZ,
                    depth.RotationW));

        // We only need world Y. Rotate the three camera basis axes once per
        // depth frame rather than calling Vector3.Transform for every pixel.
        float cameraXAxisWorldY =
            Vector3.Transform(Vector3.UnitX, cameraRotation).Y;

        float cameraYAxisWorldY =
            Vector3.Transform(Vector3.UnitY, cameraRotation).Y;

        float cameraZAxisWorldY =
            Vector3.Transform(Vector3.UnitZ, cameraRotation).Y;

        float cameraWorldY = depth.CameraY;

        bool cameraBelowSimulatedSurface =
            cameraWorldY < waterSurfaceWorldY - 0.02f;

        int tintedPixelCount = 0;
        int pixelCount = maskPixels.Length;

        for (int i = 0; i < pixelCount; i++)
        {
            if (!cachedSampleValid[i] ||
                !TrySampleDepthFast(
                    depth,
                    cachedDepthX[i],
                    cachedDepthY[i],
                    out ushort depthMillimeters))
            {
                maskPixels[i] = Transparent;
                continue;
            }

            float zMeters = depthMillimeters * 0.001f;

            // ARCore camera coordinates use -Z forward. cachedRayX/RayY are
            // the X/Z and Y/Z factors for this sample, so only the vertical
            // component of the world transform must be evaluated.
            float verticalRayFactor =
                cachedRayX[i] * cameraXAxisWorldY +
                cachedRayY[i] * cameraYAxisWorldY -
                cameraZAxisWorldY;

            float worldY =
                cameraWorldY +
                zMeters * verticalRayFactor;

            float belowSurface =
                waterSurfaceWorldY - worldY;

            if (belowSurface <= -SurfaceTransitionMeters)
            {
                maskPixels[i] = Transparent;
                continue;
            }

            float visibility =
                Math.Clamp(
                    (belowSurface + SurfaceTransitionMeters) /
                    (SurfaceTransitionMeters * 2.0f),
                    0.0f,
                    1.0f);

            int alpha =
                Math.Clamp(
                    (int)MathF.Round(maximumFloodAlpha * visibility),
                    0,
                    maximumFloodAlpha);

            maskPixels[i] = FloodColorsByAlpha[alpha];

            if (alpha > 0)
            {
                tintedPixelCount++;
            }
        }

        maskBitmap.Pixels = maskPixels;

        if (!depthActiveLogged ||
            lastLoggedFloodVersion != floodVersion ||
            lastLoggedGroundWasProvisional !=
                depth.GroundIsProvisional)
        {
            depthActiveLogged = true;
            lastLoggedFloodVersion = floodVersion;
            lastLoggedGroundWasProvisional =
                depth.GroundIsProvisional;

            float tintedPercent =
                pixelCount > 0
                    ? tintedPixelCount * 100.0f / pixelCount
                    : 0.0f;

            AndroidLog.Info(
                LogTag,
                "AR FLOOD DEPTH V7.3 OCCLUSION ACTIVE: " +
                $"floodVersion={floodVersion}, " +
                $"depthFrameVersion={depth.Version}, " +
                $"depth={floodDepthMeters:F2} m, " +
                $"groundMode={(depth.GroundIsProvisional ? "PROVISIONAL_ESTIMATE" : "VERIFIED")}, " +
                $"targetGroundWorldY={depth.GroundWorldY:F3} m, " +
                $"renderedGroundWorldY={renderedGroundWorldY:F3} m, " +
                $"surfaceWorldY={waterSurfaceWorldY:F3} m, " +
                $"cameraWorldY={cameraWorldY:F3} m, " +
                $"mask={maskWidth}x{maskHeight}, " +
                $"tinted={tintedPercent:F1}%, " +
                $"cameraBelowSimulatedSurface={cameraBelowSimulatedSurface}, " +
                $"maximumAlpha={maximumFloodAlpha}, " +
                "refreshCap=10Hz, mode=OBSERVER_DEPTH_CLASSIFICATION_PERFORMANCE.");
        }
    }

    private float GetSmoothedGroundWorldY(
        float targetGroundWorldY)
    {
        if (!hasDisplayedGroundWorldY ||
            !float.IsFinite(
                displayedGroundWorldY))
        {
            displayedGroundWorldY =
                targetGroundWorldY;

            hasDisplayedGroundWorldY =
                true;

            return displayedGroundWorldY;
        }

        float correction =
            targetGroundWorldY -
            displayedGroundWorldY;

        displayedGroundWorldY +=
            Math.Clamp(
                correction,
                -MaximumGroundCorrectionPerDepthFrameMeters,
                MaximumGroundCorrectionPerDepthFrameMeters);

        return displayedGroundWorldY;
    }

    private void EnsureSampleMapping(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        int maskWidth,
        int maskHeight)
    {
        int pixelCount = maskWidth * maskHeight;

        bool mappingMatches =
            cachedDepthX is not null &&
            cachedDepthY is not null &&
            cachedRayX is not null &&
            cachedRayY is not null &&
            cachedSampleValid is not null &&
            cachedDepthX.Length == pixelCount &&
            cachedMappingMaskWidth == maskWidth &&
            cachedMappingMaskHeight == maskHeight &&
            cachedMappingDepthWidth == depth.Width &&
            cachedMappingDepthHeight == depth.Height &&
            cachedMappingTextureWidth == depth.TextureWidth &&
            cachedMappingTextureHeight == depth.TextureHeight &&
            NearlyEqual(cachedMappingFocalLengthX, depth.FocalLengthX) &&
            NearlyEqual(cachedMappingFocalLengthY, depth.FocalLengthY) &&
            NearlyEqual(cachedMappingPrincipalPointX, depth.PrincipalPointX) &&
            NearlyEqual(cachedMappingPrincipalPointY, depth.PrincipalPointY) &&
            UvMatches(depth.ViewToTextureUv);

        if (mappingMatches)
        {
            return;
        }

        cachedDepthX = new int[pixelCount];
        cachedDepthY = new int[pixelCount];
        cachedRayX = new float[pixelCount];
        cachedRayY = new float[pixelCount];
        cachedSampleValid = new bool[pixelCount];

        float[] uv = depth.ViewToTextureUv;

        for (int y = 0; y < maskHeight; y++)
        {
            float viewV = (y + 0.5f) / maskHeight;

            for (int x = 0; x < maskWidth; x++)
            {
                int index = y * maskWidth + x;
                float viewU = (x + 0.5f) / maskWidth;

                TransformViewToTexture(
                    uv,
                    viewU,
                    viewV,
                    out float textureU,
                    out float textureV);

                if (textureU < 0.0f ||
                    textureU > 1.0f ||
                    textureV < 0.0f ||
                    textureV > 1.0f)
                {
                    cachedSampleValid[index] = false;
                    continue;
                }

                cachedDepthX[index] =
                    Math.Clamp(
                        (int)(textureU * depth.Width),
                        0,
                        depth.Width - 1);

                cachedDepthY[index] =
                    Math.Clamp(
                        (int)(textureV * depth.Height),
                        0,
                        depth.Height - 1);

                float texturePixelX = textureU * depth.TextureWidth;
                float texturePixelY = textureV * depth.TextureHeight;

                cachedRayX[index] =
                    (texturePixelX - depth.PrincipalPointX) /
                    depth.FocalLengthX;

                cachedRayY[index] =
                    -(texturePixelY - depth.PrincipalPointY) /
                    depth.FocalLengthY;

                cachedSampleValid[index] = true;
            }
        }

        cachedMappingMaskWidth = maskWidth;
        cachedMappingMaskHeight = maskHeight;
        cachedMappingDepthWidth = depth.Width;
        cachedMappingDepthHeight = depth.Height;
        cachedMappingTextureWidth = depth.TextureWidth;
        cachedMappingTextureHeight = depth.TextureHeight;
        cachedMappingFocalLengthX = depth.FocalLengthX;
        cachedMappingFocalLengthY = depth.FocalLengthY;
        cachedMappingPrincipalPointX = depth.PrincipalPointX;
        cachedMappingPrincipalPointY = depth.PrincipalPointY;

        for (int i = 0; i < cachedMappingUv.Length; i++)
        {
            cachedMappingUv[i] = uv[i];
        }

        hasCachedMappingUv = true;
    }

    private bool UvMatches(float[] uv)
    {
        if (!hasCachedMappingUv || uv.Length < 8)
        {
            return false;
        }

        for (int i = 0; i < 8; i++)
        {
            if (!NearlyEqual(cachedMappingUv[i], uv[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TrySampleDepthFast(
        ARDepthOcclusionBridge.DepthSnapshot depth,
        int centerX,
        int centerY,
        out ushort depthMillimeters)
    {
        int width = depth.Width;
        int height = depth.Height;
        ushort[] values = depth.DepthMillimeters;

        ushort center = values[centerY * width + centerX];

        if (IsTrustedDepth(center))
        {
            depthMillimeters = center;
            return true;
        }

        // Smoothed depth is normally dense. Only invalid center pixels pay for
        // a four-neighbor fallback instead of searching a full 3x3 region.
        if (centerX > 0)
        {
            ushort candidate = values[centerY * width + centerX - 1];
            if (IsTrustedDepth(candidate))
            {
                depthMillimeters = candidate;
                return true;
            }
        }

        if (centerX + 1 < width)
        {
            ushort candidate = values[centerY * width + centerX + 1];
            if (IsTrustedDepth(candidate))
            {
                depthMillimeters = candidate;
                return true;
            }
        }

        if (centerY > 0)
        {
            ushort candidate = values[(centerY - 1) * width + centerX];
            if (IsTrustedDepth(candidate))
            {
                depthMillimeters = candidate;
                return true;
            }
        }

        if (centerY + 1 < height)
        {
            ushort candidate = values[(centerY + 1) * width + centerX];
            if (IsTrustedDepth(candidate))
            {
                depthMillimeters = candidate;
                return true;
            }
        }

        depthMillimeters = 0;
        return false;
    }

    private static bool IsTrustedDepth(ushort depthMillimeters) =>
        depthMillimeters >= MinimumTrustedDepthMillimeters &&
        depthMillimeters <= MaximumTrustedDepthMillimeters;

    private static void TransformViewToTexture(
        float[] uv,
        float viewU,
        float viewV,
        out float textureU,
        out float textureV)
    {
        float topU = Lerp(uv[0], uv[2], viewU);
        float topV = Lerp(uv[1], uv[3], viewU);
        float bottomU = Lerp(uv[4], uv[6], viewU);
        float bottomV = Lerp(uv[5], uv[7], viewU);

        textureU = Lerp(topU, bottomU, viewV);
        textureV = Lerp(topV, bottomV, viewV);
    }

    private void EnsureMaskStorage(int width, int height)
    {
        if (maskBitmap is not null &&
            maskBitmap.Width == width &&
            maskBitmap.Height == height &&
            maskPixels is not null &&
            maskPixels.Length == width * height)
        {
            return;
        }

        DisposeBitmapOnly();

        maskBitmap =
            new SKBitmap(
                width,
                height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);

        maskPixels = new SKColor[width * height];
    }

    private void DisposeMask()
    {
        DisposeBitmapOnly();

        cachedDepthX = null;
        cachedDepthY = null;
        cachedRayX = null;
        cachedRayY = null;
        cachedSampleValid = null;
        cachedMappingMaskWidth = -1;
        cachedMappingMaskHeight = -1;
        cachedMappingDepthWidth = -1;
        cachedMappingDepthHeight = -1;
        cachedMappingTextureWidth = -1;
        cachedMappingTextureHeight = -1;
        cachedMappingFocalLengthX = float.NaN;
        cachedMappingFocalLengthY = float.NaN;
        cachedMappingPrincipalPointX = float.NaN;
        cachedMappingPrincipalPointY = float.NaN;
        hasCachedMappingUv = false;

        renderedDepthVersion = -1;
        renderedFloodVersion = -1;
        requestedDepthVersion = long.MinValue;
        requestedFloodVersion = long.MinValue;
        depthActiveLogged = false;
        lastLoggedFloodVersion = -1;
        hasDisplayedGroundWorldY = false;
        displayedGroundWorldY = 0.0f;
        lastLoggedGroundWasProvisional = null;
    }

    private void DisposeBitmapOnly()
    {
        maskBitmap?.Dispose();
        maskBitmap = null;
        maskPixels = null;
    }

    private static SKColor[] CreateFloodColorsByAlpha()
    {
        SKColor[] colors = new SKColor[FloodColor.Alpha + 1];
        colors[0] = Transparent;

        for (int alpha = 1; alpha < colors.Length; alpha++)
        {
            colors[alpha] =
                new SKColor(
                    FloodColor.Red,
                    FloodColor.Green,
                    FloodColor.Blue,
                    (byte)alpha);
        }

        return colors;
    }

    private static bool NearlyEqual(float a, float b) =>
        MathF.Abs(a - b) <= 0.0001f;

    private static float Lerp(float a, float b, float amount) =>
        a + (b - a) * amount;
}
