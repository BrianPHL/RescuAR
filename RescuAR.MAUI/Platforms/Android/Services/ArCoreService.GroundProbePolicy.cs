using Android.Util;
using RescuAR.AR;
using System;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Bounded, state-aware budget for ARCore plane and Depth hit tests.
/// </summary>
public sealed partial class ArCoreService
{
    private const int MaximumGroundProbesPerSearchWindow = 144;
    private const int MaximumGroundProbesPerSession = 600;
    private const long GroundProbeSearchWindowCooldownMilliseconds = 30_000;
    private const long MaximumGroundProbeBackoffMilliseconds = 8_000;
    private const long GroundProbeMetricsIntervalMilliseconds = 10_000;

    private int groundProbesThisSweep;
    private int groundProbesThisSearchWindow;
    private int groundProbesThisSession;
    private int groundProbeFailureStreak;
    private long groundProbeSuspendedUntilTimestamp = long.MinValue;
    private long lastGroundProbeMetricsTimestamp = long.MinValue;

    private long rejectedNoTrackable;
    private long rejectedEdge;
    private long rejectedNormal;
    private long rejectedHeight;
    private long rejectedConsistency;
    private long rejectedBudget;
    private long rejectedPoorTracking;
    private long acceptedGroundCandidates;

    private void ResetGroundProbeSessionBudget()
    {
        groundProbesThisSweep = 0;
        groundProbesThisSearchWindow = 0;
        groundProbesThisSession = 0;
        groundProbeFailureStreak = 0;
        groundProbeSuspendedUntilTimestamp = long.MinValue;
        lastGroundProbeMetricsTimestamp = long.MinValue;
        rejectedNoTrackable = 0;
        rejectedEdge = 0;
        rejectedNormal = 0;
        rejectedHeight = 0;
        rejectedConsistency = 0;
        rejectedBudget = 0;
        rejectedPoorTracking = 0;
        acceptedGroundCandidates = 0;
    }

    private bool TryBeginGroundProbeSweep(long now)
    {
        groundProbesThisSweep = 0;

        if (sessionPaused ||
            ARGroundStateBridge.Current.State ==
                ARGroundLifecycleState.Suspended)
        {
            RecordGroundProbeRejection(GroundProbeRejection.PoorTracking);
            return false;
        }

        if (groundProbesThisSession >= MaximumGroundProbesPerSession)
        {
            groundProbeSuspendedUntilTimestamp = long.MaxValue;
            RecordGroundProbeRejection(GroundProbeRejection.Budget);
            LogGroundProbeMetricsIfNeeded(now, "session probe budget exhausted");
            return false;
        }

        if (groundProbeSuspendedUntilTimestamp != long.MinValue &&
            now < groundProbeSuspendedUntilTimestamp)
        {
            return false;
        }

        if (groundProbesThisSearchWindow >= MaximumGroundProbesPerSearchWindow)
        {
            groundProbesThisSearchWindow = 0;
            groundProbeSuspendedUntilTimestamp =
                now + GroundProbeSearchWindowCooldownMilliseconds;
            RecordGroundProbeRejection(GroundProbeRejection.Budget);
            LogGroundProbeMetricsIfNeeded(now, "search-window budget cooldown");
            return false;
        }

        groundProbeSuspendedUntilTimestamp = long.MinValue;
        return true;
    }

    private bool TryConsumeGroundProbeBudget(long now)
    {
        int sweepLimit = Math.Max(
            1,
            powerThermalDecision.MaximumGroundProbesPerSweep);

        if (groundProbesThisSweep >= sweepLimit ||
            groundProbesThisSearchWindow >= MaximumGroundProbesPerSearchWindow ||
            groundProbesThisSession >= MaximumGroundProbesPerSession)
        {
            RecordGroundProbeRejection(GroundProbeRejection.Budget);
            LogGroundProbeMetricsIfNeeded(now, "probe denied by budget");
            return false;
        }

        groundProbesThisSweep++;
        groundProbesThisSearchWindow++;
        groundProbesThisSession++;
        return true;
    }

