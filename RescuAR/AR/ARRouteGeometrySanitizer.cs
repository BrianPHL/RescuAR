using RescuAR.Navigation.Projection;
using System;
using System.Collections.Generic;

namespace RescuAR.AR;

/// <summary>
/// Produces bounded local route geometry before pooled segment rendering.
/// Tiny spans are removed, sharp corners are beveled, and long spans are
/// subdivided so one raw route edge cannot create an oversized visual wedge.
/// When the detailed geometry exceeds the renderer budget, the complete
/// window is resampled by both travelled distance and local curvature. The
/// first and final points are always retained, so capacity pressure can reduce
/// detail but can never silently cut off the remaining route.
/// </summary>
public static class ARRouteGeometrySanitizer
{
    private const float MinimumPointSpacingMeters =
        0.20f;

    private const float MaximumRenderedSegmentLengthMeters =
        4.0f;

    private const double CornerBevelThresholdDegrees =
        35.0;

    private const float MaximumCornerTrimMeters =
        0.45f;

    private const float CornerTrimFraction =
        0.25f;

    private const float MinimumCornerTrimMeters =
        0.08f;

    private const double CurvatureSamplingWeight =
        1.75;

    public static GeometryPreparationResult Prepare(
        IReadOnlyList<ArHorizontalRoutePoint> source,
        int maximumPoints)
    {
        ArgumentNullException.ThrowIfNull(
            source);

        if (maximumPoints <
            2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPoints));
        }

        List<ArHorizontalRoutePoint> cleaned =
            new(source.Count);

        int removedPoints =
            0;

        bool sourceStartValid =
            source.Count > 0 &&
            IsFinite(
                source[0]);

        bool sourceFinalValid =
            source.Count > 0 &&
            IsFinite(
                source[^1]);

        for (int i = 0;
             i < source.Count;
             i++)
        {
            ArHorizontalRoutePoint point =
                source[i];

            if (!IsFinite(
                    point))
            {
                removedPoints++;

                continue;
            }

            if (cleaned.Count >
                    0 &&
                Distance(
                    cleaned[^1],
                    point) <
                    MinimumPointSpacingMeters)
            {
                removedPoints++;

                /*
                 * Keep the source endpoint authoritative. Replacing the last
                 * retained point avoids manufacturing a tiny final segment
                 * while ensuring a dense tail cannot discard the end of the
                 * visible route window.
                 */
                if (i ==
                    source.Count -
                        1)
                {
                    cleaned[^1] =
                        point;
                }

                continue;
            }

            cleaned.Add(
                point);
        }

        if (cleaned.Count <
            2)
        {
            return new GeometryPreparationResult(
                cleaned,
                source.Count,
                cleaned.Count,
                removedPoints,
                0,
                0,
                false,
                false,
                false,
                PolylineLength(cleaned),
                PolylineLength(cleaned),
                MaximumSegmentLength(cleaned));
        }

        float sourceLengthMeters =
            PolylineLength(cleaned);

        List<ArHorizontalRoutePoint> beveled =
            new(
                cleaned.Count *
                2);

        beveled.Add(
            cleaned[0]);

        int beveledCorners =
            0;

        for (int i = 1;
             i <
                cleaned.Count -
                    1;
             i++)
        {
            ArHorizontalRoutePoint previous =
                cleaned[i - 1];

            ArHorizontalRoutePoint corner =
                cleaned[i];

            ArHorizontalRoutePoint next =
                cleaned[i + 1];

            float incomingLength =
                Distance(
                    previous,
                    corner);

            float outgoingLength =
                Distance(
                    corner,
                    next);

            double turnDegrees =
                GetTurnAngleDegrees(
                    previous,
                    corner,
                    next);

            float trimMeters =
                MathF.Min(
                    MaximumCornerTrimMeters,
                    MathF.Min(
                        incomingLength,
                        outgoingLength) *
                    CornerTrimFraction);

            if (turnDegrees <
                    CornerBevelThresholdDegrees ||
                trimMeters <
                    MinimumCornerTrimMeters)
            {
                AddIfSeparated(
                    beveled,
                    corner);

                continue;
            }

            ArHorizontalRoutePoint beforeCorner =
                InterpolateByDistance(
                    corner,
                    previous,
                    trimMeters,
                    incomingLength);

            ArHorizontalRoutePoint afterCorner =
                InterpolateByDistance(
                    corner,
                    next,
                    trimMeters,
                    outgoingLength);

            AddIfSeparated(
                beveled,
                beforeCorner);

            AddIfSeparated(
                beveled,
                afterCorner);

            beveledCorners++;
        }

        AddIfSeparated(
            beveled,
            cleaned[^1]);

        List<ArHorizontalRoutePoint> detailed =
            new(beveled.Count);

        detailed.Add(
            beveled[0]);

        int insertedSubdivisionPoints =
            0;

        for (int i = 0;
             i <
                beveled.Count -
                    1;
             i++)
        {
            ArHorizontalRoutePoint start =
                beveled[i];

            ArHorizontalRoutePoint end =
                beveled[i + 1];

            float length =
                Distance(
                    start,
                    end);

            int partCount =
                Math.Max(
                    1,
                    (int)Math.Ceiling(
                        length /
                        MaximumRenderedSegmentLengthMeters));

            for (int part = 1;
                 part <=
                    partCount;
                 part++)
            {
                double amount =
                    (double)part /
                    partCount;

                detailed.Add(
                    Interpolate(
                        start,
                        end,
                        amount));

                if (part <
                    partCount)
                {
                    insertedSubdivisionPoints++;
                }
            }
        }

        bool capacityLimited =
            detailed.Count >
                maximumPoints;

        IReadOnlyList<ArHorizontalRoutePoint> prepared =
            capacityLimited
                ? ResampleByDistanceAndCurvature(
                    detailed,
                    maximumPoints)
                : detailed;

        bool firstPointPreserved =
            sourceStartValid &&
            prepared.Count > 0 &&
            PointsEqual(
                prepared[0],
                source[0]);

        bool finalPointPreserved =
            sourceFinalValid &&
            prepared.Count > 0 &&
            PointsEqual(
                prepared[^1],
                source[^1]);

        return new GeometryPreparationResult(
            prepared,
            source.Count,
            detailed.Count,
            removedPoints,
            beveledCorners,
            insertedSubdivisionPoints,
            capacityLimited,
            firstPointPreserved,
            finalPointPreserved,
            sourceLengthMeters,
            PolylineLength(prepared),
            MaximumSegmentLength(prepared));
    }

    private static IReadOnlyList<ArHorizontalRoutePoint>
        ResampleByDistanceAndCurvature(
            IReadOnlyList<ArHorizontalRoutePoint> source,
            int maximumPoints)
    {
        if (source.Count <=
            maximumPoints)
        {
            return source;
        }

        double[] turnStrength =
            new double[source.Count];

        for (int i = 1;
             i < source.Count -
                    1;
             i++)
        {
            turnStrength[i] =
                Math.Clamp(
                    GetTurnAngleDegrees(
                        source[i -
                            1],
                        source[i],
                        source[i +
                            1]) /
                        90.0,
                    0.0,
                    1.0);
        }

        double[] cumulativeWeight =
            new double[source.Count];

        for (int i = 0;
             i < source.Count -
                    1;
             i++)
        {
            double edgeLength =
                Distance(
                    source[i],
                    source[i +
                        1]);

            double curvature =
                Math.Max(
                    turnStrength[i],
                    turnStrength[i +
                        1]);

            double edgeWeight =
                edgeLength *
                (1.0 +
                 CurvatureSamplingWeight *
                    curvature);

            cumulativeWeight[i +
                1] =
                cumulativeWeight[i] +
                edgeWeight;
        }

        double totalWeight =
            cumulativeWeight[^1];

        if (!double.IsFinite(
                totalWeight) ||
            totalWeight <=
                0.0)
        {
            return
            [
                source[0],
                source[^1]
            ];
        }

        List<ArHorizontalRoutePoint> resampled =
            new(maximumPoints)
            {
                source[0]
            };

        int edgeIndex =
            0;

        for (int sampleIndex = 1;
             sampleIndex <
                maximumPoints -
                    1;
             sampleIndex++)
        {
            double targetWeight =
                totalWeight *
                sampleIndex /
                (maximumPoints -
                    1);

            while (edgeIndex <
                       source.Count -
                           2 &&
                   cumulativeWeight[edgeIndex +
                        1] <
                       targetWeight)
            {
                edgeIndex++;
            }

            double edgeWeight =
                cumulativeWeight[edgeIndex +
                    1] -
                cumulativeWeight[edgeIndex];

            double amount =
                edgeWeight >
                    0.0
                    ? (targetWeight -
                       cumulativeWeight[edgeIndex]) /
                        edgeWeight
                    : 0.0;

            resampled.Add(
                Interpolate(
                    source[edgeIndex],
                    source[edgeIndex +
                        1],
                    amount));
        }

        resampled.Add(
            source[^1]);

        return resampled;
    }

    private static void AddIfSeparated(
        List<ArHorizontalRoutePoint> points,
        ArHorizontalRoutePoint candidate)
    {
        if (points.Count ==
                0 ||
            Distance(
                points[^1],
                candidate) >=
                MinimumCornerTrimMeters)
        {
            points.Add(
                candidate);
        }
    }

    private static ArHorizontalRoutePoint InterpolateByDistance(
        ArHorizontalRoutePoint from,
        ArHorizontalRoutePoint toward,
        float distanceMeters,
        float fullDistanceMeters)
    {
        double amount =
            fullDistanceMeters >
                0.0f
                ? Math.Clamp(
                    distanceMeters /
                        fullDistanceMeters,
                    0.0f,
                    1.0f)
                : 0.0;

        return Interpolate(
            from,
            toward,
            amount);
    }

    private static ArHorizontalRoutePoint Interpolate(
        ArHorizontalRoutePoint start,
        ArHorizontalRoutePoint end,
        double amount)
    {
        float t =
            (float)Math.Clamp(
                amount,
                0.0,
                1.0);

        return new ArHorizontalRoutePoint(
            start.X +
                (end.X -
                 start.X) *
                t,
            start.Z +
                (end.Z -
                 start.Z) *
                t,
            start.DistanceFromWindowStartMeters +
                (end.DistanceFromWindowStartMeters -
                 start.DistanceFromWindowStartMeters) *
                t);
    }

    private static double GetTurnAngleDegrees(
        ArHorizontalRoutePoint previous,
        ArHorizontalRoutePoint corner,
        ArHorizontalRoutePoint next)
    {
        float incomingX =
            corner.X -
            previous.X;

        float incomingZ =
            corner.Z -
            previous.Z;

        float outgoingX =
            next.X -
            corner.X;

        float outgoingZ =
            next.Z -
            corner.Z;

        float incomingLength =
            MathF.Sqrt(
                incomingX *
                    incomingX +
                incomingZ *
                    incomingZ);

        float outgoingLength =
            MathF.Sqrt(
                outgoingX *
                    outgoingX +
                outgoingZ *
                    outgoingZ);

        if (incomingLength <
                MinimumCornerTrimMeters ||
            outgoingLength <
                MinimumCornerTrimMeters)
        {
            return 0.0;
        }

        double dot =
            incomingX /
                incomingLength *
                outgoingX /
                outgoingLength +
            incomingZ /
                incomingLength *
                outgoingZ /
                outgoingLength;

        return Math.Acos(
                Math.Clamp(
                    dot,
                    -1.0,
                    1.0)) *
            180.0 /
            Math.PI;
    }

    private static bool IsFinite(
        ArHorizontalRoutePoint point)
    {
        return float.IsFinite(
                point.X) &&
            float.IsFinite(
                point.Z) &&
            double.IsFinite(
                point.DistanceFromWindowStartMeters);
    }

    private static float Distance(
        ArHorizontalRoutePoint first,
        ArHorizontalRoutePoint second)
    {
        float x =
            second.X -
            first.X;

        float z =
            second.Z -
            first.Z;

        return MathF.Sqrt(
            x *
                x +
            z *
                z);
    }

    private static float PolylineLength(
        IReadOnlyList<ArHorizontalRoutePoint> points)
    {
        float length =
            0.0f;

        for (int i = 0;
             i < points.Count -
                    1;
             i++)
        {
            length +=
                Distance(
                    points[i],
                    points[i +
                        1]);
        }

        return length;
    }

    private static float MaximumSegmentLength(
        IReadOnlyList<ArHorizontalRoutePoint> points)
    {
        float maximum =
            0.0f;

        for (int i = 0;
             i < points.Count -
                    1;
             i++)
        {
            maximum =
                MathF.Max(
                    maximum,
                    Distance(
                        points[i],
                        points[i +
                            1]));
        }

        return maximum;
    }

    private static bool PointsEqual(
        ArHorizontalRoutePoint first,
        ArHorizontalRoutePoint second) =>
        first.X == second.X &&
        first.Z == second.Z &&
        first.DistanceFromWindowStartMeters ==
            second.DistanceFromWindowStartMeters;

    public readonly record struct GeometryPreparationResult(
        IReadOnlyList<ArHorizontalRoutePoint> Points,
        int InputPointCount,
        int DetailedPointCount,
        int RemovedPointCount,
        int BeveledCornerCount,
        int InsertedSubdivisionPointCount,
        bool WasCapacityResampled,
        bool FirstPointPreserved,
        bool FinalPointPreserved,
        float SourceLengthMeters,
        float RenderedLengthMeters,
        float MaximumRenderedSegmentLengthMeters);
}
