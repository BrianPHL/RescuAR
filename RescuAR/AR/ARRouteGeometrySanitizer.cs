using RescuAR.Navigation.Projection;
using System;
using System.Collections.Generic;

namespace RescuAR.AR;

/// <summary>
/// Produces bounded local route geometry before pooled segment rendering.
/// Tiny duplicate spans are removed and long source spans are subdivided.
/// Every retained GeoJSON vertex stays on the line: bevels and budget-driven
/// chords can cut across the inside of a building or an unmapped crossing.
/// If too many source vertices need rendering, the renderer rejects the
/// window and reports the capacity limit rather than inventing a shortcut.
/// </summary>
public static class ARRouteGeometrySanitizer
{
    private const float MinimumPointSpacingMeters =
        0.01f;

    private const float MaximumRenderedSegmentLengthMeters =
        4.0f;

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

        // Retain each mapped corner exactly. Filling in straight source
        // edges is safe because interpolation stays on the same line.
        List<ArHorizontalRoutePoint> beveled = cleaned;
        int beveledCorners = 0;

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

        // Discard only the optional interpolation points when capacity is
        // tight. Never connect across omitted source corners. If even the
        // source corners exceed capacity, the renderer rejects the window.
        IReadOnlyList<ArHorizontalRoutePoint> prepared =
            capacityLimited ? cleaned : detailed;

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

    private static ArHorizontalRoutePoint Interpolate(
        ArHorizontalRoutePoint start,
        ArHorizontalRoutePoint end,
        double amount)
    {
        if (amount <=
            0.0)
        {
            return start;
        }

        if (amount >=
            1.0)
        {
            /*
             * Preserve the authoritative endpoint exactly. Reconstructing it
             * through floating-point arithmetic can differ by one bit and
             * falsely fail the renderer's endpoint-integrity check.
             */
            return end;
        }

        float t =
            (float)amount;

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
        bool WasCapacityLimited,
        bool FirstPointPreserved,
        bool FinalPointPreserved,
        float SourceLengthMeters,
        float RenderedLengthMeters,
        float MaximumRenderedSegmentLengthMeters);
}
