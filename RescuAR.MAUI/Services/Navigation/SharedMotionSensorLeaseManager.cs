using Microsoft.Maui.Devices.Sensors;

#if ANDROID
using Android.Util;
#endif

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>
/// Owns the process-wide MAUI orientation and accelerometer services. Each
/// consumer receives an idempotent lease; the underlying sensor is stopped
/// only after the final lease is released and only when this manager started
/// that sensor.
/// </summary>
public static class SharedMotionSensorLeaseManager
{
    private const string LogTag = "RescuAR-SensorLease";

    private static readonly object orientationSync = new();
    private static readonly Dictionary<long, EventHandler<OrientationSensorChangedEventArgs>>
        orientationHandlers = new();
    private static bool orientationSubscribed;
    private static bool orientationStartedByManager;

    private static readonly object accelerometerSync = new();
    private static readonly Dictionary<long, EventHandler<AccelerometerChangedEventArgs>>
        accelerometerHandlers = new();
    private static bool accelerometerSubscribed;
    private static bool accelerometerStartedByManager;

    private static long leaseSequence;

    public static IDisposable AcquireOrientation(
        string owner,
        EventHandler<OrientationSensorChangedEventArgs> handler,
        SensorSpeed speed = SensorSpeed.UI)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(handler);

        long leaseId = Interlocked.Increment(ref leaseSequence);

        lock (orientationSync)
        {
            if (!OrientationSensor.Default.IsSupported)
            {
                throw new NotSupportedException(
                    "The device orientation sensor is not supported.");
            }

            orientationHandlers.Add(leaseId, handler);

            try
            {
                if (!orientationSubscribed)
                {
                    OrientationSensor.Default.ReadingChanged +=
                        OnOrientationReadingChanged;
                    orientationSubscribed = true;
                }

                if (!OrientationSensor.Default.IsMonitoring)
                {
                    OrientationSensor.Default.Start(speed);
                    orientationStartedByManager = true;
                }
            }
            catch
            {
                orientationHandlers.Remove(leaseId);
                StopOrientationIfUnusedLocked();
                throw;
            }

            LogLease(
                "orientation",
                "ACQUIRED",
                owner,
                leaseId,
                orientationHandlers.Count,
                orientationStartedByManager);
        }

