using System;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Publishes one immutable account of ARCore tracking transitions for both
/// MAUI diagnostics and Evergine render gates. Intentional lifecycle pauses
/// are counted separately from tracking loss observed while the session is
/// actively running.
/// </summary>
public static class ARTrackingStateBridge
{
    private static readonly object sync = new();

    private static TrackingSnapshot current =
        TrackingSnapshot.Unavailable;

    private static long version;

    public static TrackingSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current.WithCurrentLossDuration(
                    DateTimeOffset.UtcNow);
            }
        }
    }

    public static TrackingSnapshot PublishObservation(
        long sessionGeneration,
        string trackingState,
        string failureReason,
        long frameTimestamp,
        bool depthEnabled)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (sync)
        {
            bool sameSession =
                current.IsAvailable &&
                current.SessionGeneration == sessionGeneration;

            TrackingSnapshot previous =
                sameSession
                    ? current
                    : TrackingSnapshot.ForSession(
                        sessionGeneration,
                        now,
                        depthEnabled);

            bool isTracking = string.Equals(
                trackingState,
                "Tracking",
                StringComparison.OrdinalIgnoreCase);

            string normalizedReason =
                failureReason ?? string.Empty;

            bool transition =
                !previous.IsAvailable ||
                previous.IsTracking != isTracking ||
                !string.Equals(
                    previous.TrackingState,
                    trackingState,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    previous.FailureReason,
                    normalizedReason,
                    StringComparison.Ordinal) ||
                previous.IsIntentionalLifecycleEvent ||
                previous.DepthEnabled != depthEnabled;

            if (!transition)
            {
                current = previous.WithFrameTimestamp(
                    frameTimestamp);

                return current;
            }

            DateTimeOffset? lossStartedAtUtc =
                previous.LossStartedAtUtc;

            DateTimeOffset? lossEndedAtUtc =
                previous.LossEndedAtUtc;

            long lastLossDurationMilliseconds =
                previous.LastLossDurationMilliseconds;

            long activePauseTransitionCount =
                previous.ActivePauseTransitionCount;

            long recoveryTransitionCount =
                previous.RecoveryTransitionCount;

            string recoveryTransition =
                previous.RecoveryTransition;

            if (!isTracking &&
                (previous.IsTracking ||
                 previous.IsIntentionalLifecycleEvent ||
                 !previous.LossStartedAtUtc.HasValue))
            {
                lossStartedAtUtc = now;
                lossEndedAtUtc = null;
                activePauseTransitionCount++;
            }
            else if (isTracking &&
                     !previous.IsTracking &&
                     !previous.IsIntentionalLifecycleEvent &&
                     previous.LossStartedAtUtc.HasValue)
            {
                lossEndedAtUtc = now;
                lastLossDurationMilliseconds = Math.Max(
                    0,
                    (long)(now - previous.LossStartedAtUtc.Value)
                        .TotalMilliseconds);
                recoveryTransitionCount++;
                recoveryTransition =
                    $"{previous.TrackingState}/{previous.FailureReason} -> {trackingState}";
                lossStartedAtUtc = null;
            }

            current = new TrackingSnapshot(
                true,
                Interlocked.Increment(ref version),
                sessionGeneration,
                trackingState ?? string.Empty,
                normalizedReason,
                isTracking,
                false,
                "Running",
                depthEnabled,
                lossStartedAtUtc,
                lossEndedAtUtc,
                lastLossDurationMilliseconds,
                lossStartedAtUtc.HasValue
                    ? Math.Max(
                        0,
                        (long)(now - lossStartedAtUtc.Value)
                            .TotalMilliseconds)
                    : 0,
                now,
                frameTimestamp,
                activePauseTransitionCount,
                previous.LifecyclePauseTransitionCount,
                recoveryTransitionCount,
                recoveryTransition);

            return current;
        }
    }

    public static TrackingSnapshot PublishLifecycleTransition(
        long sessionGeneration,
        string lifecycleState,
        string reason,
        bool depthEnabled)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (sync)
        {
            if (current.IsAvailable &&
                current.SessionGeneration == sessionGeneration &&
                current.IsIntentionalLifecycleEvent &&
                string.Equals(
                    current.LifecycleState,
                    lifecycleState,
                    StringComparison.Ordinal) &&
                string.Equals(
                    current.FailureReason,
                    reason,
                    StringComparison.Ordinal))
            {
                return current;
            }

            bool sameSession =
                current.IsAvailable &&
                current.SessionGeneration == sessionGeneration;

            TrackingSnapshot previous =
                sameSession
                    ? current
                    : TrackingSnapshot.ForSession(
                        sessionGeneration,
                        now,
                        depthEnabled);

            bool closesActiveLoss =
                previous.LossStartedAtUtc.HasValue &&
                !previous.IsIntentionalLifecycleEvent;

            DateTimeOffset? lossEndedAtUtc =
                closesActiveLoss
                    ? now
                    : previous.LossEndedAtUtc;

            long lastLossDurationMilliseconds =
                closesActiveLoss
                    ? Math.Max(
                        0,
                        (long)(now - previous.LossStartedAtUtc!.Value)
                            .TotalMilliseconds)
                    : previous.LastLossDurationMilliseconds;

            current = new TrackingSnapshot(
                true,
                Interlocked.Increment(ref version),
                sessionGeneration,
                "LifecyclePaused",
                reason ?? string.Empty,
                false,
                true,
                lifecycleState ?? string.Empty,
                depthEnabled,
                null,
                lossEndedAtUtc,
                lastLossDurationMilliseconds,
                0,
                now,
                long.MinValue,
                previous.ActivePauseTransitionCount,
                previous.LifecyclePauseTransitionCount + 1,
                previous.RecoveryTransitionCount,
                string.Empty);

            return current;
        }
    }

    public static void Clear()
    {
        lock (sync)
        {
            current = TrackingSnapshot.Unavailable with
            {
                Version = Interlocked.Increment(ref version)
            };
        }
    }

    public readonly record struct TrackingSnapshot(
        bool IsAvailable,
        long Version,
        long SessionGeneration,
        string TrackingState,
        string FailureReason,
        bool IsTracking,
        bool IsIntentionalLifecycleEvent,
        string LifecycleState,
        bool DepthEnabled,
        DateTimeOffset? LossStartedAtUtc,
        DateTimeOffset? LossEndedAtUtc,
        long LastLossDurationMilliseconds,
        long CurrentLossDurationMilliseconds,
        DateTimeOffset TransitionAtUtc,
        long FrameTimestamp,
        long ActivePauseTransitionCount,
        long LifecyclePauseTransitionCount,
        long RecoveryTransitionCount,
        string RecoveryTransition)
    {
        public static TrackingSnapshot Unavailable => new(
            false,
            0,
            0,
            string.Empty,
            string.Empty,
            false,
            false,
            string.Empty,
            false,
            null,
            null,
            0,
            0,
            DateTimeOffset.MinValue,
            long.MinValue,
            0,
            0,
            0,
            string.Empty);

        internal static TrackingSnapshot ForSession(
            long sessionGeneration,
            DateTimeOffset timestamp,
            bool depthEnabled) =>
            Unavailable with
            {
                SessionGeneration = sessionGeneration,
                DepthEnabled = depthEnabled,
                TransitionAtUtc = timestamp
            };

        public bool IsRenderableFor(long sessionGeneration) =>
            IsAvailable &&
            IsTracking &&
            !IsIntentionalLifecycleEvent &&
            SessionGeneration == sessionGeneration;

        internal TrackingSnapshot WithFrameTimestamp(
            long frameTimestamp) =>
            this with
            {
                FrameTimestamp = frameTimestamp
            };

        internal TrackingSnapshot WithCurrentLossDuration(
            DateTimeOffset timestamp)
        {
            if (!LossStartedAtUtc.HasValue ||
                IsIntentionalLifecycleEvent)
            {
                return this;
            }

            return this with
            {
                CurrentLossDurationMilliseconds = Math.Max(
                    0,
                    (long)(timestamp - LossStartedAtUtc.Value)
                        .TotalMilliseconds)
            };
        }
    }
}
