using Android.Content;
using Android.Hardware;
using Android.Util;
using Google.AR.Core;
using System;
using AndroidBuild = global::Android.OS.Build;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Produces one compact, Release-safe device profile for cross-device AR
/// validation. Runtime GPS, heading, tracking, and thermal results remain in
/// the existing periodic navigation and power/thermal status lines.
/// </summary>
public sealed partial class ArCoreService
{
    private void LogDeviceCompatibilityProfile(
        Session currentSession,
        ArCoreApk.Availability availability)
    {
        SensorManager? sensorManager =
            context.GetSystemService(
                Context.SensorService) as
            SensorManager;

        bool hasRotationVector =
            HasSensor(
                sensorManager,
                SensorType.RotationVector);

        bool hasMagnetometer =
            HasSensor(
                sensorManager,
                SensorType.MagneticField);

        bool hasAccelerometer =
            HasSensor(
                sensorManager,
                SensorType.Accelerometer);

        bool hasGyroscope =
            HasSensor(
                sensorManager,
                SensorType.Gyroscope);

        bool hasHeadingInputs =
            hasRotationVector ||
            (hasMagnetometer &&
             hasAccelerometer);

        bool hasGpsHardware =
            TryHasSystemFeature(
                "android.hardware.location.gps");

        string featureLevel =
            !hasHeadingInputs
                ? "LIMITED_HEADING"
                : depthModeSupported
                    ? "DEPTH_CAPABLE"
                    : "PLANE_ONLY";

        CameraConfig? cameraConfig =
            currentSession.CameraConfig;

        string cameraFps =
            TryReadCameraValue(
                cameraConfig,
                static value =>
                    value.FpsRange.ToString());

        string gpuTexture =
            TryReadCameraValue(
                cameraConfig,
                static value =>
                    $"{value.TextureSize.Width}x" +
                    $"{value.TextureSize.Height}");

        string cpuImage =
            TryReadCameraValue(
                cameraConfig,
                static value =>
                    $"{value.ImageSize.Width}x" +
                    $"{value.ImageSize.Height}");

        string cameraFacing =
            TryReadCameraValue(
                cameraConfig,
                static value =>
                    value.GetFacingDirection().ToString());

        string depthSensorUsage =
            TryReadCameraValue(
                cameraConfig,
                static value =>
                    value.GetDepthSensorUsage().ToString());

        string manufacturer =
            AndroidBuild.Manufacturer ??
            "unknown";

        string model =
            AndroidBuild.Model ??
            "unknown";

        string device =
            AndroidBuild.Device ??
            "unknown";

        int androidApi =
            (int)AndroidBuild.VERSION.SdkInt;

        Log.Info(
            Tag,
            "DEVICE COMPATIBILITY PROFILE: " +
            $"manufacturer='{manufacturer}', " +
            $"model='{model}', " +
            $"device='{device}', " +
            $"androidApi={androidApi}, " +
            $"featureLevel={featureLevel}, " +
            $"arCoreAvailability={availability}, " +
            $"depthSupported={depthModeSupported}, " +
            $"cameraFacing={cameraFacing}, " +
            $"cameraFps={cameraFps}, " +
            $"gpuTexture={gpuTexture}, " +
            $"cpuImage={cpuImage}, " +
            $"depthSensorUsage={depthSensorUsage}, " +
            $"rotationVector={hasRotationVector}, " +
            $"magnetometer={hasMagnetometer}, " +
            $"accelerometer={hasAccelerometer}, " +
            $"gyroscope={hasGyroscope}, " +
            $"gpsHardware={hasGpsHardware}.");
    }

    private static bool HasSensor(
        SensorManager? sensorManager,
        SensorType sensorType)
    {
        try
        {
            return sensorManager?
                .GetDefaultSensor(
                    sensorType) is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool TryHasSystemFeature(
        string featureName)
    {
        try
        {
            return context.PackageManager?
                .HasSystemFeature(
                    featureName) ??
                false;
        }
        catch
        {
            return false;
        }
    }

    private static string TryReadCameraValue(
        CameraConfig? cameraConfig,
        Func<CameraConfig, string> readValue)
    {
        if (cameraConfig is null)
        {
            return "unavailable";
        }

        try
        {
            return readValue(
                cameraConfig);
        }
        catch (Exception exception)
        {
            return $"unavailable-{exception.GetType().Name}";
        }
    }
}
