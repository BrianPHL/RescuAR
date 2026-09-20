#if ANDROID
using Android.Hardware;
using Android.Util;
#endif

using System.Numerics;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using RescuAR.AR;
using RescuAR.Navigation.Models;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>
/// Establishes the horizontal yaw relationship between geographic map axes
/// and the retained ARCore world frame.
///
/// The device orientation sensor supplies an Earth-referenced quaternion.
/// From it, this service derives the REAR CAMERA optical-axis bearing instead
/// of relying on a simple 2D compass heading. That is important while the
/// phone is held upright for AR.
///
/// Calibration equation:
///
///     mapToArYaw = ARCoreCameraAzimuth - GeographicCameraBearing
///
/// ArRouteAlignment then rotates East/North route coordinates into AR X/Z
/// using that constant yaw.
///
/// Magnetic orientation provides the initial estimate. CameraPage may later
/// replace it with a movement-validated yaw; raw magnetic sensor updates are
/// never applied directly to the rendered route.
/// </summary>
public sealed class ArHeadingAlignmentService : IDisposable
{
    private const string LogTag =
        "RescuAR-Heading";

    private static readonly TimeSpan SensorReadingMaxAge =
        TimeSpan.FromSeconds(
            2);

    private static readonly TimeSpan CaptureTimeout =
        TimeSpan.FromSeconds(
            4);

    private const int MinimumStableSamples =
        6;

    private const int MaximumSamples =
        24;

    /*
     * If either the Earth-referenced or AR camera optical axis points almost
     * straight up/down, its projection onto the horizontal plane becomes too
     * small for a reliable heading.
     */
    private const float MinimumHorizontalMagnitude =
        0.20f;

    /*
     * A short calibration is accepted early once the yaw samples agree
     * reasonably well. If they do not, CaptureAsync keeps sampling until the
     * timeout and returns the circular mean with IsStable=false.
     */
    private const double StableMaxDeviationDegrees =
        8.0;

    private const double StableMinimumCircularConcentration =
        0.98;

    private const string HeadingCoordinateConvention =
        "device(+X right,+Y screen-top,+Z display-out); " +
        "rear-camera=-Z; earth(+X east,+Y north,+Z up); " +
        "ARCore camera=-Z; AR azimuth atan2(+X,+Z)";

    private readonly object sync =
        new();

    private Quaternion latestDeviceOrientation =
        Quaternion.Identity;

    private DateTimeOffset latestOrientationTimestamp =
        DateTimeOffset.MinValue;

    private bool hasOrientationReading;
    private bool started;
    private bool disposed;
    private IDisposable? orientationLease;

    /*
     * The map<->AR relationship belongs to the retained ARCore Session, not
     * to an individual route request or destination. Keep it process-static
     * so it also survives a CameraPage recreation while the same ARCore
     * Session is retained. CameraPage explicitly clears it only when a truly
     * new ARCore Session is created.
     */
    private static readonly object sessionCalibrationSync =
        new();

    private static HeadingAlignmentResult? sessionCalibration;

    public bool IsSupported =>
        OrientationSensor.Default.IsSupported;

    public bool IsStarted
    {
        get
        {
            lock (sync)
            {
                return started;
            }
        }
    }

    public HeadingAlignmentResult? LastResult
    {
        get
        {
            lock (sessionCalibrationSync)
            {
                return sessionCalibration;
            }
        }
    }

    public bool HasSessionCalibration
    {
        get
        {
            lock (sessionCalibrationSync)
            {
                return sessionCalibration.HasValue;
            }
        }
    }

    /// <summary>
    /// Clears the cached map-to-AR yaw because a genuinely new ARCore world
    /// frame is about to be created. Do not call this for Camera-tab pause /
    /// resume or for a destination change.
    /// </summary>
    public void ResetSessionCalibration(
        string reason)
    {
        lock (sessionCalibrationSync)
        {
            sessionCalibration =
                null;
        }

#if ANDROID
        Log.Debug(
            LogTag,
            "Session heading calibration cleared. " +
            $"Reason={reason}");
#endif
    }

    public void Start()
    {
        ThrowIfDisposed();

        lock (sync)
        {
            if (started)
            {
                return;
            }
        }

        if (!OrientationSensor.Default.IsSupported)
        {
#if ANDROID
            Log.Warn(
                LogTag,
                "Device orientation sensor is not supported. " +
                "Heading alignment will fall back to yaw=0.");
#endif

            return;
        }

        try
        {
            orientationLease =
                SharedMotionSensorLeaseManager.AcquireOrientation(
                    nameof(ArHeadingAlignmentService),
                    OnOrientationReadingChanged,
                    SensorSpeed.UI);

            lock (sync)
            {
                started =
                    true;
            }

#if ANDROID
            Log.Debug(
                LogTag,
                "Earth-referenced orientation sensor started.");
#endif
        }
        catch (Exception exception)
        {
            Interlocked.Exchange(
                ref orientationLease,
                null)?.Dispose();

#if ANDROID
            Log.Error(
                LogTag,
                $"Failed to start orientation sensor: {exception}");
#endif
        }
    }

