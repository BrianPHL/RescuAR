using System;
using System.Numerics;

namespace RescuAR.AR;

/// <summary>
/// Projects bounded simulation outlines with the display-oriented ARCore pose
/// and projection. Clips the whole segment before dividing by depth.
/// </summary>
public static class FloodSimulationProjection
{
    public static bool TryProjectSegment(Vector3 start, Vector3 end, Vector3 camera,
        Quaternion rotation, ARCameraPoseBridge.ProjectionSnapshot projection,
        out Vector2 screenStart, out Vector2 screenEnd)
    {
        screenStart = screenEnd = default;
        float length = rotation.LengthSquared();
        if (!projection.IsAvailable || !Finite(start) || !Finite(end) || !Finite(camera) ||
            !float.IsFinite(length) || length < 0.0001f) return false;
        var inverse = Quaternion.Inverse(Quaternion.Normalize(rotation));
        var matrix = new Matrix4x4(
            projection.M11, projection.M21, projection.M31, projection.M41,
            projection.M12, projection.M22, projection.M32, projection.M42,
            projection.M13, projection.M23, projection.M33, projection.M43,
            projection.M14, projection.M24, projection.M34, projection.M44);
        Vector4 a = Vector4.Transform(new Vector4(Vector3.Transform(start - camera, inverse), 1), matrix);
        Vector4 b = Vector4.Transform(new Vector4(Vector3.Transform(end - camera, inverse), 1), matrix);
        float enter = 0, exit = 1;
        for (int plane = 0; plane < 6; plane++)
        {
            float da = Plane(a, plane), db = Plane(b, plane);
            if (!float.IsFinite(da) || !float.IsFinite(db) || (da < 0 && db < 0)) return false;
            if (da < 0) enter = MathF.Max(enter, da / (da - db));
            else if (db < 0) exit = MathF.Min(exit, da / (da - db));
        }
        if (enter > exit) return false;
        Vector4 clippedA = Vector4.Lerp(a, b, enter), clippedB = Vector4.Lerp(a, b, exit);
        if (clippedA.W <= 0.0001f || clippedB.W <= 0.0001f) return false;
        screenStart = new(clippedA.X / clippedA.W * 0.5f + 0.5f, 0.5f - clippedA.Y / clippedA.W * 0.5f);
        screenEnd = new(clippedB.X / clippedB.W * 0.5f + 0.5f, 0.5f - clippedB.Y / clippedB.W * 0.5f);
        return float.IsFinite(screenStart.X) && float.IsFinite(screenStart.Y) &&
            float.IsFinite(screenEnd.X) && float.IsFinite(screenEnd.Y);
    }
    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static float Plane(Vector4 v, int plane) => plane switch
    {
        0 => v.W + v.X, 1 => v.W - v.X, 2 => v.W + v.Y,
        3 => v.W - v.Y, 4 => v.W + v.Z, _ => v.W - v.Z
    };
}
