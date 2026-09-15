using RescuAR.Navigation.Projection;
using System;
using System.Collections.Generic;

namespace RescuAR.AR;

/// <summary>
/// Produces bounded local route geometry before pooled segment rendering.
/// Tiny spans are removed, sharp corners are beveled, and long spans are
/// subdivided so one raw route edge cannot create an oversized visual wedge.
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
            new(
                Math.Min(
                    source.Count,
                    maximumPoints));

        int removedPoints =
            0;

        foreach (ArHorizontalRoutePoint point in
                 source)
        {
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
                removedPoints,
                0,
                0,
                false);
        }

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

        List<ArHorizontalRoutePoint> prepared =
            new(
                Math.Min(
                    maximumPoints,
                    beveled.Count *
                        2));

        prepared.Add(
            beveled[0]);

        int insertedSubdivisionPoints =
            0;

        bool truncated =
            false;

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
                if (prepared.Count >=
                    maximumPoints)
                {
                    truncated =
                        true;

                    break;
                }

                double amount =
                    (double)part /
                    partCount;

                prepared.Add(
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

            if (truncated)
            {
                break;
            }
        }

        return new GeometryPreparationResult(
            prepared,
            removedPoints,
            beveledCorners,
            insertedSubdivisionPoints,
            truncated);
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

    public readonly record struct GeometryPreparationResult(
        IReadOnlyList<ArHorizontalRoutePoint> Points,
        int RemovedPointCount,
        int BeveledCornerCount,
        int InsertedSubdivisionPointCount,
        bool WasTruncated);
}
