using System;
using System.Numerics;

namespace RescuAR.AR;

/// <summary>A bounded direction marker near the phone, not a GPS-to-road line.</summary>
public static class RoadApproachArrowGeometry
{
    public readonly record struct Segment(Vector2 Start, Vector2 End)
    {
        public float YawRadians => MathF.Atan2(End.X - Start.X, End.Y - Start.Y);
    }
    public static Segment[] Create(Vector2 direction)
    {
        float length = direction.LengthSquared();
        if (!float.IsFinite(length) || length < 0.0001f) return [];
        direction = Vector2.Normalize(direction);
        Vector2 perpendicular = new(-direction.Y, direction.X);
        Vector2 tip = direction * 2.0f;
        Vector2 wingBase = direction * 1.25f;
        return [new(direction * 0.5f, tip),
            new(wingBase + perpendicular * 0.45f, tip),
            new(wingBase - perpendicular * 0.45f, tip)];
    }
}
