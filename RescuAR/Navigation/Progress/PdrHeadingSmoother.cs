using System;
using System.Collections.Generic;

namespace RescuAR.Navigation.Progress;

/// <summary>
/// Estimates pedestrian direction from a short rolling ARCore motion window.
/// Camera-forward direction remains a secondary signal so one phone rotation
/// cannot immediately reject an otherwise route-consistent walking step.
/// </summary>
public sealed class PdrHeadingSmoother
{
    private const long SampleWindowMilliseconds =
        4000;

    private const long MaximumSampleGapMilliseconds =
        2500;

    private const int MaximumSamples =
        6;

    private const double MinimumMotionSampleMeters =
        0.08;

    private const double MaximumMotionSampleMeters =
        2.50;

    private const double MinimumAccumulatedMotionMeters =
        0.45;

    private const double MinimumResultantMotionMeters =
        0.30;

    private const double MinimumMotionCoherence =
        0.55;

    private const double WalkingVectorWeight =
        0.75;

    private const double CameraVectorWeight =
        1.0 -
        WalkingVectorWeight;

    private readonly List<MotionSample> samples =
        new();

    private readonly object sync =
        new();

    public void Reset()
    {
        lock (sync)
        {
            samples.Clear();
        }
    }

    public HeadingEstimate Evaluate(
        long timestampMilliseconds,
        double positionX,
        double positionZ,
        double cameraAzimuthDegrees,
        double routeAzimuthDegrees)
    {
        lock (sync)
        {
        if (!double.IsFinite(positionX) ||
            !double.IsFinite(positionZ) ||
            !double.IsFinite(cameraAzimuthDegrees) ||
            !double.IsFinite(routeAzimuthDegrees))
        {
            return HeadingEstimate.Unavailable(
                "PDR heading input is invalid.");
        }

        if (samples.Count >
            0)
        {
            MotionSample previous =
                samples[^1];

            long sampleGap =
                timestampMilliseconds -
                previous.TimestampMilliseconds;

            double displacement =
                GetDistance(
                    positionX -
                        previous.PositionX,
                    positionZ -
                        previous.PositionZ);

            if (sampleGap <=
                    0 ||
                sampleGap >
                    MaximumSampleGapMilliseconds ||
                displacement >
                    MaximumMotionSampleMeters)
            {
                Reset();
            }
        }

        samples.Add(
            new MotionSample(
                timestampMilliseconds,
                positionX,
                positionZ,
                Normalize360Degrees(
                    cameraAzimuthDegrees)));

        long oldestTimestamp =
            timestampMilliseconds -
            SampleWindowMilliseconds;

        while (samples.Count >
                   MaximumSamples ||
               (samples.Count >
                    1 &&
                samples[0].TimestampMilliseconds <
                    oldestTimestamp))
        {
            samples.RemoveAt(
                0);
        }

        (double cameraX,
         double cameraZ) =
            GetWeightedCameraVector();

        double smoothedCameraAzimuth =
            GetAzimuthDegrees(
                cameraX,
                cameraZ);

        bool walkingVectorAvailable =
            TryGetWalkingVector(
                out double walkingX,
                out double walkingZ,
                out double walkingAzimuth,
                out double motionCoherence,
                out double accumulatedMotionMeters);

        double fusedX =
            cameraX;

        double fusedZ =
            cameraZ;

        if (walkingVectorAvailable)
        {
            fusedX =
                walkingX *
                    WalkingVectorWeight +
                cameraX *
                    CameraVectorWeight;

            fusedZ =
                walkingZ *
                    WalkingVectorWeight +
                cameraZ *
                    CameraVectorWeight;
        }

        double fusedMagnitude =
            GetDistance(
                fusedX,
                fusedZ);

        if (!double.IsFinite(
                fusedMagnitude) ||
            fusedMagnitude <
                0.001)
        {
            return HeadingEstimate.Unavailable(
                "Smoothed PDR direction is indeterminate.");
        }

        double fusedAzimuth =
            GetAzimuthDegrees(
                fusedX,
                fusedZ);

        double headingError =
            AbsoluteHeadingDifferenceDegrees(
                fusedAzimuth,
                routeAzimuthDegrees);

        return new HeadingEstimate(
            true,
            fusedAzimuth,
            smoothedCameraAzimuth,
            walkingVectorAvailable
                ? walkingAzimuth
                : double.NaN,
            Normalize360Degrees(
                routeAzimuthDegrees),
            headingError,
            walkingVectorAvailable,
            motionCoherence,
            accumulatedMotionMeters,
            samples.Count,
            walkingVectorAvailable
                ? "rolling ARCore walking vector blended with smoothed camera direction"
                : "smoothed camera direction while walking motion accumulates");
        }
    }

    private (double X, double Z) GetWeightedCameraVector()
    {
        double sumX =
            0.0;

        double sumZ =
            0.0;

        double totalWeight =
            0.0;

        for (int i = 0;
             i <
                samples.Count;
             i++)
        {
            double weight =
                i +
                1.0;

            double radians =
                DegreesToRadians(
                    samples[i].CameraAzimuthDegrees);

            sumX +=
                Math.Sin(radians) *
                weight;

            sumZ +=
                Math.Cos(radians) *
                weight;

            totalWeight +=
                weight;
        }

        if (totalWeight <=
            0.0)
        {
            return (0.0, 1.0);
        }

        double meanX =
            sumX /
            totalWeight;

        double meanZ =
            sumZ /
            totalWeight;

        if (GetDistance(
                meanX,
                meanZ) <
            0.15)
        {
            double latestRadians =
                DegreesToRadians(
                    samples[^1].CameraAzimuthDegrees);

            return (
                Math.Sin(
                    latestRadians),
                Math.Cos(
                    latestRadians));
        }

        return NormalizeVector(
            meanX,
            meanZ);
    }