    public void Stop()
    {
        bool wasStarted;

        lock (sync)
        {
            wasStarted =
                started;

            started =
                false;

            hasOrientationReading =
                false;

            latestOrientationTimestamp =
                DateTimeOffset.MinValue;
        }

        if (!wasStarted)
        {
            return;
        }

        Interlocked.Exchange(
            ref orientationLease,
            null)?.Dispose();

#if ANDROID
        Log.Debug(
            LogTag,
            "Earth-referenced orientation sensor stopped.");
#endif
    }

    /// <summary>
    /// Captures a short set of map-to-AR yaw samples and circular-averages
    /// them. This method waits for both a fresh orientation reading and a
    /// TRACKING ARCore pose.
    /// </summary>
    public async Task<HeadingAlignmentResult?> CaptureAsync(
        GeoCoordinate location,
        double? altitudeMeters,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!location.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(location));
        }

        if (!IsStarted)
        {
            Start();
        }

        if (!IsSupported)
        {
            return null;
        }

        HeadingAlignmentResult? cachedCalibration;

        lock (sessionCalibrationSync)
        {
            cachedCalibration =
                sessionCalibration;
        }

        if (cachedCalibration.HasValue)
        {
#if ANDROID
            Log.Debug(
                LogTag,
                "Reusing retained ARCore-session heading alignment: " +
                $"mapToArYaw={cachedCalibration.Value.MapToArYawDegrees:F2} deg, " +
                $"stable={cachedCalibration.Value.IsStable}, " +
                $"originalSamples={cachedCalibration.Value.SampleCount}, " +
                $"originalSpatialVersion={cachedCalibration.Value.SpatialVersion}");
#endif

            return cachedCalibration;
        }

#if ANDROID
        Log.Debug(
            LogTag,
            "No retained session heading exists. Starting initial calibration. " +
            "Hold the phone reasonably steady for about one second.");
#endif

        DateTimeOffset deadline =
            DateTimeOffset.UtcNow +
            CaptureTimeout;

        List<HeadingAlignmentResult> samples =
            new(
                MaximumSamples);

        while (DateTimeOffset.UtcNow <
               deadline &&
               samples.Count <
               MaximumSamples)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (TryCreateSample(
                    location,
                    altitudeMeters,
                    out HeadingAlignmentResult sample,
                    out string? unavailableReason))
            {
                samples.Add(
                    sample);

                if (samples.Count >=
                    MinimumStableSamples)
                {
                    double meanYaw =
                        CircularMeanDegrees(
                            samples.Select(
                                item =>
                                    item.MapToArYawDegrees));

                    double maxDeviation =
                        samples.Max(
                            item =>
                                Math.Abs(
                                    NormalizeSignedDegrees(
                                        item.MapToArYawDegrees -
                                        meanYaw)));

                    double circularConcentration =
                        CircularConcentration(
                            samples.Select(
                                item =>
                                    item.MapToArYawDegrees));

                    if (maxDeviation <=
                            StableMaxDeviationDegrees &&
                        circularConcentration >=
                            StableMinimumCircularConcentration)
                    {
                        HeadingAlignmentResult locked =
                            sample with
                            {
                                MapToArYawDegrees =
                                    meanYaw,
                                SampleCount =
                                    samples.Count,
                                MaxSampleDeviationDegrees =
                                    maxDeviation,
                                CircularConcentration =
                                    circularConcentration,
                                IsStable =
                                    true
                            };

                        StoreAndLogResult(
                            locked);

                        return locked;
                    }
                }
            }
            else if (samples.Count ==
                     0)
            {
#if ANDROID
                /*
                 * Do not spam Logcat every 100 ms. A first-state message is
                 * enough; the final failure below contains the outcome.
                 */
                if (!string.IsNullOrWhiteSpace(
                        unavailableReason))
                {
                    Log.Debug(
                        LogTag,
                        $"Heading calibration waiting: {unavailableReason}");
                }
#endif
            }

            await Task.Delay(
                100,
                cancellationToken);
        }

        if (samples.Count >
            0)
        {
            HeadingAlignmentResult latest =
                samples[^1];

            double meanYaw =
                CircularMeanDegrees(
                    samples.Select(
                        item =>
                            item.MapToArYawDegrees));

            double maxDeviation =
                samples.Max(
                    item =>
                        Math.Abs(
                            NormalizeSignedDegrees(
                                item.MapToArYawDegrees -
                                    meanYaw)));

            double circularConcentration =
                CircularConcentration(
                    samples.Select(
                        item =>
                            item.MapToArYawDegrees));

            HeadingAlignmentResult bestEffort =
                latest with
                {
                    MapToArYawDegrees =
                        meanYaw,
                    SampleCount =
                        samples.Count,
                    MaxSampleDeviationDegrees =
                        maxDeviation,
                    CircularConcentration =
                        circularConcentration,
                    IsStable =
                        false
                };

#if ANDROID
            Log.Warn(
                LogTag,
                "Heading calibration timed out before reaching the stability " +
                "threshold; using the circular mean without locking it for the session. " +
                $"maxDeviation={maxDeviation:F2}deg, " +
                $"circularConcentration={circularConcentration:F3}. " +
                "Magnetic interference or device movement may be present.");
#endif

            return bestEffort;
        }

