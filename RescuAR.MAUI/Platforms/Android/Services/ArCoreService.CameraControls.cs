using Android.Content;
using Android.Util;
using Google.AR.Core;
using RescuAR.AR;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// User-facing camera controls that must remain synchronized with ARCore.
///
/// Zoom is implemented as a centered presentation crop rather than by
/// taking ownership of Camera2. The same ratio is applied to the camera UVs
/// and AR projection for each ARCore frame, preserving AR registration.
public sealed partial class ArCoreService
{
    private const float MinimumCameraZoomRatio =
        1.0f;

    private const float MaximumCameraZoomRatio =
        3.0f;

    private volatile float cameraZoomRatio =
        MinimumCameraZoomRatio;

    /// <inheritdoc />
    public float CameraZoomRatio =>
        cameraZoomRatio;


    /// <inheritdoc />
    public void SetCameraZoomRatio(
        float zoomRatio)
    {
        float clampedZoomRatio =
            Math.Clamp(
                zoomRatio,
                MinimumCameraZoomRatio,
                MaximumCameraZoomRatio);

        if (Math.Abs(
                clampedZoomRatio -
                cameraZoomRatio) < 0.001f)
        {
            return;
        }

        cameraZoomRatio =
            clampedZoomRatio;

        /*
         * Force the next camera-frame diagnostic to print the newly cropped
         * UVs. Depth is cleared so an old unzoomed depth mapping cannot be
         * consumed with a newly zoomed camera/projection frame.
         */
        hasLoggedTransformedUv =
            false;

        ARDepthOcclusionBridge.Clear();

        lastDepthOcclusionPublishTimestamp =
            long.MinValue;

        Log.Info(
            Tag,
            $"Camera presentation zoom changed to {clampedZoomRatio:0.#}x.");
    }


    /// <summary>
    /// Converts a point expressed in the zoomed on-screen [0,1] view back to
    /// ARCore's full unzoomed VIEW_NORMALIZED coordinate system.
    /// </summary>
    private static float MapZoomedViewCoordinateToArCoreView(
        float normalizedCoordinate,
        float zoomRatio)
    {
        float safeZoomRatio =
            Math.Clamp(
                zoomRatio,
                MinimumCameraZoomRatio,
                MaximumCameraZoomRatio);

        return 0.5f +
            ((normalizedCoordinate - 0.5f) /
                safeZoomRatio);
    }

    /// <summary>
    /// Applies centered presentation zoom directly to ARCore's OpenGL
    /// projection matrix. ARCore stores this matrix column-major, therefore
    /// the first and second mathematical rows are the indices below.
    /// </summary>
    private static void ApplyCameraZoomToProjection(
        float[] projection,
        float zoomRatio)
    {
        if (projection is null ||
            projection.Length < 16)
        {
            return;
        }

        float safeZoomRatio =
            Math.Clamp(
                zoomRatio,
                MinimumCameraZoomRatio,
                MaximumCameraZoomRatio);

        if (Math.Abs(
                safeZoomRatio - 1.0f) < 0.001f)
        {
            return;
        }

        // Mathematical row 1: m11, m12, m13, m14.
        projection[0] *= safeZoomRatio;
        projection[4] *= safeZoomRatio;
        projection[8] *= safeZoomRatio;
        projection[12] *= safeZoomRatio;

        // Mathematical row 2: m21, m22, m23, m24.
        projection[1] *= safeZoomRatio;
        projection[5] *= safeZoomRatio;
        projection[9] *= safeZoomRatio;
        projection[13] *= safeZoomRatio;
    }
}