    private long GetGroundProbeIntervalMilliseconds()
    {
        int exponentialShift = Math.Min(5, groundProbeFailureStreak);
        long failureInterval = GroundPlaneSearchIntervalMilliseconds <<
            exponentialShift;

        return Math.Min(
            MaximumGroundProbeBackoffMilliseconds,
            checked(failureInterval * Math.Max(
                1,
                powerThermalDecision.GroundProbeIntervalMultiplier)));
    }

    private void CompleteGroundProbeSweep(
        long now,
        bool usableEvidenceObserved)
    {
        if (usableEvidenceObserved)
        {
            groundProbeFailureStreak = 0;
        }
        else
        {
            groundProbeFailureStreak = Math.Min(
                groundProbeFailureStreak + 1,
                8);
        }

        nextGroundPlaneSearchTimestamp =
            now + GetGroundProbeIntervalMilliseconds();

        LogGroundProbeMetricsIfNeeded(
            now,
            usableEvidenceObserved
                ? "usable ground evidence"
                : "no usable ground evidence");
    }

    private void RegisterGroundProbeSuccess(long now)
    {
        acceptedGroundCandidates++;
        groundProbeFailureStreak = 0;
        groundProbesThisSearchWindow = 0;
        groundProbeSuspendedUntilTimestamp = long.MinValue;
        LogGroundProbeMetricsIfNeeded(now, "ground acquired");
    }

    private void RecordGroundProbeRejection(GroundProbeRejection rejection)
    {
        switch (rejection)
        {
            case GroundProbeRejection.Edge:
                rejectedEdge++;
                break;
            case GroundProbeRejection.Normal:
                rejectedNormal++;
                break;
            case GroundProbeRejection.Height:
                rejectedHeight++;
                break;
            case GroundProbeRejection.Consistency:
                rejectedConsistency++;
                break;
            case GroundProbeRejection.Budget:
                rejectedBudget++;
                break;
            case GroundProbeRejection.PoorTracking:
                rejectedPoorTracking++;
                break;
            default:
                rejectedNoTrackable++;
                break;
        }
    }

    private void LogGroundProbeMetricsIfNeeded(long now, string reason)
    {
        if (lastGroundProbeMetricsTimestamp != long.MinValue &&
            now - lastGroundProbeMetricsTimestamp <
                GroundProbeMetricsIntervalMilliseconds)
        {
            return;
        }

        lastGroundProbeMetricsTimestamp = now;

        Log.Info(
            SpatialPoseTag,
            "ARCORE_GROUND_PROBE_METRICS " +
            $"session={groundProbesThisSession}/{MaximumGroundProbesPerSession}; " +
            $"searchWindow={groundProbesThisSearchWindow}/{MaximumGroundProbesPerSearchWindow}; " +
            $"sweep={groundProbesThisSweep}/{powerThermalDecision.MaximumGroundProbesPerSweep}; " +
            $"failureStreak={groundProbeFailureStreak}; " +
            $"nextIntervalMs={GetGroundProbeIntervalMilliseconds()}; " +
            $"accepted={acceptedGroundCandidates}; rejectedNoTrackable={rejectedNoTrackable}; " +
            $"rejectedEdge={rejectedEdge}; rejectedNormal={rejectedNormal}; " +
            $"rejectedHeight={rejectedHeight}; rejectedConsistency={rejectedConsistency}; " +
            $"rejectedBudget={rejectedBudget}; rejectedPoorTracking={rejectedPoorTracking}; " +
            $"reason='{reason}'.");
    }

    private enum GroundProbeRejection
    {
        NoTrackable,
        PoorTracking,
        Edge,
        Normal,
        Height,
        Consistency,
        Budget
    }
}