    private bool TryGetWalkingVector(
        out double walkingX,
        out double walkingZ,
        out double walkingAzimuthDegrees,
        out double motionCoherence,
        out double accumulatedMotionMeters)
    {
        double weightedX =
            0.0;

        double weightedZ =
            0.0;

        double resultantX =
            0.0;

        double resultantZ =
            0.0;

        double totalWeight =
            0.0;

        accumulatedMotionMeters =
            0.0;

        for (int i = 1;
             i <
                samples.Count;
             i++)
        {
            double deltaX =
                samples[i].PositionX -
                samples[i - 1].PositionX;

            double deltaZ =
                samples[i].PositionZ -
                samples[i - 1].PositionZ;

            double distance =
                GetDistance(
                    deltaX,
                    deltaZ);

            if (!double.IsFinite(
                    distance) ||
                distance <
                    MinimumMotionSampleMeters ||
                distance >
                    MaximumMotionSampleMeters)
            {
                continue;
            }

            double recencyWeight =
                i;

            weightedX +=
                deltaX /
                    distance *
                distance *
                recencyWeight;

            weightedZ +=
                deltaZ /
                    distance *
                distance *
                recencyWeight;

            resultantX +=
                deltaX;

            resultantZ +=
                deltaZ;

            totalWeight +=
                recencyWeight;

            accumulatedMotionMeters +=
                distance;
        }

        double weightedMagnitude =
            GetDistance(
                weightedX,
                weightedZ);

        double resultantMotionMeters =
            GetDistance(
                resultantX,
                resultantZ);

        double weightedPathLength =
            0.0;

        for (int i = 1;
             i <
                samples.Count;
             i++)
        {
            double distance =
                GetDistance(
                    samples[i].PositionX -
                        samples[i - 1].PositionX,
                    samples[i].PositionZ -
                        samples[i - 1].PositionZ);

            if (double.IsFinite(distance) &&
                distance >=
                    MinimumMotionSampleMeters &&
                distance <=
                    MaximumMotionSampleMeters)
            {
                weightedPathLength +=
                    distance *
                    i;
            }
        }

        motionCoherence =
            weightedPathLength >
                0.0
                ? Math.Clamp(
                    weightedMagnitude /
                        weightedPathLength,
                    0.0,
                    1.0)
                : 0.0;

        bool available =
            accumulatedMotionMeters >=
                MinimumAccumulatedMotionMeters &&
            resultantMotionMeters >=
                MinimumResultantMotionMeters &&
            totalWeight >
                0.0 &&
            motionCoherence >=
                MinimumMotionCoherence;

        if (!available)
        {
            walkingX =
                0.0;

            walkingZ =
                0.0;

            walkingAzimuthDegrees =
                double.NaN;

            return false;
        }

        (walkingX,
         walkingZ) =
            NormalizeVector(
                weightedX,
                weightedZ);

        walkingAzimuthDegrees =
            GetAzimuthDegrees(
                walkingX,
                walkingZ);

        return true;
    }

    private static (double X, double Z) NormalizeVector(
        double x,
        double z)
    {
        double magnitude =
            GetDistance(
                x,
                z);

        if (!double.IsFinite(magnitude) ||
            magnitude <
                0.001)
        {
            return (0.0, 1.0);
        }

        return (
            x /
                magnitude,
            z /
                magnitude);
    }

    private static double GetAzimuthDegrees(
        double x,
        double z)
    {
        return Normalize360Degrees(
            RadiansToDegrees(
                Math.Atan2(
                    x,
                    z)));
    }

    private static double GetDistance(
        double x,
        double z)
    {
        return Math.Sqrt(
            x *
                x +
            z *
                z);
    }

    private static double AbsoluteHeadingDifferenceDegrees(
        double first,
        double second)
    {
        double difference =
            Normalize360Degrees(
                first -
                second);

        return difference >
            180.0
                ? 360.0 -
                    difference
                : difference;
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

    private static double DegreesToRadians(
        double degrees)
    {
        return degrees *
            Math.PI /
            180.0;
    }

    private static double RadiansToDegrees(
        double radians)
    {
        return radians *
            180.0 /
            Math.PI;
    }

    private readonly record struct MotionSample(
        long TimestampMilliseconds,
        double PositionX,
        double PositionZ,
        double CameraAzimuthDegrees);

    public readonly record struct HeadingEstimate(
        bool IsAvailable,
        double FusedAzimuthDegrees,
        double SmoothedCameraAzimuthDegrees,
        double WalkingAzimuthDegrees,
        double RouteAzimuthDegrees,
        double HeadingErrorDegrees,
        bool UsedWalkingVector,
        double MotionCoherence,
        double AccumulatedMotionMeters,
        int SampleCount,
        string Reason)
    {
        public static HeadingEstimate Unavailable(
            string reason)
        {
            return new HeadingEstimate(
                false,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                false,
                0.0,
                0.0,
                0,
                reason);
        }
    }
}
