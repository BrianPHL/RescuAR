using Android.Util;
using Google.AR.Core;
using System.Globalization;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Deterministic production camera policy. Rear-facing fixed 30 FPS is the
/// first priority, followed by a bounded 16:9 texture size close to 1280x720
/// and hardware-Depth availability. The ordering prevents a high-resolution
/// Depth configuration from silently multiplying the per-frame Vulkan work.
/// </summary>
public sealed partial class ArCoreService
{
    private const int PreferredCameraWidth = 1280;
    private const int PreferredCameraHeight = 720;
    private const int MaximumPreferredCameraWidth = 1920;
    private const int MaximumPreferredCameraHeight = 1080;

    private static void SelectProductionCameraConfiguration(
        Session currentSession)
    {
        try
        {
            using CameraConfigFilter filter =
                new(currentSession);

            IList<CameraConfig> configurations =
                currentSession.GetSupportedCameraConfigs(
                    filter);

            List<CameraConfigurationCandidate> candidates =
                [];

            for (int index = 0;
                 index < configurations.Count;
                 index++)
            {
                CameraConfig configuration =
                    configurations[index];

                if (!TryDescribeCameraConfiguration(
                        configuration,
                        index,
                        out CameraConfigurationCandidate candidate))
                {
                    continue;
                }

                Log.Info(
                    Tag,
                    "ARCORE_CAMERA_CONFIG_CANDIDATE " +
                    candidate.ToLogFields());

                if (candidate.IsRearFacing)
                {
                    candidates.Add(candidate);
                }
            }

            CameraConfigurationCandidate? selected =
                candidates
                    .OrderBy(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.Index)
                    .FirstOrDefault();

            if (selected is null)
            {
                Log.Warn(
                    Tag,
                    "ARCORE_CAMERA_CONFIG_SELECTED result=DEFAULT; " +
                    "reason='No readable rear-facing configuration was returned.'.");

                return;
            }

            currentSession.CameraConfig =
                selected.Configuration;

            Log.Info(
                Tag,
                "ARCORE_CAMERA_CONFIG_SELECTED result=POLICY; " +
                selected.ToLogFields() +
                "; policy='rear camera, fixed/supporting 30 FPS, Depth " +
                "preferred, bounded 16:9 GPU workload'.");
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                "ARCORE_CAMERA_CONFIG_SELECTED result=DEFAULT; " +
                $"failureType={exception.GetType().Name}; " +
                $"message='{exception.Message}'.");
        }
    }

    private static bool TryDescribeCameraConfiguration(
        CameraConfig configuration,
        int index,
        out CameraConfigurationCandidate candidate)
    {
        candidate = null!;

        try
        {
            string facing =
                configuration.GetFacingDirection().ToString();

            global::Android.Util.Range fpsRange =
                configuration.FpsRange;

            if (!TryReadFps(
                    fpsRange.Lower?.ToString(),
                    out int minimumFps) ||
                !TryReadFps(
                    fpsRange.Upper?.ToString(),
                    out int maximumFps))
            {
                Log.Warn(
                    Tag,
                    $"ARCORE_CAMERA_CONFIG_CANDIDATE index={index}; " +
                    "result=REJECTED; reason='FPS range was unreadable.'.");

                return false;
            }

            global::Android.Util.Size textureSize =
                configuration.TextureSize;

            global::Android.Util.Size imageSize =
                configuration.ImageSize;

            string depthUsage =
                configuration.GetDepthSensorUsage().ToString();

            string stereoUsage =
                configuration.GetStereoCameraUsage().ToString();

            bool isRearFacing =
                facing.Equals(
                    "BACK",
                    StringComparison.OrdinalIgnoreCase);

            long score =
                CalculateCameraConfigurationScore(
                    isRearFacing,
                    minimumFps,
                    maximumFps,
                    textureSize.Width,
                    textureSize.Height,
                    imageSize.Width,
                    imageSize.Height,
                    depthUsage);

            candidate =
                new CameraConfigurationCandidate(
                    index,
                    configuration,
                    configuration.CameraId ?? "unknown",
                    facing,
                    isRearFacing,
                    minimumFps,
                    maximumFps,
                    textureSize.Width,
                    textureSize.Height,
                    imageSize.Width,
                    imageSize.Height,
                    depthUsage,
                    stereoUsage,
                    score);

            return true;
        }
        catch (Exception exception)
        {
            Log.Warn(
                Tag,
                $"ARCORE_CAMERA_CONFIG_CANDIDATE index={index}; " +
                "result=REJECTED; " +
                $"failureType={exception.GetType().Name}; " +
                $"message='{exception.Message}'.");

            return false;
        }
    }

    private static long CalculateCameraConfigurationScore(
        bool isRearFacing,
        int minimumFps,
        int maximumFps,
        int textureWidth,
        int textureHeight,
        int imageWidth,
        int imageHeight,
        string depthUsage)
    {
        if (!isRearFacing ||
            textureWidth <= 0 ||
            textureHeight <= 0)
        {
            return long.MaxValue;
        }

        long fpsPenalty =
            minimumFps == 30 && maximumFps == 30
                ? 0
                : minimumFps <= 30 && maximumFps >= 30
                    ? 1
                    : 10 + Math.Abs(maximumFps - 30);

        long depthPenalty =
            depthUsage.Contains(
                "REQUIRE",
                StringComparison.OrdinalIgnoreCase)
                ? 0
                : 1;

        long textureArea =
            (long)textureWidth * textureHeight;

        long preferredArea =
            (long)PreferredCameraWidth * PreferredCameraHeight;

        long resolutionPenalty =
            Math.Abs(textureArea - preferredArea);

        if (textureWidth > MaximumPreferredCameraWidth ||
            textureHeight > MaximumPreferredCameraHeight)
        {
            resolutionPenalty +=
                50_000_000_000L;
        }

        double aspectRatio =
            textureWidth / (double)textureHeight;

        long aspectPenalty =
            (long)(Math.Abs(aspectRatio - (16.0 / 9.0)) *
                   1_000_000_000.0);

        long cpuImagePenalty =
            Math.Max(
                0L,
                (long)imageWidth * imageHeight / 8L);

        return fpsPenalty * 100_000_000_000L +
               depthPenalty * 10_000_000_000L +
               resolutionPenalty +
               aspectPenalty +
               cpuImagePenalty;
    }

    private static bool TryReadFps(
        string? value,
        out int fps) =>
        int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out fps);

    private sealed record CameraConfigurationCandidate(
        int Index,
        CameraConfig Configuration,
        string CameraId,
        string Facing,
        bool IsRearFacing,
        int MinimumFps,
        int MaximumFps,
        int TextureWidth,
        int TextureHeight,
        int ImageWidth,
        int ImageHeight,
        string DepthUsage,
        string StereoUsage,
        long Score)
    {
        public string ToLogFields() =>
            $"index={Index}; cameraId='{CameraId}'; facing={Facing}; " +
            $"fps={MinimumFps}-{MaximumFps}; " +
            $"gpuTexture={TextureWidth}x{TextureHeight}; " +
            $"cpuImage={ImageWidth}x{ImageHeight}; " +
            $"depthUsage={DepthUsage}; stereoUsage={StereoUsage}; " +
            $"score={Score}";
    }
}
