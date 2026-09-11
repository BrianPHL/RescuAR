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
///
/// Flashlight uses ARCore's FlashMode API so the retained ARCore Session
/// remains the sole owner of the physical camera.
/// </summary>
public sealed partial class ArCoreService
{
    private const float MinimumCameraZoomRatio =
        1.0f;

    private const float MaximumCameraZoomRatio =
        3.0f;

    private volatile float cameraZoomRatio =
        MinimumCameraZoomRatio;

    private volatile bool flashlightEnabled;

    /// <inheritdoc />
    public float CameraZoomRatio =>
        cameraZoomRatio;

    /// <inheritdoc />
    public bool IsFlashlightOn =>
        flashlightEnabled;

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

    /// <inheritdoc />
    public async Task<bool> SetFlashlightAsync(
        bool enabled)
    {
        Session? currentSession =
            session;

        if (currentSession is null)
        {
            Log.Warn(
                Tag,
                "Flashlight request ignored because ARCore Session is unavailable.");

            return false;
        }

        if (enabled &&
            !CurrentCameraSupportsFlash(
                currentSession))
        {
            flashlightEnabled =
                false;

            Log.Warn(
                Tag,
                "Flashlight request ignored because the active ARCore camera has no flash unit.");

            return false;
        }

        await updateGate
            .WaitAsync()
            .ConfigureAwait(false);

        try
        {
            /*
             * session.Config is a copy of the CURRENT configuration. Starting
             * from it preserves TextureUpdateMode, UpdateMode, PlaneFinding,
             * DepthMode, and any future ARCore settings when only FlashMode
             * is changed.
             */
            using Google.AR.Core.Config config =
                currentSession.Config;

            config.SetFlashMode(
                enabled
                    ? Google.AR.Core.Config.FlashMode.Torch
                    : Google.AR.Core.Config.FlashMode.Off);

            currentSession.Configure(
                config);

            flashlightEnabled =
                enabled;

            Log.Info(
                Tag,
                enabled
                    ? "ARCore flashlight enabled."
                    : "ARCore flashlight disabled.");

            return true;
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                "Unable to change ARCore flashlight state: " +
                $"{exception.GetType().Name}: {exception.Message}");

            return false;
        }
        finally
        {
            updateGate.Release();
        }
    }

    private bool CurrentCameraSupportsFlash(
        Session currentSession)
    {
        try
        {
            CameraConfig? cameraConfig =
                currentSession.CameraConfig;

            string? cameraId =
                cameraConfig?.CameraId;

            if (string.IsNullOrWhiteSpace(
                    cameraId))
            {
                return false;
            }

            var cameraManager =
                context.GetSystemService(
                    Context.CameraService)
                as global::Android.Hardware.Camera2.CameraManager;

            if (cameraManager is null)
            {
                return false;
            }

            global::Android.Hardware.Camera2.CameraCharacteristics
                characteristics =
                    cameraManager.GetCameraCharacteristics(
                        cameraId);

            Java.Lang.Object? hasFlash =
                characteristics.Get(
                    global::Android.Hardware.Camera2.CameraCharacteristics
                        .FlashInfoAvailable);

            return hasFlash is Java.Lang.Boolean value &&
                value.BooleanValue();
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "Unable to query ARCore camera flash capability: " +
                $"{exception.GetType().Name}: {exception.Message}");

            return false;
        }
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
