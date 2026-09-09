using Android.Util;
using RescuAR.AR;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Ground-anchor recovery V2 for the retained ARCore Session.
///
/// Why V2 exists:
/// The first recovery implementation attempted updateGate.Wait(100 ms) from
/// CameraPage's one-second diagnostic timer. With ARCore UpdateMode.BLOCKING,
/// the frame worker owns/reacquires that gate almost continuously. On-device
/// testing showed the intended 1.25 s grace period stretching to ~6.5 s.
///
/// V2 does not poll/compete for the gate every second. Instead it:
/// 1. observes that camera tracking is valid but the retained Anchor is not;
/// 2. starts one asynchronous grace-period worker;
/// 3. queues on updateGate after the grace period;
/// 4. re-validates all state while owning the gate;
/// 5. releases only a still-stale Anchor;
/// 6. lets the existing ARCore frame loop resume its normal floor hit-test.
///
/// A normal Camera-tab Session.Pause()/Resume() does not destroy the Anchor.
/// </summary>
public sealed partial class ArCoreService
{
    private const string AnchorRecoveryLogTag =
        "RescuAR-AnchorRecovery";

    private const long GroundAnchorRecoveryGraceMilliseconds =
        1250;

    /*
     * If the ordinary floor hit-test has not produced a replacement after a
     * few seconds, emit one actionable message. Recovery remains armed.
     */
    private const long ReplacementAnchorSearchNoticeMilliseconds =
        5000;

    private readonly object groundAnchorRecoveryLock =
        new();

    private Task? groundAnchorRecoveryTask;

    /*
     * Incremented whenever CameraPage observes camera tracking unavailable.
     * A pending grace worker captures the generation. If tracking is lost
     * again during its grace interval, it must not release the Anchor after a
     * later, newer recovery.
     */
    private long groundAnchorRecoveryGeneration;

    /*
     * V6 durable replacement event.
     *
     * This increments ONLY after this recovery component itself releases a
     * stale retained Anchor. It is deliberately independent from short-lived
     * SpatialSnapshot.Anchor.IsAvailable changes.
     */
    private long groundAnchorReplacementGeneration;

    private bool groundAnchorReacquisitionArmed;

    private long replacementAnchorSearchStartedTimestamp =
        long.MinValue;

    private bool replacementAnchorSearchNoticeLogged;

    /// <inheritdoc />
    public long GroundAnchorReplacementGeneration
    {
        get
        {
            lock (groundAnchorRecoveryLock)
            {
                return groundAnchorReplacementGeneration;
            }
        }
    }

    /// <inheritdoc />
    public bool TryRecoverGroundAnchorIfNeeded()
    {
        if (session is null ||
            sessionPaused)
        {
            InvalidatePendingRecoveryCountdown();

            return false;
        }

        ARCameraPoseBridge.SpatialSnapshot spatial =
            ARCameraPoseBridge.CurrentFrame;

        /*
         * Never destroy an Anchor while camera tracking itself is unavailable.
         * Mark a new generation so any worker started before this tracking-loss
         * period becomes stale and exits without touching the Anchor.
         */
        if (!spatial.IsTracking ||
            !spatial.Pose.IsTracking)
        {
            InvalidatePendingRecoveryCountdown();

            return groundAnchorReacquisitionArmed;
        }

        Google.AR.Core.Anchor? anchor =
            spatialGroundAnchor;

        if (anchor is null)
        {
            ArmReplacementFloorSearch();

            return true;
        }

        string anchorTrackingState =
            GetAnchorTrackingState(
                anchor);

        if (anchorTrackingState.Equals(
                "Tracking",
                StringComparison.OrdinalIgnoreCase))
        {
            bool wasRecovering;

            lock (groundAnchorRecoveryLock)
            {
                wasRecovering =
                    groundAnchorReacquisitionArmed ||
                    groundAnchorRecoveryTask is not null;

                groundAnchorReacquisitionArmed =
                    false;

                replacementAnchorSearchStartedTimestamp =
                    long.MinValue;

                replacementAnchorSearchNoticeLogged =
                    false;
            }

            if (wasRecovering)
            {
                Log.Debug(
                    AnchorRecoveryLogTag,
                    "Retained/replacement ground anchor is TRACKING. " +
                    "Recovery state cleared.");
            }

            return false;
        }

        ScheduleStaleAnchorRecovery(
            anchor,
            anchorTrackingState);

        return groundAnchorReacquisitionArmed;
    }

    private void ScheduleStaleAnchorRecovery(
        Google.AR.Core.Anchor expectedAnchor,
        string observedTrackingState)
    {
        lock (groundAnchorRecoveryLock)
        {
            if (groundAnchorRecoveryTask is not null &&
                !groundAnchorRecoveryTask.IsCompleted)
            {
                return;
            }

            long generation =
                groundAnchorRecoveryGeneration;

            Log.Warn(
                AnchorRecoveryLogTag,
                "ARCore camera is TRACKING but retained ground anchor is " +
                $"{observedTrackingState}. Allowing " +
                $"{GroundAnchorRecoveryGraceMilliseconds} ms for natural " +
                "anchor relocalization.");

            groundAnchorRecoveryTask =
                RunStaleAnchorRecoveryAsync(
                    expectedAnchor,
                    generation);
        }
    }

