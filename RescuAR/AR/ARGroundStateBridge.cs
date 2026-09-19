using RescuAR.Diagnostics;
using System;
using System.Threading;

namespace RescuAR.AR;

/// <summary>
/// Mandatory trust level for every ground-relative AR publication.
/// </summary>
public enum ARGroundTrust
{
    None = 0,
    Provisional = 1,
    Verified = 2
}

/// <summary>
/// Single ground lifecycle shared by acquisition, recovery, route placement,
/// flood placement, and Depth publication.
/// </summary>
public enum ARGroundLifecycleState
{
    Unavailable = 0,
    Searching = 1,
    Provisional = 2,
    Verified = 3,
    Recovering = 4,
    Suspended = 5
}

public static class ARGroundStateBridge
{
    private const string LogTag = "RescuAR-GroundState";

    private static readonly object sync = new();

    private static GroundStateSnapshot current =
        GroundStateSnapshot.Unavailable;

    private static long stateVersion;
    private static long referenceGeneration;

    public static GroundStateSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public static GroundStateSnapshot BeginSearch(
        ARRenderGenerationToken renderGeneration,
        bool recovering,
        string reason)
    {
        ARGroundLifecycleState state =
            recovering
                ? ARGroundLifecycleState.Recovering
                : ARGroundLifecycleState.Searching;

        return Transition(
            renderGeneration,
            state,
            ARGroundTrust.None,
            advanceReferenceGeneration: false,
            reason);
    }

    public static GroundStateSnapshot PublishProvisional(
        ARRenderGenerationToken renderGeneration,
        string reason) =>
        Transition(
            renderGeneration,
            ARGroundLifecycleState.Provisional,
            ARGroundTrust.Provisional,
            advanceReferenceGeneration:
                Current.Trust != ARGroundTrust.Provisional,
            reason);

    public static GroundStateSnapshot PublishVerified(
        ARRenderGenerationToken renderGeneration,
        string reason) =>
        Transition(
            renderGeneration,
            ARGroundLifecycleState.Verified,
            ARGroundTrust.Verified,
            advanceReferenceGeneration: true,
            reason);

    public static GroundStateSnapshot RefreshRenderGeneration(
        ARRenderGenerationToken renderGeneration,
        string reason)
    {
        GroundStateSnapshot snapshot = Current;

        return Transition(
            renderGeneration,
            snapshot.State,
            snapshot.Trust,
            advanceReferenceGeneration: false,
            reason);
    }

    public static GroundStateSnapshot Suspend(
        ARRenderGenerationToken renderGeneration,
        string reason) =>
        Transition(
            renderGeneration,
            ARGroundLifecycleState.Suspended,
            ARGroundTrust.None,
            advanceReferenceGeneration:
                Current.Trust != ARGroundTrust.None,
            reason);

    public static GroundStateSnapshot InvalidateAndSearch(
        ARRenderGenerationToken renderGeneration,
        bool recovering,
        string reason) =>
        Transition(
            renderGeneration,
            recovering
                ? ARGroundLifecycleState.Recovering
                : ARGroundLifecycleState.Searching,
            ARGroundTrust.None,
            advanceReferenceGeneration:
                Current.Trust != ARGroundTrust.None,
            reason);

    public static void Clear(string reason)
    {
        GroundStateSnapshot next;

        lock (sync)
        {
            long nextVersion =
                Interlocked.Increment(ref stateVersion);

            if (current.Trust != ARGroundTrust.None)
            {
                referenceGeneration++;
            }

            next = new GroundStateSnapshot(
                nextVersion,
                ARRenderGenerationToken.Invalid,
                referenceGeneration,
                ARGroundLifecycleState.Unavailable,
                ARGroundTrust.None,
                Environment.TickCount64,
                NormalizeReason(reason));

            current = next;
        }

        LogTransition(next);
    }

    private static GroundStateSnapshot Transition(
        ARRenderGenerationToken renderGeneration,
        ARGroundLifecycleState state,
        ARGroundTrust trust,
        bool advanceReferenceGeneration,
        string reason)
    {
        if (!renderGeneration.IsValid ||
            !ARRenderGenerationBridge.IsCurrentSession(
                renderGeneration))
        {
            return Current;
        }

        string normalizedReason =
            NormalizeReason(reason);

        GroundStateSnapshot next;

        lock (sync)
        {
            if (!advanceReferenceGeneration &&
                current.RenderGeneration == renderGeneration &&
                current.State == state &&
                current.Trust == trust &&
                string.Equals(
                    current.Reason,
                    normalizedReason,
                    StringComparison.Ordinal))
            {
                return current;
            }

            if (advanceReferenceGeneration)
            {
                referenceGeneration++;
            }

            long nextVersion =
                Interlocked.Increment(ref stateVersion);

            next = new GroundStateSnapshot(
                nextVersion,
                renderGeneration,
                referenceGeneration,
                state,
                trust,
                Environment.TickCount64,
                normalizedReason);

            current = next;
        }

        LogTransition(next);
        return next;
    }

    private static string NormalizeReason(string reason) =>
        string.IsNullOrWhiteSpace(reason)
            ? "unspecified"
            : reason.Trim();

    private static void LogTransition(GroundStateSnapshot snapshot)
    {
        AndroidLog.Info(
            LogTag,
            "ARCORE_GROUND_STATE " +
            $"stateVersion={snapshot.StateVersion}; " +
            $"referenceGeneration={snapshot.ReferenceGeneration}; " +
            $"state={snapshot.State}; trust={snapshot.Trust}; " +
            $"sessionGeneration={snapshot.RenderGeneration.SessionGeneration}; " +
            $"reason='{snapshot.Reason}'.");
    }

    public readonly record struct GroundStateSnapshot(
        long StateVersion,
        ARRenderGenerationToken RenderGeneration,
        long ReferenceGeneration,
        ARGroundLifecycleState State,
        ARGroundTrust Trust,
        long UpdatedAtMonotonicMilliseconds,
        string Reason)
    {
        public static GroundStateSnapshot Unavailable =>
            new(
                0,
                ARRenderGenerationToken.Invalid,
                0,
                ARGroundLifecycleState.Unavailable,
                ARGroundTrust.None,
                long.MinValue,
                string.Empty);

        public bool HasGroundReference =>
            Trust != ARGroundTrust.None &&
            ReferenceGeneration > 0 &&
            RenderGeneration.SessionGeneration > 0;
    }
}
