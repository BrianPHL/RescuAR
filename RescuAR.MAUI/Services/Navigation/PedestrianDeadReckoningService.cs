using Microsoft.Maui.Devices.Sensors;
using System.Numerics;

#if ANDROID
using Android.Util;
#endif

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>
/// Capstone-sized pedestrian dead-reckoning sensor front-end.
///
/// Milestone 1 responsibility:
/// - detect conservative walking steps from accelerometer magnitude;
/// - emit one event per accepted step;
/// - do NOT estimate geographic position;
/// - do NOT move the Evergine camera.
///
/// CameraPage decides whether a detected step is directionally plausible
/// relative to the currently rendered AR route before route progress advances.
/// </summary>
public sealed class PedestrianDeadReckoningService
{
    private const string LogTag =
        "RescuAR-PDR";

    /*
     * MAUI accelerometer values are expressed in G. Magnitude is
     * orientation-independent and therefore avoids requiring a separate
     * gravity-axis transform merely to detect a footfall.
     */
    private const double GravityLowPassAlpha =
        0.90;

    /*
     * Conservative thresholds are intentional. Missing an occasional step is
     * preferable to advancing evacuation guidance while the user is merely
     * waving/rotating the phone.
     */
    private const double StepPeakThresholdG =
        0.12;

    private const double StepReleaseThresholdG =
        0.025;

    private static readonly TimeSpan MinimumStepInterval =
        TimeSpan.FromMilliseconds(
            320);

    private static readonly TimeSpan MinimumPeakDuration =
        TimeSpan.FromMilliseconds(
            40);

    private static readonly TimeSpan MaximumPeakDuration =
        TimeSpan.FromMilliseconds(
            650);

    private readonly object sync =
        new();

    private bool isRunning;

    private bool hasGravityEstimate;

    private double gravityMagnitudeG =
        1.0;

    private bool peakArmed;

    private DateTimeOffset peakStartedUtc;

    private double peakDynamicG;

    private DateTimeOffset lastStepUtc =
        DateTimeOffset.MinValue;

    private long detectedStepCount;

    public event EventHandler<PdrStepDetectedEventArgs>?
        StepDetected;

    public bool IsSupported =>
        Accelerometer.Default.IsSupported;

    public bool IsRunning
    {
        get
        {
            lock (sync)
            {
                return isRunning;
            }
        }
    }

    public long DetectedStepCount
    {
        get
        {
            lock (sync)
            {
                return detectedStepCount;
            }
        }
    }

    public bool Start()
    {
        lock (sync)
        {
            if (isRunning)
            {
                return true;
            }

            if (!Accelerometer.Default.IsSupported)
            {
#if ANDROID
                Log.Warn(
                    LogTag,
                    "PDR step detector cannot start: accelerometer is not supported.");
#endif
                return false;
            }

            ResetDetectorStateLocked();

            Accelerometer.Default.ReadingChanged +=
                OnAccelerometerReadingChanged;

            try
            {
                Accelerometer.Default.Start(
                    SensorSpeed.UI);

                isRunning =
                    true;
            }
            catch
            {
                Accelerometer.Default.ReadingChanged -=
                    OnAccelerometerReadingChanged;

                ResetDetectorStateLocked();

                throw;
            }
        }

#if ANDROID
        Log.Debug(
            LogTag,
            "PDR accelerometer step detector STARTED. " +
            $"peakThreshold={StepPeakThresholdG:F3}g, " +
            $"minimumInterval={MinimumStepInterval.TotalMilliseconds:F0}ms.");
#endif

        return true;
    }

    public void Stop()
    {
        bool shouldStop;

        lock (sync)
        {
            shouldStop =
                isRunning;

            isRunning =
                false;

            peakArmed =
                false;
        }

        if (!shouldStop)
        {
            return;
        }

        try
        {
            Accelerometer.Default.ReadingChanged -=
                OnAccelerometerReadingChanged;

            if (Accelerometer.Default.IsMonitoring)
            {
                Accelerometer.Default.Stop();
            }
        }
        catch
        {
            // Best-effort sensor shutdown on page exit.
        }

#if ANDROID
        Log.Debug(
            LogTag,
            $"PDR accelerometer step detector STOPPED. " +
            $"detectedSteps={DetectedStepCount}.");
#endif
    }

    private void OnAccelerometerReadingChanged(
        object? sender,
        AccelerometerChangedEventArgs e)
    {
        Vector3 acceleration =
            e.Reading.Acceleration;

        double magnitudeG =
            Math.Sqrt(
                acceleration.X *
                    acceleration.X +
                acceleration.Y *
                    acceleration.Y +
                acceleration.Z *
                    acceleration.Z);

        if (!double.IsFinite(
                magnitudeG))
        {
            return;
        }

        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        PdrStepDetectedEventArgs? acceptedStep =
            null;

        lock (sync)
        {
            if (!isRunning)
            {
                return;
            }

            if (!hasGravityEstimate)
            {
                gravityMagnitudeG =
                    magnitudeG;

                hasGravityEstimate =
                    true;

                return;
            }

            gravityMagnitudeG =
                GravityLowPassAlpha *
                    gravityMagnitudeG +
                (1.0 -
                 GravityLowPassAlpha) *
                    magnitudeG;

            double dynamicG =
                magnitudeG -
                gravityMagnitudeG;

            if (!peakArmed)
            {
                if (dynamicG >=
                        StepPeakThresholdG &&
                    now -
                        lastStepUtc >=
                        MinimumStepInterval)
                {
                    peakArmed =
                        true;

                    peakStartedUtc =
                        now;

                    peakDynamicG =
                        dynamicG;
                }

                return;
            }

            peakDynamicG =
                Math.Max(
                    peakDynamicG,
                    dynamicG);

            TimeSpan peakAge =
                now -
                peakStartedUtc;

            if (peakAge >
                MaximumPeakDuration)
            {
                peakArmed =
                    false;

                return;
            }

            if (dynamicG >
                StepReleaseThresholdG)
            {
                return;
            }

            peakArmed =
                false;

            if (peakAge <
                MinimumPeakDuration ||
                now -
                    lastStepUtc <
                MinimumStepInterval)
            {
                return;
            }

            lastStepUtc =
                now;

            detectedStepCount++;

            acceptedStep =
                new PdrStepDetectedEventArgs(
                    detectedStepCount,
                    now,
                    peakDynamicG,
                    magnitudeG);
        }

        if (acceptedStep is null)
        {
            return;
        }

#if ANDROID && DEBUG
        Log.Debug(
            LogTag,
            "STEP detected: " +
            $"step={acceptedStep.StepNumber}, " +
            $"peakDynamic={acceptedStep.PeakDynamicAccelerationG:F3}g.");
#endif

        StepDetected?.Invoke(
            this,
            acceptedStep);
    }

    private void ResetDetectorStateLocked()
    {
        hasGravityEstimate =
            false;

        gravityMagnitudeG =
            1.0;

        peakArmed =
            false;

        peakStartedUtc =
            default;

        peakDynamicG =
            0.0;

        lastStepUtc =
            DateTimeOffset.MinValue;

        detectedStepCount =
            0;
    }

    public sealed record PdrStepDetectedEventArgs(
        long StepNumber,
        DateTimeOffset TimestampUtc,
        double PeakDynamicAccelerationG,
        double AccelerationMagnitudeG);
}