#if ANDROID
        Log.Error(
            LogTag,
            "Heading calibration failed: no valid synchronized orientation + " +
            "ARCore tracking samples were available.");
#endif

        return null;
    }

    /// <summary>
    /// Stores a yaw correction only after CameraPage has validated sustained
    /// walking direction against GPS, route geometry, and ARCore movement.
    /// </summary>
    public HeadingAlignmentResult ApplyMovementValidatedYaw(
        double correctedMapToArYawDegrees,
        int confirmationCount,
        double residualAlignmentErrorDegrees,
        DateTimeOffset timestampUtc)
    {
        ThrowIfDisposed();

        if (!double.IsFinite(
                correctedMapToArYawDegrees))
        {
            throw new ArgumentOutOfRangeException(
                nameof(correctedMapToArYawDegrees));
        }

        HeadingAlignmentResult corrected;

        lock (sessionCalibrationSync)
        {
            HeadingAlignmentResult previous =
                sessionCalibration ??
                new HeadingAlignmentResult(
                    true,
                    correctedMapToArYawDegrees,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    ARCameraPoseBridge.CurrentFrame.Version,
                    0,
                    double.NaN,
                    false,
                    timestampUtc,
                    GetDisplayRotationName(),
                    HeadingCoordinateConvention,
                    0.0);

            corrected =
                previous with
                {
                    IsAvailable =
                        true,
                    MapToArYawDegrees =
                        NormalizeSignedDegrees(
                            correctedMapToArYawDegrees),
                    SpatialVersion =
                        ARCameraPoseBridge.CurrentFrame.Version,
                    SampleCount =
                        Math.Max(
                            previous.SampleCount,
                            confirmationCount),
                    MaxSampleDeviationDegrees =
                        Math.Abs(
                            residualAlignmentErrorDegrees),
                    IsStable =
                        Math.Abs(
                            residualAlignmentErrorDegrees) <=
                            StableMaxDeviationDegrees,
                    Timestamp =
                        timestampUtc
                };

            sessionCalibration =
                corrected;
        }

#if ANDROID
        Log.Warn(
            LogTag,
            "Heading alignment UPDATED from sustained movement: " +
            $"mapToArYaw={corrected.MapToArYawDegrees:F2} deg, " +
            $"confirmations={confirmationCount}, " +
            $"residualError={residualAlignmentErrorDegrees:F2} deg, " +
            $"stable={corrected.IsStable}, " +
            $"spatialVersion={corrected.SpatialVersion}.");
#endif

        return corrected;
    }

    private void OnOrientationReadingChanged(
        object? sender,
        OrientationSensorChangedEventArgs e)
    {
        Quaternion orientation =
            e.Reading.Orientation;

        float lengthSquared =
            orientation.LengthSquared();

        if (!float.IsFinite(
                lengthSquared) ||
            lengthSquared <
                0.0001f)
        {
            return;
        }

        orientation =
            Quaternion.Normalize(
                orientation);

        lock (sync)
        {
            latestDeviceOrientation =
                orientation;

            latestOrientationTimestamp =
                DateTimeOffset.UtcNow;

            hasOrientationReading =
                true;
        }
    }

    private bool TryCreateSample(
        GeoCoordinate location,
        double? altitudeMeters,
        out HeadingAlignmentResult result,
        out string? unavailableReason)
    {
        result =
            default;

        unavailableReason =
            null;

        Quaternion deviceOrientation;
        DateTimeOffset orientationTimestamp;
        bool hasReading;

        lock (sync)
        {
            deviceOrientation =
                latestDeviceOrientation;

            orientationTimestamp =
                latestOrientationTimestamp;

            hasReading =
                hasOrientationReading;
        }

        if (!hasReading)
        {
            unavailableReason =
                "orientation sensor has not produced a reading yet";

            return false;
        }

        if (DateTimeOffset.UtcNow -
            orientationTimestamp >
            SensorReadingMaxAge)
        {
            unavailableReason =
                "orientation sensor reading is stale";

            return false;
        }

        /*
         * Device coordinates:
         * +X = screen right
         * +Y = screen top
         * +Z = out through the front/display side
         *
         * Therefore the rear camera optical axis points approximately -Z.
         *
         * The MAUI orientation quaternion is Earth-referenced. For Android,
         * the corresponding world basis is East/North/Up.
         */
        Vector3 earthRearCameraForward =
            Vector3.Transform(
                new Vector3(
                    0.0f,
                    0.0f,
                    -1.0f),
                deviceOrientation);

        float earthHorizontalMagnitude =
            MathF.Sqrt(
                earthRearCameraForward.X *
                earthRearCameraForward.X +
                earthRearCameraForward.Y *
                earthRearCameraForward.Y);

        if (!float.IsFinite(
                earthHorizontalMagnitude) ||
            earthHorizontalMagnitude <
                MinimumHorizontalMagnitude)
        {
            unavailableReason =
                "rear camera is too close to vertical for a stable " +
                "geographic bearing";

            return false;
        }

        double magneticCameraHeadingDegrees =
            Normalize360Degrees(
                RadiansToDegrees(
                    Math.Atan2(
                        earthRearCameraForward.X,
                        earthRearCameraForward.Y)));

        double declinationDegrees =
            GetMagneticDeclinationDegrees(
                location,
                altitudeMeters);

        double trueCameraHeadingDegrees =
            Normalize360Degrees(
                magneticCameraHeadingDegrees +
                declinationDegrees);

        ARCameraPoseBridge.SpatialSnapshot spatial =
            ARCameraPoseBridge.CurrentFrame;

        if (!spatial.IsTracking ||
            !spatial.Pose.IsTracking)
        {
            unavailableReason =
                $"ARCore is not TRACKING " +
                $"({spatial.TrackingFailureReason})";

            return false;
        }

        ARCameraPoseBridge.PoseSnapshot arPose =
            spatial.Pose;

        Quaternion arCameraRotation =
            new(
                arPose.RotationX,
                arPose.RotationY,
                arPose.RotationZ,
                arPose.RotationW);

        float arLengthSquared =
            arCameraRotation.LengthSquared();

        if (!float.IsFinite(
                arLengthSquared) ||
            arLengthSquared <
                0.0001f)
        {
            unavailableReason =
                "ARCore camera quaternion is invalid";

            return false;
        }

        arCameraRotation =
            Quaternion.Normalize(
                arCameraRotation);

        /*
         * ARCore's camera optical axis is local -Z.
         */
        Vector3 arCameraForward =
            Vector3.Transform(
                new Vector3(
                    0.0f,
                    0.0f,
                    -1.0f),
                arCameraRotation);

        float arHorizontalMagnitude =
            MathF.Sqrt(
                arCameraForward.X *
                arCameraForward.X +
                arCameraForward.Z *
                arCameraForward.Z);

        if (!float.IsFinite(
                arHorizontalMagnitude) ||
            arHorizontalMagnitude <
                MinimumHorizontalMagnitude)
        {
            unavailableReason =
                "ARCore camera forward vector is too close to vertical";

            return false;
        }

        /*
         * AR horizontal azimuth convention matches ArRouteAlignment:
         *
         *     0 deg   = +Z
         *     90 deg  = +X
         *     180 deg = -Z
         *     270 deg = -X
         */
        double arCameraAzimuthDegrees =
            Normalize360Degrees(
                RadiansToDegrees(
                    Math.Atan2(
                        arCameraForward.X,
                        arCameraForward.Z)));

        double mapToArYawDegrees =
            NormalizeSignedDegrees(
                arCameraAzimuthDegrees -
                trueCameraHeadingDegrees);

        result =
            new HeadingAlignmentResult(
                true,
                mapToArYawDegrees,
                magneticCameraHeadingDegrees,
                trueCameraHeadingDegrees,
                declinationDegrees,
                arCameraAzimuthDegrees,
                spatial.Version,
                1,
                0.0,
                false,
                DateTimeOffset.UtcNow,
                GetDisplayRotationName(),
                HeadingCoordinateConvention,
                1.0);

        return true;
    }

    private static double GetMagneticDeclinationDegrees(
        GeoCoordinate location,
        double? altitudeMeters)
    {
#if ANDROID
        try
        {
            GeomagneticField field =
                new(
                    (float)location.Latitude,
                    (float)location.Longitude,
                    (float)(altitudeMeters ??
                            0.0),
                    DateTimeOffset.UtcNow
                        .ToUnixTimeMilliseconds());

            return field.Declination;
        }
        catch (Exception exception)
        {
            Log.Warn(
                LogTag,
                "Geomagnetic declination lookup failed; using magnetic " +
                $"north directly. {exception.GetType().Name}: " +
                $"{exception.Message}");

            return 0.0;
        }
#else
        return 0.0;
#endif
    }

    private void StoreAndLogResult(
        HeadingAlignmentResult result)
    {
        lock (sessionCalibrationSync)
        {
            sessionCalibration =
                result;
        }

#if ANDROID
        Log.Debug(
            LogTag,
            "Heading alignment LOCKED: " +
            $"mapToArYaw={result.MapToArYawDegrees:F2} deg, " +
            $"rearCameraMagnetic={result.MagneticCameraHeadingDegrees:F2} deg, " +
            $"rearCameraTrue={result.TrueCameraHeadingDegrees:F2} deg, " +
            $"declination={result.MagneticDeclinationDegrees:F2} deg, " +
            $"arCameraAzimuth={result.ArCameraAzimuthDegrees:F2} deg, " +
            $"samples={result.SampleCount}, " +
            $"maxDeviation={result.MaxSampleDeviationDegrees:F2} deg, " +
            $"circularConcentration={result.CircularConcentration:F3}, " +
            $"displayRotation={result.DisplayRotation}, " +
            $"coordinateConvention='{result.CoordinateConvention}', " +
            $"stable={result.IsStable}, " +
            $"spatialVersion={result.SpatialVersion}");
#endif
    }

    private static double CircularMeanDegrees(
        IEnumerable<double> values)
    {
        double sinSum =
            0.0;

        double cosSum =
            0.0;

        int count =
            0;

        foreach (double value in
                 values)
        {
            double radians =
                value *
                Math.PI /
                180.0;

            sinSum +=
                Math.Sin(
                    radians);

            cosSum +=
                Math.Cos(
                    radians);

            count++;
        }

        if (count ==
            0)
        {
            return 0.0;
        }

        return NormalizeSignedDegrees(
            RadiansToDegrees(
                Math.Atan2(
                    sinSum /
                    count,
                    cosSum /
                    count)));
    }

    private static string GetDisplayRotationName()
    {
        try
        {
            return DeviceDisplay.MainDisplayInfo.Rotation.ToString();
        }
        catch
        {
            return "Unavailable";
        }
    }

    private static double CircularConcentration(
        IEnumerable<double> values)
    {
        double sinSum = 0.0;
        double cosSum = 0.0;
        int count = 0;

        foreach (double value in values)
        {
            double radians = value * Math.PI / 180.0;
            sinSum += Math.Sin(radians);
            cosSum += Math.Cos(radians);
            count++;
        }

        if (count == 0)
        {
            return 0.0;
        }

        return Math.Clamp(
            Math.Sqrt(sinSum * sinSum + cosSum * cosSum) / count,
            0.0,
            1.0);
    }

    private static double Normalize360Degrees(
        double degrees)
    {
        double normalized =
            degrees %
            360.0;

        if (normalized <
            0.0)
        {
            normalized +=
                360.0;
        }

        return normalized;
    }

    private static double NormalizeSignedDegrees(
        double degrees)
    {
        double normalized =
            Normalize360Degrees(
                degrees);

        if (normalized >
            180.0)
        {
            normalized -=
                360.0;
        }

        return normalized;
    }

    private static double RadiansToDegrees(
        double radians)
    {
        return radians *
            180.0 /
            Math.PI;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(
            disposed,
            this);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        Stop();

        disposed =
            true;
    }

    public readonly record struct HeadingAlignmentResult(
        bool IsAvailable,
        double MapToArYawDegrees,
        double MagneticCameraHeadingDegrees,
        double TrueCameraHeadingDegrees,
        double MagneticDeclinationDegrees,
        double ArCameraAzimuthDegrees,
        long SpatialVersion,
        int SampleCount,
        double MaxSampleDeviationDegrees,
        bool IsStable,
        DateTimeOffset Timestamp,
        string DisplayRotation,
        string CoordinateConvention,
        double CircularConcentration);
}
