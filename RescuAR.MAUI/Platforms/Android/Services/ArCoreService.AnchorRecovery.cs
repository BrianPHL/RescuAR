using Android.Util;
using RescuAR.AR;
using RescuAR.Navigation.Projection;

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

    /*
     * PAUSED means ARCore may resume tracking the same anchor. Because the
     * renderer now holds the last valid visual placement during temporary
     * tracking loss, recovery can favor continuity over aggressive anchor
     * replacement. Five seconds substantially reduces unnecessary detach /
     * reacquire cycles after short lighting, feature, or motion failures.
     */
    private const long GroundAnchorRecoveryGraceMilliseconds =
        5000;

    /*
     * If the ordinary floor hit-test has not produced a replacement after a
     * few seconds, emit one actionable message. Recovery remains armed.
     */
    private const long ReplacementAnchorSearchNoticeMilliseconds =
        5000;

    private readonly object groundAnchorRecoveryLock =
        new();

    private Task? groundAnchorRecoveryTask;

    private CancellationTokenSource?
        groundAnchorRecoveryCancellation;

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
     * This increments only after this component releases an anchor because it
     * is stale or has moved outside the local AR navigation radius. It remains
     * independent from short-lived SpatialSnapshot.Anchor.IsAvailable changes.
     */
    private long groundAnchorReplacementGeneration;

    private bool groundAnchorReacquisitionArmed;

    private long replacementAnchorSearchStartedTimestamp =
        long.MinValue;

    private bool replacementAnchorSearchNoticeLogged;

    private const long ImplausibleGroundHeightConfirmationMilliseconds =
        1000;

    private long implausibleGroundHeightCandidateStartedTimestamp =
        long.MinValue;

    private Google.AR.Core.Anchor?
        implausibleGroundHeightCandidateAnchor;

    /// <summary>
    /// Retires a still-valid anchor once it is no longer local to the camera
    /// or remains vertically inconsistent with the tracked camera.
    ///
    /// This method runs from the ARCore frame worker while updateGate is held,
    /// so releasing the anchor cannot race the normal pose read. Route state
    /// is not cleared; CameraPage observes the replacement generation and
    /// republishes the current geographic route progress in the new frame.
    /// </summary>
    private bool TryRetireGroundAnchorBeyondLocalWindow(
        float cameraX,
        float cameraY,
        float cameraZ)
    {
        Google.AR.Core.Anchor? anchor =
            spatialGroundAnchor;

        if (anchor is null ||
            !GetAnchorTrackingState(
                    anchor)
                .Equals(
                    "Tracking",
                    StringComparison.OrdinalIgnoreCase))
        {
            ResetImplausibleGroundHeightCandidate();

            return false;
        }

        using Google.AR.Core.Pose? anchorPose =
            anchor.Pose;

        if (anchorPose is null)
        {
            ResetImplausibleGroundHeightCandidate();

            return false;
        }

        float[] anchorTranslation =
            new float[3];

        anchorPose.GetTranslation(
            anchorTranslation,
            0);

        float deltaX =
            cameraX -
            anchorTranslation[0];

        float deltaZ =
            cameraZ -
            anchorTranslation[2];

        float distanceMeters =
            LocalArNavigationPolicy.GetHorizontalDistanceMeters(
                deltaX,
                deltaZ);

        bool heightPlausible =
            LocalArNavigationPolicy
                .IsCameraHeightAboveGroundPlausible(
                    cameraY,
                    anchorTranslation[1],
                    out float cameraHeightAboveGroundMeters);

        long now =
            Environment.TickCount64;

        bool implausibleHeightConfirmed =
            false;

        if (heightPlausible)
        {
            ResetImplausibleGroundHeightCandidate();
        }
        else if (!ReferenceEquals(
                     implausibleGroundHeightCandidateAnchor,
                     anchor) ||
                 implausibleGroundHeightCandidateStartedTimestamp ==
                     long.MinValue)
        {
            implausibleGroundHeightCandidateAnchor =
                anchor;

            implausibleGroundHeightCandidateStartedTimestamp =
                now;

            Log.Warn(
                AnchorRecoveryLogTag,
                "Implausible ground height detected; awaiting confirmation: " +
                $"cameraHeight={cameraHeightAboveGroundMeters:F2} m, " +
                $"allowed=[{LocalArNavigationPolicy.MinimumPlausibleCameraHeightAboveGroundMeters:F2}," +
                $"{LocalArNavigationPolicy.MaximumPlausibleCameraHeightAboveGroundMeters:F2}] m, " +
                $"confirmation={ImplausibleGroundHeightConfirmationMilliseconds}ms.");
        }
        else
        {
            implausibleHeightConfirmed =
                now -
                    implausibleGroundHeightCandidateStartedTimestamp >=
                ImplausibleGroundHeightConfirmationMilliseconds;
        }

        bool anchorOutsideLocalWindow =
            float.IsFinite(
                distanceMeters) &&
            distanceMeters >=
                LocalArNavigationPolicy
                    .GroundAnchorRetirementDistanceMeters;

        if (!anchorOutsideLocalWindow &&
            !implausibleHeightConfirmed)
        {
            return false;
        }

        /*
         * Make any delayed stale-anchor worker obsolete before detaching the
         * valid-but-obsolete anchor. This prevents an old worker from acting
         * on the replacement anchor.
         */
        InvalidatePendingRecoveryCountdown();

        ResetImplausibleGroundHeightCandidate();

        ReleaseSpatialGroundAnchor();

        long replacementGeneration;

        lock (groundAnchorRecoveryLock)
        {
            groundAnchorReplacementGeneration++;

            replacementGeneration =
                groundAnchorReplacementGeneration;

            groundAnchorReacquisitionArmed =
                true;

            replacementAnchorSearchStartedTimestamp =
                now;

            replacementAnchorSearchNoticeLogged =
                false;
        }

        hasLoggedGroundPlaneSearch =
            false;

        Log.Warn(
            AnchorRecoveryLogTag,
            "MOVING LOCAL AR FRAME: retired a ground anchor. " +
            $"reason={(anchorOutsideLocalWindow ? "DISTANCE" : "IMPLAUSIBLE_HEIGHT")}, " +
            $"cameraToAnchor={distanceMeters:F2} m, " +
            $"cameraHeight={cameraHeightAboveGroundMeters:F2} m, " +
            $"retirementThreshold=" +
            $"{LocalArNavigationPolicy.GroundAnchorRetirementDistanceMeters:F1} m, " +
            $"replacementGeneration={replacementGeneration}. " +
            "The current geographic route progress is retained while a nearby " +
            "floor anchor is acquired.");

        return true;
    }

    private void ResetImplausibleGroundHeightCandidate()
    {
        implausibleGroundHeightCandidateAnchor =
            null;

        implausibleGroundHeightCandidateStartedTimestamp =
            long.MinValue;
    }

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
            CancellationTokenSource? cancellationToCancel;

            lock (groundAnchorRecoveryLock)
            {
                wasRecovering =
                    groundAnchorReacquisitionArmed ||
                    (groundAnchorRecoveryTask is not null &&
                     !groundAnchorRecoveryTask.IsCompleted);

                groundAnchorReacquisitionArmed =
                    false;

                replacementAnchorSearchStartedTimestamp =
                    long.MinValue;

                replacementAnchorSearchNoticeLogged =
                    false;

                if (wasRecovering)
                {
                    /*
                     * Cancel the delayed stale-anchor worker immediately when
                     * the same anchor naturally resumes TRACKING. Detach the
                     * old worker from shared state before cancellation so a
                     * later independent interruption can schedule immediately;
                     * the old worker's generation check prevents it from
                     * touching any newer recovery.
                     */
                    groundAnchorRecoveryGeneration++;

                    cancellationToCancel =
                        groundAnchorRecoveryCancellation;

                    groundAnchorRecoveryCancellation =
                        null;

                    groundAnchorRecoveryTask =
                        null;
                }
                else
                {
                    cancellationToCancel =
                        null;
                }
            }

            if (wasRecovering &&
                cancellationToCancel is not null)
            {
                try
                {
                    cancellationToCancel.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // Worker completed concurrently.
                }
            }

            if (wasRecovering)
            {
                Log.Debug(
                    AnchorRecoveryLogTag,
                    "Retained/replacement ground anchor naturally returned " +
                    "to TRACKING. Pending stale-anchor replacement cancelled.");
            }

            return false;
        }

        bool anchorStopped =
            anchorTrackingState.Equals(
                "Stopped",
                StringComparison.OrdinalIgnoreCase);

        ScheduleStaleAnchorRecovery(
            anchor,
            anchorTrackingState,
            anchorStopped
                ? 0
                : GroundAnchorRecoveryGraceMilliseconds);

        return groundAnchorReacquisitionArmed;
    }

    private void ScheduleStaleAnchorRecovery(
        Google.AR.Core.Anchor expectedAnchor,
        string observedTrackingState,
        long graceMilliseconds)
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

            if (graceMilliseconds <= 0)
            {
                Log.Warn(
                    AnchorRecoveryLogTag,
                    "ARCore camera is TRACKING but retained ground anchor is " +
                    $"{observedTrackingState}. STOPPED anchors cannot resume; " +
                    "queueing immediate safe replacement.");
            }
            else
            {
                Log.Warn(
                    AnchorRecoveryLogTag,
                    "ARCore camera is TRACKING but retained ground anchor is " +
                    $"{observedTrackingState}. Allowing " +
                    $"{graceMilliseconds} ms for natural anchor relocalization " +
                    "while the renderer keeps the last valid AR placement visible.");
            }

            CancellationTokenSource cancellation =
                new();

            groundAnchorRecoveryCancellation =
                cancellation;

            groundAnchorRecoveryTask =
                RunStaleAnchorRecoveryAsync(
                    expectedAnchor,
                    generation,
                    graceMilliseconds,
                    cancellation);
        }
    }

    private async Task RunStaleAnchorRecoveryAsync(
        Google.AR.Core.Anchor expectedAnchor,
        long generation,
        long graceMilliseconds,
        CancellationTokenSource cancellation)
    {
        CancellationToken cancellationToken =
            cancellation.Token;

        try
        {
            if (graceMilliseconds > 0)
            {
                await Task.Delay(
                    (int)graceMilliseconds,
                    cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            /*
             * Queue behind the current ARCore frame instead of repeatedly
             * timing out from the UI diagnostic thread. SemaphoreSlim will
             * hand the gate to this waiting operation between frame updates.
             */
            await updateGate.WaitAsync(
                cancellationToken);

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
                    "Retained ground anchor is still stale after the continuity " +
                    "grace period: " +
                    $"state={currentState}. Releasing it so the hybrid floor " +
                    "search can reacquire without resetting navigation state.");

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
        catch (OperationCanceledException)
        {
            Log.Debug(
                AnchorRecoveryLogTag,
                "Queued stale-anchor replacement cancelled because tracking " +
                "state changed before replacement was necessary.");
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
                if (ReferenceEquals(
                        groundAnchorRecoveryCancellation,
                        cancellation))
                {
                    groundAnchorRecoveryCancellation =
                        null;

                    groundAnchorRecoveryTask =
                        null;
                }
            }

            cancellation.Dispose();
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
                $"{searchDuration} ms. Recovery is still active. Move slowly " +
                "and keep a well-lit, textured floor visible while hybrid " +
                "plane-only acquisition continues.");
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
        CancellationTokenSource? cancellationToCancel;

        lock (groundAnchorRecoveryLock)
        {
            groundAnchorRecoveryGeneration++;

            cancellationToCancel =
                groundAnchorRecoveryCancellation;

            groundAnchorRecoveryCancellation =
                null;

            groundAnchorRecoveryTask =
                null;
        }

        if (cancellationToCancel is null)
        {
            return;
        }

        try
        {
            cancellationToCancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Worker completed concurrently.
        }
    }
}
