using System.Buffers;
using System.Numerics;
using Android.Util;
using Google.AR.Core;
using RescuAR.AR;
using Frame = Google.AR.Core.Frame;
using Config = Google.AR.Core.Config;

namespace RescuAR.MAUI.Platforms.Android.Services;

public sealed partial class ArCoreService
{
    private bool roadSemanticsEnabled;
    private long lastRoadSurfaceSample = long.MinValue;
    private long lastRoadSurfaceLog = long.MinValue;
    private string? lastRoadSurfaceStatus;

    private void ConfigureRoadSemantics(Session currentSession, Config config)
    {
        roadSemanticsEnabled = currentSession.IsSemanticModeSupported(Config.SemanticMode.Enabled);
        if (roadSemanticsEnabled) config.SetSemanticMode(Config.SemanticMode.Enabled);
        ARRoadSurfaceBridge.SetStatus(roadSemanticsEnabled
            ? "Looking for the visible road" : "Automatic road alignment is unavailable on this device");
        Log.Info(Tag, $"NAV_ROAD_CAPABILITY semantics={roadSemanticsEnabled}; provider=LOCAL_ARCORE; cloud=False.");
    }

    private void PublishRoadSurface(Frame frame)
    {
        var request = ARRoadSurfaceBridge.CurrentRequest;
        var spatial = ARCameraPoseBridge.CurrentFrame;
        long now = Environment.TickCount64;
        if (!roadSemanticsEnabled || request is null || !spatial.IsFresh || !spatial.IsTracking ||
            !spatial.Anchor.IsVerified || spatial.DisplayGeometry.Rotation != 0 ||
            spatial.FrameTimestamp != frame.Timestamp ||
            now - lastRoadSurfaceSample is >= 0 and < 500) return;
        lastRoadSurfaceSample = now;
        byte[]? labels = null, confidence = null;
        try
        {
            using var image = frame.AcquireSemanticImage();
            using var confidenceImage = frame.AcquireSemanticConfidenceImage();
            int width = image.Width, height = image.Height;
            string dimensions = $"semantic={width}x{height}; confidence={confidenceImage.Width}x{confidenceImage.Height}; " +
                $"view={spatial.DisplayGeometry.Width}x{spatial.DisplayGeometry.Height}; " +
                $"semanticNs={image.Timestamp}; confidenceNs={confidenceImage.Timestamp}";
            // Acquiring both outputs from this Frame pairs them. Raw semantic
            // textures are not viewport pixels; ARCore supplies their UV mapping.
            if (width <= 4 || height <= 4 || width > 1024 || height > 2048 ||
                confidenceImage.Width != width || confidenceImage.Height != height)
            {
                RoadSurfaceStatus("Road image geometry unavailable", now, dimensions);
                return;
            }
            var labelPlane = image.GetPlanes()?[0];
            var confidencePlane = confidenceImage.GetPlanes()?[0];
            if (labelPlane?.Buffer is not { } labelBuffer || confidencePlane?.Buffer is not { } confidenceBuffer ||
                labelPlane.PixelStride != 1 || confidencePlane.PixelStride != 1 ||
                labelPlane.RowStride < width || confidencePlane.RowStride < width) return;
            int labelLength = checked((height - 1) * labelPlane.RowStride + width);
            int confidenceLength = checked((height - 1) * confidencePlane.RowStride + width);
            if (labelLength > labelBuffer.Remaining() || confidenceLength > confidenceBuffer.Remaining()) return;
            labels = ArrayPool<byte>.Shared.Rent(labelLength);
            confidence = ArrayPool<byte>.Shared.Rent(confidenceLength);
            labelBuffer.Get(labels, 0, labelLength);
            confidenceBuffer.Get(confidence, 0, confidenceLength);
            float[] uv = new float[8];
            frame.TransformCoordinates2d(Coordinates2d.ViewNormalized,
                new float[] { 0, 0, 1, 0, 0, 1, 1, 1 }, Coordinates2d.TextureNormalized, uv);
            if (!SemanticImageCoordinates.TryReproject(labels, confidence, width, height,
                labelPlane.RowStride, confidencePlane.RowStride,
                checked((int)spatial.DisplayGeometry.Width), checked((int)spatial.DisplayGeometry.Height), uv, out var view))
            {
                RoadSurfaceStatus("Road image transform unavailable", now, dimensions);
                return;
            }
            var components = SemanticRoadBoundaries.Extract(view.Labels, view.Confidence, view.Width, view.Height,
                view.Width, view.Width, request.Label);
            RoadSurfaceAlignment.RoadLine line = default;
            int qualified = 0, boundaryCount = 0, imageBoundaryCount = 0, groundBoundaryCount = 0;
            foreach (var component in components)
            {
                imageBoundaryCount += component.Count;
                var boundaries = new List<RoadSurfaceAlignment.Boundary>();
                foreach (var pixel in component)
                    if (RoadSurfaceAlignment.TryGroundPoint((pixel.X + .5f) / view.Width, (pixel.Y + .5f) / view.Height,
                        spatial, cameraZoomRatio, out var point)) boundaries.Add(new(point, pixel.Cropped));
                groundBoundaryCount += boundaries.Count;
                if (!RoadSurfaceAlignment.TryFit(boundaries, new Vector2(spatial.Pose.PositionX, spatial.Pose.PositionZ),
                    request.Direction, out var fitted, request.Label == 5 ? .8f : 2, request.Label == 5 ? 6 : 15)) continue;
                qualified++; line = fitted; boundaryCount = boundaries.Count;
            }
            if (qualified != 1)
            {
                RoadSurfaceStatus(qualified > 1 ? "Visible road alignment is ambiguous" : "Looking for clear road edges", now,
                    dimensions + $"; viewMask={view.Width}x{view.Height}; components={components.Count}; qualified={qualified}; " +
                    $"imageEdges={imageBoundaryCount}; groundEdges={groundBoundaryCount}; " +
                    $"expectedDirection=({request.Direction.X:F3},{request.Direction.Y:F3}); " +
                    $"cameraHeight={spatial.Pose.PositionY - spatial.Anchor.PositionY:F2}");
                return;
            }
            ARRoadSurfaceBridge.Observe(request, line, spatial);
            // Both images were acquired synchronously from this Frame. The
            // API defines them as its current semantic result; their optional
            // image timestamp is not assumed to equal camera frame time.
            RoadSurfaceStatus(ARRoadSurfaceBridge.Status, now, $"width={width}; height={height}; " +
                $"boundaryPoints={boundaryCount}; sections={line.Sections}; span={line.Span:F2}; " +
                $"fit={line.Residual:F3}; roadWidth={line.Width:F2}; " +
                $"center=({line.Center.X:F3},{line.Center.Y:F3}); direction=({line.Direction.X:F5},{line.Direction.Y:F5}); " +
                $"evidence={ARRoadSurfaceBridge.Current?.Evidence}; frameNs={spatial.FrameTimestamp}; " +
                $"semanticNs={image.Timestamp}; " +
                $"session={spatial.Generation.SessionGeneration}; ground={spatial.Anchor.ReferenceGeneration}; " +
                $"epoch={spatial.TrackingEpoch}; world={spatial.WorldFrame}");
        }
        catch (Google.AR.Core.Exceptions.NotYetAvailableException)
        {
            RoadSurfaceStatus("Looking for the visible road", now);
        }
        catch (Exception ex)
        {
            RoadSurfaceStatus("Road alignment observation unavailable", now, $"failure={ex.GetType().Name}");
        }
        finally
        {
            if (labels is not null) ArrayPool<byte>.Shared.Return(labels);
            if (confidence is not null) ArrayPool<byte>.Shared.Return(confidence);
        }
    }

    private void RoadSurfaceStatus(string status, long now, string detail = "")
    {
        ARRoadSurfaceBridge.SetStatus(status);
        if (status == lastRoadSurfaceStatus && now - lastRoadSurfaceLog < 5000) return;
        lastRoadSurfaceStatus = status; lastRoadSurfaceLog = now;
        Log.Debug(Tag, $"NAV_ROAD_SURFACE status='{status}'; {detail}; movementRequired=False; cloud=False.");
    }
}
