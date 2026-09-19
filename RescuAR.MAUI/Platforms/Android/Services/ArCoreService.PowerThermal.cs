using Android.Content;
using Android.OS;
using Android.Util;
using RescuAR.AR;
using System;
using System.Threading;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Low-rate Android battery/thermal proxy sampling for adaptive AR work.
/// Battery temperature is available from Android's sticky battery intent on
/// devices where a direct application thermal sensor is not exposed.
/// </summary>
public sealed partial class ArCoreService
{
    private const long PowerThermalPollIntervalMilliseconds =
        5_000;

    private long lastPowerThermalPollTimestamp =
        long.MinValue;

    private bool powerThermalModeLogged;

    private ARPowerThermalPolicy.WorkloadDecision
        powerThermalDecision =
            ARPowerThermalPolicy.WorkloadDecision.Normal;

    private void RefreshPowerThermalDecisionIfNeeded(
        bool force)
    {
        long now =
            System.Environment.TickCount64;

        if (!force &&
            lastPowerThermalPollTimestamp !=
                long.MinValue &&
            now -
                lastPowerThermalPollTimestamp <
                    PowerThermalPollIntervalMilliseconds)
        {
            return;
        }

        lastPowerThermalPollTimestamp =
            now;

        float temperatureCelsius =
            TryReadBatteryTemperatureCelsius();

        bool powerSaveMode =
            TryReadPowerSaveMode();

        ARPowerThermalPolicy.AndroidThermalSeverity thermalSeverity =
            TryReadAndroidThermalSeverity();

        ARPowerThermalPolicy.WorkloadDecision previous =
            powerThermalDecision;

        ARPowerThermalPolicy.WorkloadDecision next =
            ARPowerThermalPolicy.Evaluate(
                temperatureCelsius,
                powerSaveMode,
                thermalSeverity,
                previous.Mode);

        powerThermalDecision =
            next;

        ARPowerThermalPolicy.PublishCurrentDecision(next);

        bool modeChanged =
            previous.Mode !=
                next.Mode;

        if (force ||
            modeChanged ||
            !powerThermalModeLogged)
        {
            powerThermalModeLogged =
                true;

            string temperatureText =
                float.IsFinite(
                    temperatureCelsius)
                    ? $"{temperatureCelsius:F1}C"
                    : "unavailable";

            Log.Info(
                Tag,
                "AR POWER/THERMAL MODE: " +
                $"mode={next.Mode}, " +
                $"previous={previous.Mode}, " +
                $"batteryTemperature={temperatureText}, " +
                $"androidThermalSeverity={thermalSeverity}, " +
                $"powerSaver={powerSaveMode}, " +
                $"maxPipelineFps={next.TargetMaximumFramesPerSecond}, " +
                $"depthIntervalMultiplier={next.DepthIntervalMultiplier}, " +
                $"groundProbeIntervalMultiplier={next.GroundProbeIntervalMultiplier}, " +
                $"maximumGroundProbesPerSweep={next.MaximumGroundProbesPerSweep}, " +
                $"floodMaskWidth={next.FloodMaskWidthPixels}, " +
                $"routeDepthAllowed={next.RouteDepthAllowed}.");
        }

        if (modeChanged)
        {
            processedFrameCount =
                0;

            fpsWindowStartTimestamp =
                now;
        }
    }

    private float TryReadBatteryTemperatureCelsius()
    {
        try
        {
            using IntentFilter filter =
                new(
                    Intent.ActionBatteryChanged);

            using Intent? battery =
                context.RegisterReceiver(
                    null,
                    filter);

            int tenthsCelsius =
                battery?.GetIntExtra(
                    BatteryManager.ExtraTemperature,
                    int.MinValue) ??
                int.MinValue;

            if (tenthsCelsius ==
                int.MinValue)
            {
                return float.NaN;
            }

            return tenthsCelsius /
                10.0f;
        }
        catch
        {
            return float.NaN;
        }
    }

    private bool TryReadPowerSaveMode()
    {
        try
        {
            PowerManager? powerManager =
                context.GetSystemService(
                    Context.PowerService) as
                PowerManager;

            return powerManager?.IsPowerSaveMode ??
                false;
        }
        catch
        {
            return false;
        }
    }

    private ARPowerThermalPolicy.AndroidThermalSeverity
        TryReadAndroidThermalSeverity()
    {
        try
        {
            PowerManager? powerManager =
                context.GetSystemService(Context.PowerService) as PowerManager;

            if (powerManager is null ||
                global::Android.OS.Build.VERSION.SdkInt <
                    global::Android.OS.BuildVersionCodes.Q)
            {
                return ARPowerThermalPolicy.AndroidThermalSeverity.Unknown;
            }

            object? rawStatus =
                powerManager.GetType()
                    .GetProperty("CurrentThermalStatus")?
                    .GetValue(powerManager);

            if (rawStatus is null)
            {
                return ARPowerThermalPolicy.AndroidThermalSeverity.Unknown;
            }

            int status = Convert.ToInt32(rawStatus);

            return Enum.IsDefined(
                    typeof(ARPowerThermalPolicy.AndroidThermalSeverity),
                    status)
                ? (ARPowerThermalPolicy.AndroidThermalSeverity)status
                : ARPowerThermalPolicy.AndroidThermalSeverity.Unknown;
        }
        catch
        {
            return ARPowerThermalPolicy.AndroidThermalSeverity.Unknown;
        }
    }

    private void ApplyFrameWorkloadDelay(
        long iterationStartedTimestamp,
        CancellationToken cancellationToken)
    {
        int minimumInterval =
            Math.Max(
                1,
                powerThermalDecision
                    .MinimumFrameIntervalMilliseconds);

        long elapsed =
            Math.Max(
                0,
                System.Environment.TickCount64 -
                    iterationStartedTimestamp);

        int remainingDelay =
            (int)Math.Max(
                0,
                minimumInterval -
                    elapsed);

        if (remainingDelay >
            0)
        {
            cancellationToken.WaitHandle.WaitOne(
                remainingDelay);
        }
    }
}