        return new IdempotentReleaseLease(
            () => ReleaseOrientation(leaseId, owner));
    }

    public static IDisposable AcquireAccelerometer(
        string owner,
        EventHandler<AccelerometerChangedEventArgs> handler,
        SensorSpeed speed = SensorSpeed.UI)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentNullException.ThrowIfNull(handler);

        long leaseId = Interlocked.Increment(ref leaseSequence);

        lock (accelerometerSync)
        {
            if (!Accelerometer.Default.IsSupported)
            {
                throw new NotSupportedException(
                    "The accelerometer is not supported.");
            }

            accelerometerHandlers.Add(leaseId, handler);

            try
            {
                if (!accelerometerSubscribed)
                {
                    Accelerometer.Default.ReadingChanged +=
                        OnAccelerometerReadingChanged;
                    accelerometerSubscribed = true;
                }

                if (!Accelerometer.Default.IsMonitoring)
                {
                    Accelerometer.Default.Start(speed);
                    accelerometerStartedByManager = true;
                }
            }
            catch
            {
                accelerometerHandlers.Remove(leaseId);
                StopAccelerometerIfUnusedLocked();
                throw;
            }

            LogLease(
                "accelerometer",
                "ACQUIRED",
                owner,
                leaseId,
                accelerometerHandlers.Count,
                accelerometerStartedByManager);
        }

        return new IdempotentReleaseLease(
            () => ReleaseAccelerometer(leaseId, owner));
    }

    private static void ReleaseOrientation(
        long leaseId,
        string owner)
    {
        lock (orientationSync)
        {
            if (!orientationHandlers.Remove(leaseId))
            {
                return;
            }

            StopOrientationIfUnusedLocked();

            LogLease(
                "orientation",
                "RELEASED",
                owner,
                leaseId,
                orientationHandlers.Count,
                orientationStartedByManager);
        }
    }

    private static void StopOrientationIfUnusedLocked()
    {
        if (orientationHandlers.Count != 0)
        {
            return;
        }

        if (orientationSubscribed)
        {
            try
            {
                OrientationSensor.Default.ReadingChanged -=
                    OnOrientationReadingChanged;
            }
            catch (Exception exception)
            {
                LogHandlerFailure(
                    "orientation unsubscribe",
                    exception);
            }

            orientationSubscribed = false;
        }

        if (orientationStartedByManager &&
            OrientationSensor.Default.IsMonitoring)
        {
            try
            {
                OrientationSensor.Default.Stop();
            }
            catch (Exception exception)
            {
                LogHandlerFailure(
                    "orientation stop",
                    exception);
            }
        }

        orientationStartedByManager = false;
    }

    private static void ReleaseAccelerometer(
        long leaseId,
        string owner)
    {
        lock (accelerometerSync)
        {
            if (!accelerometerHandlers.Remove(leaseId))
            {
                return;
            }

            StopAccelerometerIfUnusedLocked();

            LogLease(
                "accelerometer",
                "RELEASED",
                owner,
                leaseId,
                accelerometerHandlers.Count,
                accelerometerStartedByManager);
        }
    }

    private static void StopAccelerometerIfUnusedLocked()
    {
        if (accelerometerHandlers.Count != 0)
        {
            return;
        }

        if (accelerometerSubscribed)
        {
            try
            {
                Accelerometer.Default.ReadingChanged -=
                    OnAccelerometerReadingChanged;
            }
            catch (Exception exception)
            {
                LogHandlerFailure(
                    "accelerometer unsubscribe",
                    exception);
            }

            accelerometerSubscribed = false;
        }

        if (accelerometerStartedByManager &&
            Accelerometer.Default.IsMonitoring)
        {
            try
            {
                Accelerometer.Default.Stop();
            }
            catch (Exception exception)
            {
                LogHandlerFailure(
                    "accelerometer stop",
                    exception);
            }
        }

        accelerometerStartedByManager = false;
    }

    private static void OnOrientationReadingChanged(
        object? sender,
        OrientationSensorChangedEventArgs args)
    {
        EventHandler<OrientationSensorChangedEventArgs>[] handlers;

        lock (orientationSync)
        {
            handlers = orientationHandlers.Values.ToArray();
        }

        foreach (EventHandler<OrientationSensorChangedEventArgs> handler in handlers)
        {
            try
            {
                handler(sender, args);
            }
            catch (Exception exception)
            {
                LogHandlerFailure("orientation", exception);
            }
        }
    }

    private static void OnAccelerometerReadingChanged(
        object? sender,
        AccelerometerChangedEventArgs args)
    {
        EventHandler<AccelerometerChangedEventArgs>[] handlers;

        lock (accelerometerSync)
        {
            handlers = accelerometerHandlers.Values.ToArray();
        }

        foreach (EventHandler<AccelerometerChangedEventArgs> handler in handlers)
        {
            try
            {
                handler(sender, args);
            }
            catch (Exception exception)
            {
                LogHandlerFailure("accelerometer", exception);
            }
        }
    }

    private static void LogLease(
        string sensor,
        string action,
        string owner,
        long leaseId,
        int activeLeases,
        bool startedByManager)
    {
#if ANDROID
        Log.Info(
            LogTag,
            "ARCORE_SENSOR_LEASE " +
            $"sensor={sensor}; action={action}; owner='{owner}'; " +
            $"lease={leaseId}; activeLeases={activeLeases}; " +
            $"startedByManager={startedByManager}.");
#endif
    }

    private static void LogHandlerFailure(
        string sensor,
        Exception exception)
    {
#if ANDROID
        Log.Warn(
            LogTag,
            $"{sensor} lease callback failed: " +
            $"{exception.GetType().Name}: {exception.Message}");
#endif
    }

}
