using System;
using System.Numerics;

namespace RescuAR.AR;

/// <summary>Detects sustained yaw changes of the same verified ground anchor.
/// This invalidates registration; it never supplies a geographic heading.</summary>
public sealed class ARAnchorYawCorrectionPolicy
{
    // Five degrees moves a point 7.5 m away by about 65 cm. Require several
    // distinct frames over half a second so one noisy anchor pose cannot vote.
    public const float MinimumYawDriftDegrees = 5;
    public const long ConfirmationMilliseconds = 500;
    public const long MaximumObservationGapMilliseconds = 1000;
    private long session, ground, lastFrame, candidateStartedAt = -1;
    private long lastObservedAt = -1;
    private int candidateFrames, candidateDirection;
    private Quaternion? baseline;

    public void Reset()
    {
        session = ground = lastFrame = 0;
        lastObservedAt = -1;
        baseline = null;
        ClearCandidate();
    }

    public void SeedReference(long sessionGeneration, long groundReference, Quaternion? rotation)
    {
        if (session != sessionGeneration || ground != groundReference)
        {
            Reset();
            session = sessionGeneration;
            ground = groundReference;
        }
        if (baseline is null && sessionGeneration > 0 && groundReference > 0 &&
            rotation is { } value && IsFinite(value) && value.LengthSquared() >= 0.0001f)
            baseline = Quaternion.Normalize(value);
    }

    public bool Observe(long sessionGeneration, long groundReference, long frameTimestamp,
        long monotonicNow, Quaternion? rotation, out float yawDriftDegrees)
    {
        yawDriftDegrees = 0;
        SeedReference(sessionGeneration, groundReference, null);
        if (sessionGeneration <= 0 || groundReference <= 0 || frameTimestamp <= 0 ||
            monotonicNow < 0 || rotation is not { } value || !IsFinite(value) ||
            value.LengthSquared() < 0.0001f)
        {
            ClearCandidate();
            return false;
        }
        if (frameTimestamp <= lastFrame) return false;
        lastFrame = frameTimestamp;
        if (lastObservedAt >= 0 && (monotonicNow < lastObservedAt ||
            monotonicNow - lastObservedAt > MaximumObservationGapMilliseconds))
            ClearCandidate();
        lastObservedAt = monotonicNow;
        value = Quaternion.Normalize(value);
        if (baseline is not { } initial)
        {
            baseline = value;
            return false;
        }

        Quaternion change = Quaternion.Normalize(value * Quaternion.Inverse(initial));
        Vector3 changedUp = Vector3.Transform(Vector3.UnitY, change);
        // A major change in surface tilt is not evidence of a yaw-only world
        // correction. Floor replacement is handled by its own reference ID.
        if (changedUp.Y < MathF.Cos(10 * MathF.PI / 180))
        {
            ClearCandidate();
            return false;
        }
        float twistLengthSquared = change.Y * change.Y + change.W * change.W;
        if (twistLengthSquared < 0.0001f)
        {
            ClearCandidate();
            return false;
        }
        float signedYawDriftDegrees = (float)Math.IEEERemainder(
            2 * Math.Atan2(change.Y, change.W) * 180 / Math.PI, 360);
        yawDriftDegrees = MathF.Abs(signedYawDriftDegrees);
        if (yawDriftDegrees < MinimumYawDriftDegrees)
        {
            ClearCandidate();
            return false;
        }
        int direction = Math.Sign(signedYawDriftDegrees);
        if (candidateStartedAt < 0 || monotonicNow < candidateStartedAt || direction != candidateDirection)
        {
            candidateStartedAt = monotonicNow;
            candidateFrames = 1;
            candidateDirection = direction;
            return false;
        }
        candidateFrames++;
        if (candidateFrames < 3 || monotonicNow - candidateStartedAt < ConfirmationMilliseconds)
            return false;

        baseline = value;
        ClearCandidate();
        return true;
    }

    private void ClearCandidate()
    {
        candidateStartedAt = -1;
        candidateFrames = 0;
        candidateDirection = 0;
    }

    private static bool IsFinite(Quaternion value) => float.IsFinite(value.X) &&
        float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W) &&
        float.IsFinite(value.LengthSquared());
}