    private async Task RunStaleAnchorRecoveryAsync(
        Google.AR.Core.Anchor expectedAnchor,
        long generation)
    {
        try
        {
            await Task.Delay(
                (int)GroundAnchorRecoveryGraceMilliseconds);

            /*
             * Queue behind the current ARCore frame instead of repeatedly
             * timing out from the UI diagnostic thread. SemaphoreSlim will
             * hand the gate to this waiting operation between frame updates.
             */
            await updateGate.WaitAsync();

            try
            {
                if (session is null ||
                    sessionPaused)
                {
                    return;
                }

                lock (groundAnchorRecoveryLock)
                {
                    if (generation !=
                        groundAnchorRecoveryGeneration)
                    {
                        Log.Debug(
                            AnchorRecoveryLogTag,
                            "Stale-anchor release cancelled because camera " +
                            "tracking was lost again during the grace period.");

                        return;
                    }
                }

                ARCameraPoseBridge.SpatialSnapshot spatial =
                    ARCameraPoseBridge.CurrentFrame;

                if (!spatial.IsTracking ||
                    !spatial.Pose.IsTracking)
                {
                    return;
                }

                Google.AR.Core.Anchor? currentAnchor =
                    spatialGroundAnchor;

                if (currentAnchor is null)
                {
                    ArmReplacementFloorSearch();

                    return;
                }

                /*
                 * If something else already replaced the Anchor, never detach
                 * the new one using a worker created for the previous object.
                 */
                if (!ReferenceEquals(
                        currentAnchor,
                        expectedAnchor))
                {
                    return;
                }

                string currentState =
                    GetAnchorTrackingState(
                        currentAnchor);

                if (currentState.Equals(
                        "Tracking",
                        StringComparison.OrdinalIgnoreCase))
                {
                    Log.Debug(
                        AnchorRecoveryLogTag,
                        "Retained ground anchor naturally returned to " +
                        "TRACKING during the grace period. No replacement " +
                        "is required.");

                    return;
                }

                Log.Warn(
                    AnchorRecoveryLogTag,
                    "Retained ground anchor is still stale after camera " +
                    "tracking recovery: " +
                    $"state={currentState}. Releasing it so the existing " +
                    "floor hit-test can reacquire.");

                /*
                 * updateGate is held, so ReleaseSpatialGroundAnchor() cannot
                 * race the frame worker's anchor-pose read.
                 */
                ReleaseSpatialGroundAnchor();

                hasLoggedGroundPlaneSearch =
                    false;

                long replacementGeneration;

                lock (groundAnchorRecoveryLock)
                {
                    /*
                     * This is the exact V6 transition from:
                     *
                     *     retained-anchor relocalization attempt
                     *
                     * to:
                     *
                     *     actual replacement-anchor recovery.
                     *
                     * CameraPage must react only to this generation change,
                     * never merely to a transient Anchor.IsAvailable=false.
                     */
                    groundAnchorReplacementGeneration++;

                    replacementGeneration =
                        groundAnchorReplacementGeneration;

                    groundAnchorReacquisitionArmed =
                        true;

                    replacementAnchorSearchStartedTimestamp =
                        Environment.TickCount64;

                    replacementAnchorSearchNoticeLogged =
                        false;
                }

                Log.Debug(
                    AnchorRecoveryLogTag,
                    "Stale ground anchor released after queued grace-period " +
                    "recovery. Replacement floor-anchor search is armed on " +
                    "subsequent ARCore frames. " +
                    $"replacementGeneration={replacementGeneration}");
            }
            finally
            {
                updateGate.Release();
            }
        }
        catch (Exception exception)
        {
            Log.Error(
                AnchorRecoveryLogTag,
                $"Queued ground-anchor recovery failed: {exception}");
        }
        finally
        {
            lock (groundAnchorRecoveryLock)
            {
                groundAnchorRecoveryTask =
                    null;
            }
        }
    }

    private void ArmReplacementFloorSearch()
    {
        bool logArmed =
            false;

        bool logSearchNotice =
            false;

        long searchDuration =
            0;

        lock (groundAnchorRecoveryLock)
        {
            if (!groundAnchorReacquisitionArmed)
            {
                groundAnchorReacquisitionArmed =
                    true;

                replacementAnchorSearchStartedTimestamp =
                    Environment.TickCount64;

                replacementAnchorSearchNoticeLogged =
                    false;

                hasLoggedGroundPlaneSearch =
                    false;

                logArmed =
                    true;
            }
            else if (replacementAnchorSearchStartedTimestamp !=
                     long.MinValue)
            {
                searchDuration =
                    Math.Max(
                        0,
                        Environment.TickCount64 -
                        replacementAnchorSearchStartedTimestamp);

                if (!replacementAnchorSearchNoticeLogged &&
                    searchDuration >=
                        ReplacementAnchorSearchNoticeMilliseconds)
                {
                    replacementAnchorSearchNoticeLogged =
                        true;

                    logSearchNotice =
                        true;
                }
            }
        }

        if (logArmed)
        {
            Log.Debug(
                AnchorRecoveryLogTag,
                "No usable retained ground anchor exists. Existing ARCore " +
                "frame loop will search for a replacement horizontal floor " +
                "anchor.");
        }

        if (logSearchNotice)
        {
            Log.Warn(
                AnchorRecoveryLogTag,
                "Replacement anchor has not been acquired after " +
                $"{searchDuration} ms. Recovery is still active. Point the " +
                "lower-middle camera view at a well-lit, textured floor so " +
                "the existing ARCore plane hit-test can succeed.");
        }
    }

    private static string GetAnchorTrackingState(
        Google.AR.Core.Anchor anchor)
    {
        try
        {
            return anchor.TrackingState.ToString();
        }
        catch (Exception exception)
        {
            Log.Warn(
                AnchorRecoveryLogTag,
                "Reading retained ground-anchor TrackingState failed. " +
                "Treating the anchor as STOPPED. " +
                $"{exception.GetType().Name}: {exception.Message}");

            return "Stopped";
        }
    }

    private void InvalidatePendingRecoveryCountdown()
    {
        lock (groundAnchorRecoveryLock)
        {
            groundAnchorRecoveryGeneration++;
        }
    }
}
