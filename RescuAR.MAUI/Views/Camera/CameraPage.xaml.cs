#if ANDROID
using Android.Util;
#endif

using RescuAR;
using RescuAR.AR;
using RescuAR.MAUI.Services;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Location;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;
using RescuAR.App.Services.AreaStatus;
using RescuAR.App.Services.Flood;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.State;
using System.Numerics;

namespace RescuAR.App.Views.Camera
{
    public partial class CameraPage : ContentPage
    {
        private const string ArCoreLogTag =
            "RescuAR-ARCore";

        private const string MldLogTag =
            "RescuAR-MLD";

        private const string RouteLogTag =
            "RescuAR-ARRoute";

        private const string HeadingLogTag =
            "RescuAR-Heading";

        private const string ProgressLogTag =
            "RescuAR-NavProgress";

        private const string PdrLogTag =
            "RescuAR-PDR";

        private const string FusionLogTag =
            "RescuAR-Fusion";

        private const string RerouteLogTag =
            "RescuAR-Reroute";

        private const string TurnLogTag =
            "RescuAR-Turn";

        private const string SafeZoneLogTag =
            "RescuAR-SafeZone";

        private const string EmergencyAlertLogTag =
            "RescuAR-AlertOverlay";

        private const string FloodDepthLogTag =
            "RescuAR-FloodDepth";


        private readonly MyApplication evergineApplication;
        private readonly IArCoreService _arCoreService;
        private readonly MLDARIntegrationService _mldArIntegrationService;
        private readonly ArHeadingAlignmentService _headingAlignmentService;
        private readonly ILocationService _locationService;
        private readonly RouteProgressTracker _routeProgressTracker;
        private readonly PedestrianDeadReckoningService _pdrService;
        private readonly GpsPdrFusionPolicy _gpsPdrFusionPolicy;
        private readonly OffRouteReroutePolicy _offRouteReroutePolicy;
        private readonly PedestrianTurnGuidanceService _turnGuidanceService;
        private readonly SafeZoneConfirmationService _safeZoneConfirmationService;
        private readonly FloodDepthVisualizationService _floodDepthVisualizationService;

        /*
         * GPS and PDR can both update the same monotonic route progress state.
         * Serialize their short tracker + route-publication transactions so a
         * GPS sample cannot publish an older window immediately after a step.
         */
        private readonly object routeProgressFusionSync =
            new();

        private readonly IDispatcherTimer diagnosticTimer;

        private CancellationTokenSource? routeRequestCancellation;
        private CancellationTokenSource? routeProgressCancellation;
        private Task? routeProgressTask;

        private bool routeRequestInProgress;
        private bool destinationEventSubscribed;
        private bool emergencyAdvisoryEventSubscribed;
        private bool emergencyAdvisoryVisible;
        private bool pageIsVisible;

        private DisasterAdvisory? currentEmergencyAdvisory;

        private CancellationTokenSource? emergencyAdvisoryAutoStartCancellation;

        private bool emergencyGuidanceStartInProgress;

        private FloodDepthVisualizationService.FloodVisualizationSnapshot
            currentFloodVisualization =
                FloodDepthVisualizationService.FloodVisualizationSnapshot.Unavailable;

        private int developerFloodDepthSequenceIndex;

        private const int HighSeverityEmergencyAutoStartSeconds =
            5;

        private ArHeadingAlignmentService.HeadingAlignmentResult?
            lastHeadingAlignment;

        private RouteResult? activeRoute;

        private string activeDestinationName =
            string.Empty;

        private GeoCoordinate? activeDestinationCoordinate;

        private double activeMapToArYawDegrees;

        /*
         * TEST SWITCH:
         * Normal navigation baseline. Set true only for deliberate indoor
         * GPS-freeze diagnostics.
         */
        private const bool IndoorRouteTestMode =
            false;

        /*
         * MILESTONE 3 DEVELOPER VALIDATION HARNESS
         *
         * TEMPORARY: keep true only while validating the confirmed-off-route
         * -> Railway reroute -> replacement-route publication pipeline.
         *
         * This does NOT change the real RouteProgressTracker 35 m off-route
         * threshold. It only exposes a test button that injects three policy
         * confirmations while routing from the latest REAL GPS coordinate.
         *
         * Set false after Milestone 3 validation; the button then disappears.
         */
        private const bool EnableDeveloperOffRouteSimulation =
            false;

        /*
         * MILESTONE 3 DEVELOPER TURN-STATE VALIDATION HARNESS
         *
         * This is deliberately separate from natural field validation. It
         * runs controlled synthetic route geometries through the REAL
         * PedestrianTurnGuidanceService so every classifier branch can be
         * exercised without changing the active navigation route.
         *
         * Keep true only while running the classifier validation. Set false
         * afterward; the button then disappears.
         */
        private const bool EnableDeveloperTurnSimulation =
            false;

        /*
         * STAGE 5 DEVELOPER SAFE-ZONE VALIDATION
         *
         * This does NOT change the production 30 m arrival radius or the real
         * evacuation-center destination used for routing. When armed, only
         * SafeZoneConfirmationService evaluation is temporarily pointed at a
         * test coordinate 20 m ahead along the CURRENT active route. The DEV
         * target intentionally starts inside the unchanged 30 m arrival radius,
         * inside the unchanged 30 m production arrival radius. This controlled
         * validation is for the real 3-distinct-GPS-fix confirmation path and UI,
         * not for natural destination-distance validation. Disable after Stage 5 validation.
         */
        private const bool EnableDeveloperSafeZoneValidation =
            true;

        private const double DeveloperSafeZoneTargetAheadMeters =
            20.0;

        /*
         * STAGE 7 DEVELOPER FLOOD-DEPTH VISUALIZATION
         *
         * The production advisory field `water_level` is a river gauge value,
         * not local street depth. This temporary harness renders explicit
         * synthetic LOCAL depth values so the camera UI can be validated
         * without misrepresenting the live advisory data.
         */
        private const bool EnableDeveloperFloodDepthValidation =
            true;

        private static readonly double?[] DeveloperFloodDepthSequenceMeters =
        {
            0.30,
            0.60,
            1.00,
            1.50,
            null
        };

        private const double DeveloperSimulatedCrossTrackMeters =
            50.0;

        private const double DeveloperSimulatedGpsAccuracyMeters =
            5.0;

        /*
         * The moving-window milestone has already been proven. While indoor
         * testing continues, freeze route progress so poor GPS and synthetic
         * test advancement cannot move an otherwise healthy AR route.
         *
         * Set IndoorRouteTestMode=false for real outdoor GPS progress.
         */
        private const bool FreezeRouteProgressDuringIndoorTest =
            true;

        /*
         * PDR MILESTONE 1
         *
         * PDR is intentionally allowed while indoor GPS progress is frozen.
         * This lets us validate physical step -> route progress -> moving AR
         * window without letting poor indoor GPS move the route.
         */
        private const bool EnablePedestrianDeadReckoning =
            true;

        private const double PdrStepLengthMeters =
            0.70;

        /*
         * Direction gating occurs entirely in the retained ARCore world:
         *
         * current ARCore camera forward
         *          vs.
         * current rendered route tangent.
         *
         * This avoids making step acceptance depend on the Earth-referenced
         * heading calibration's stability flag. The existing mapToArYaw still
         * determines how the geographic route is rendered.
         */
        private double? lastPdrHeadingErrorDegrees;

        private double lastPdrStrideScale;

        private GpsPdrFusionPolicy.PdrConfidence lastPdrConfidence =
            GpsPdrFusionPolicy.PdrConfidence.Rejected;

        private GpsPdrFusionPolicy.GpsConfidence lastGpsConfidence =
            GpsPdrFusionPolicy.GpsConfidence.Unavailable;

        private GpsPdrFusionPolicy.GpsFusionAction lastGpsFusionAction =
            GpsPdrFusionPolicy.GpsFusionAction.Ignore;

        private double? lastGpsPdrDivergenceMeters;

        private int lastGpsBackwardConfirmationCount;

        private long acceptedPdrStepCount;

        private long rejectedPdrStepCount;

        private bool dynamicRerouteInProgress;

        private bool lastOffRouteCandidate;

        private int lastOffRouteConfirmationCount;

        private string lastRerouteResult =
            "None";

        private GeoCoordinate? latestGpsCoordinateForDeveloperReroute;

        private double? latestGpsAccuracyForDeveloperReroute;

        private PedestrianTurnGuidanceService.TurnGuidanceSnapshot
            lastTurnGuidance =
                PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable;

        private PedestrianTurnGuidanceService.TurnInstruction
            lastLoggedTurnInstruction =
                PedestrianTurnGuidanceService.TurnInstruction.Continue;

        private int lastLoggedTurnDistanceBucket =
            -1;

        /*
         * STAGE 5 SAFE ZONE CONFIRMATION
         *
         * Arrival requires repeated good-quality GPS fixes that agree with
         * both destination proximity and retained route progress. PDR alone
         * never completes navigation.
         */
        private SafeZoneConfirmationService.SafeZoneDecision
            lastSafeZoneDecision =
                SafeZoneConfirmationService.SafeZoneDecision.Unavailable;

        private bool safeZoneConfirmed;

        private int lastLoggedSafeZoneConfirmationCount =
            -1;

        private bool developerSafeZoneValidationArmed;

        private GeoCoordinate? developerSafeZoneTargetCoordinate;

        private double developerSafeZoneTargetProgressMeters =
            double.NaN;

        private const int IndoorStationaryPollsBeforeSyntheticAdvance =
            3;

        private const double IndoorSyntheticAdvanceMeters =
            1.5;

        private static readonly TimeSpan RouteProgressPollInterval =
            TimeSpan.FromSeconds(
                2);

        private int indoorStationaryPollCount;

        /*
         * Ground-anchor recovery is only armed after this CameraPage has
         * observed at least one valid anchor. Initial floor acquisition is
         * still handled by the existing ARCore frame loop.
         */
        private bool hasObservedGroundAnchor;

        private bool anchorRecoveryInProgress;

        /*
         * Recovery State V6:
         *
         * CameraPage no longer interprets a temporary
         * SpatialSnapshot.Anchor.IsAvailable=false as proof that ARCore has
         * replaced the ground Anchor.
         *
         * The service increments GroundAnchorReplacementGeneration only after
         * it ACTUALLY releases a stale retained Anchor. Only that durable event
         * starts CameraPage's V4/V5 route-rebase phase.
         */
        private long handledGroundAnchorReplacementGeneration;

        private long activeGroundAnchorReplacementGeneration =
            -1;

        /*
         * Anchor-continuity V4:
         *
         * Keep the cyan route's LAST VALID AR-world X/Z start position
         * independent from whichever ARCore ground Anchor currently owns the
         * route root.
         *
         * When a stale Anchor is replaced, the new Anchor may be several
         * meters away because it comes from a new floor hit. V4 compensates
         * for that change instead of making the route jump to the new hit.
         */
        private bool hasRetainedRouteWorldStart;

        private float retainedRouteWorldStartX;
        private float retainedRouteWorldStartZ;

        private long retainedRouteWorldStartRouteVersion =
            -1;

        /*
         * Camera-tab ARCore activation is serialized so repeated MAUI
         * OnAppearing transitions cannot initialize/resume the Session
         * concurrently.
         */
        private readonly SemaphoreSlim arCoreActivationGate =
            new(
                1,
                1);

        private CancellationTokenSource? arCoreAutoStartCancellation;

        private const int ArCoreSurfaceReadyTimeoutMilliseconds =
            3000;

        private const int ArCoreSurfaceReadyPollMilliseconds =
            100;

        private const int ArCoreSurfaceSettleMilliseconds =
            250;

        public CameraPage(
            IArCoreService arCoreService)
        {
            InitializeComponent();

            developerRerouteTestButton.IsVisible =
                EnableDeveloperOffRouteSimulation;

            developerTurnTestButton.IsVisible =
                EnableDeveloperTurnSimulation;

            developerSafeZoneTestButton.IsVisible =
                EnableDeveloperSafeZoneValidation;

            developerFloodDepthTestButton.IsVisible =
                EnableDeveloperFloodDepthValidation;

            this.evergineApplication =
                new MyApplication();

            this.evergineView.Application =
                this.evergineApplication;

            _arCoreService =
                arCoreService;

            handledGroundAnchorReplacementGeneration =
                _arCoreService.GroundAnchorReplacementGeneration;

            _mldArIntegrationService =
                new MLDARIntegrationService();

            _headingAlignmentService =
                new ArHeadingAlignmentService();

            _locationService =
                new MauiLocationService();

            _routeProgressTracker =
                new RouteProgressTracker(
                    indoorTestMode:
                        IndoorRouteTestMode);

            _pdrService =
                new PedestrianDeadReckoningService();

            _gpsPdrFusionPolicy =
                new GpsPdrFusionPolicy();

            _offRouteReroutePolicy =
                new OffRouteReroutePolicy();

            _turnGuidanceService =
                new PedestrianTurnGuidanceService();

            _safeZoneConfirmationService =
                new SafeZoneConfirmationService();

            _floodDepthVisualizationService =
                new FloodDepthVisualizationService();

            _pdrService.StepDetected +=
                OnPdrStepDetected;

            diagnosticTimer =
                Dispatcher.CreateTimer();

            diagnosticTimer.Interval =
                TimeSpan.FromSeconds(
                    1);

            diagnosticTimer.Tick +=
                OnDiagnosticTimerTick;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            pageIsVisible =
                true;

#if ANDROID
            Log.Debug(
                ArCoreLogTag,
                "Camera tab entered.");

            if (IndoorRouteTestMode)
            {
                Log.Warn(
                    ProgressLogTag,
                    FreezeRouteProgressDuringIndoorTest
                        ? "INDOOR ROUTE TEST MODE ENABLED. GPS/synthetic progress is " +
                          "FROZEN for stability. PDR step progress remains enabled when " +
                          "a route is active. Disable IndoorRouteTestMode for outdoor " +
                          "GPS/PDR fusion testing."
                        : "INDOOR ROUTE TEST MODE ENABLED. GPS thresholds are relaxed " +
                          "and controlled synthetic progress may be used after several " +
                          "stationary samples. Disable this before outdoor/production testing.");
            }
#endif

            SubscribeDestinationChanged();
            SubscribeEmergencyAdvisories();

            if (currentFloodVisualization.IsAvailable)
            {
                ApplyFloodVisualization(
                    currentFloodVisualization,
                    "Camera tab re-entered");
            }

            _headingAlignmentService.Start();

            lastHeadingAlignment =
                _headingAlignmentService.LastResult;

            if (!diagnosticTimer.IsRunning)
            {
                diagnosticTimer.Start();
            }

            /*
             * Camera is now a self-starting AR surface:
             *
             * - first visit -> request camera permission if needed, then create
             *   the ARCore Session automatically;
             * - later visits -> resume the retained Session automatically.
             *
             */
            arCoreAutoStartCancellation?.Cancel();
            arCoreAutoStartCancellation?.Dispose();

            arCoreAutoStartCancellation =
                new CancellationTokenSource();

            _ =
                EnsureArCoreActiveAsync(
                    arCoreAutoStartCancellation.Token);
        }

        protected override void OnDisappearing()
        {
            pageIsVisible =
                false;

            arCoreAutoStartCancellation?.Cancel();
            arCoreAutoStartCancellation?.Dispose();

            arCoreAutoStartCancellation =
                null;

            UnsubscribeDestinationChanged();
            UnsubscribeEmergencyAdvisories();
            HideEmergencyAdvisoryOverlay(
                "Camera tab exited",
                restoreTurnGuidance: false);

            SetFloodVisualizationVisibility(
                false,
                "Camera tab exited; retaining last flood context for re-entry");

            if (diagnosticTimer.IsRunning)
            {
                diagnosticTimer.Stop();
            }

            StopRouteProgress(
                "Camera tab exited.");

            Dispatcher.Dispatch(
                () =>
                    turnGuidancePanel.IsVisible =
                        false);

            CancelRouteRequest(
                "Camera tab exited.");

            _headingAlignmentService.Stop();

#if ANDROID
            Log.Debug(
                ArCoreLogTag,
                "Camera tab exited. Releasing ARCore camera.");
#endif

            /*
             * This is the lifecycle behavior that prevents the physical
             * camera/ARCore policy from remaining active on Home/Map/etc.
             * The Session and guidance state are retained for Camera re-entry.
             */
            if (_arCoreService.IsInitialized &&
                !_arCoreService.IsSessionPaused)
            {
                _arCoreService.PauseCameraSession();
            }

#if ANDROID
            Log.Debug(
                RouteLogTag,
                "CameraPage disappearing. AR/route status diagnostics stopped.");
#endif

            base.OnDisappearing();
        }

        /// <summary>
        /// Ensures ARCore is active whenever the Camera tab is visible.
        ///
        /// First visit:
        ///   wait briefly for the Evergine surface/handler to exist,
        ///   request Android camera permission if necessary,
        ///   create a new ARCore Session.
        ///
        /// Later visits:
        ///   resume the retained Session and restart its frame loop.
        ///
        /// The operation is serialized with arCoreActivationGate so repeated
        /// page lifecycle callbacks cannot race automatic startup.
        /// </summary>
        private async Task<bool> EnsureArCoreActiveAsync(
            CancellationToken cancellationToken)
        {
#if ANDROID
            bool gateEntered =
                false;

            try
            {
                await arCoreActivationGate.WaitAsync(
                    cancellationToken);

                gateEntered =
                    true;

                cancellationToken.ThrowIfCancellationRequested();

                if (!pageIsVisible)
                {
                    return false;
                }

                /*
                 * Fast path for the retained ARCore Session. No permission
                 * prompt or heading reset is needed because this is still the
                 * same ARCore world frame.
                 */
                if (_arCoreService.IsInitialized)
                {
                    Log.Debug(
                        ArCoreLogTag,
                        "Camera tab auto-start: resuming retained ARCore Session.");

                    bool resumed =
                        _arCoreService.ResumeCameraSession();

                    Log.Debug(
                        ArCoreLogTag,
                        "Camera tab ARCore resume result = " +
                        $"{resumed}; " +
                        $"paused={_arCoreService.IsSessionPaused}, " +
                        $"frameLoop={_arCoreService.IsFrameLoopRunning}");

                    if (resumed &&
                        pageIsVisible)
                    {
                        if (CanResumeRetainedRoute())
                        {
                            Log.Debug(
                                MldLogTag,
                                "Camera re-entry is using the retained MLD route. " +
                                "No new Railway request and no route-progress reset.");

                            StartRouteProgress();
                        }
                        else
                        {
                            Log.Debug(
                                MldLogTag,
                                "Camera re-entry has no compatible retained route. " +
                                "Requesting MLD route for the current destination.");

                            StartRouteRequestIfPossible();
                        }
                    }
                    else if (!resumed &&
                             pageIsVisible)
                    {
                        Log.Error(
                            ArCoreLogTag,
                            "Retained ARCore Session could not be resumed.");

                        await DisplayAlert(
                            "AR Camera",
                            "The AR camera could not be resumed. Leave the Camera tab and try again.",
                            "OK");
                    }

                    return resumed;
                }

                Log.Debug(
                    ArCoreLogTag,
                    "Camera tab auto-start: first ARCore Session is not yet " +
                    "initialized. Waiting for the Evergine camera surface.");

                await WaitForArCoreSurfaceReadyAsync(
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (!pageIsVisible)
                {
                    Log.Debug(
                        ArCoreLogTag,
                        "Automatic ARCore initialization cancelled because " +
                        "Camera tab is no longer visible.");

                    return false;
                }

                PermissionStatus permissionStatus =
                    await Permissions.CheckStatusAsync<
                        Permissions.Camera>();

                if (permissionStatus !=
                    PermissionStatus.Granted)
                {
                    Log.Debug(
                        ArCoreLogTag,
                        "Camera tab auto-start requesting Android camera permission.");

                    permissionStatus =
                        await Permissions.RequestAsync<
                            Permissions.Camera>();
                }

                Log.Debug(
                    ArCoreLogTag,
                    $"Camera permission status: {permissionStatus}");

                cancellationToken.ThrowIfCancellationRequested();

                if (permissionStatus !=
                    PermissionStatus.Granted)
                {
                    Log.Error(
                        ArCoreLogTag,
                        "Automatic ARCore initialization stopped because " +
                        "camera permission was not granted.");

                    if (pageIsVisible)
                    {
                        await DisplayAlert(
                            "Camera Permission Required",
                            "Camera permission is required to start AR navigation.",
                            "OK");
                    }

                    return false;
                }

                if (!pageIsVisible)
                {
                    return false;
                }

                /*
                 * A newly created Session establishes a new arbitrary ARCore
                 * world yaw. Reset only Session-scoped spatial state here.
                 */
                _headingAlignmentService.ResetSessionCalibration(
                    "creating a new ARCore Session");

                lastHeadingAlignment =
                    null;

                hasObservedGroundAnchor =
                    false;

                anchorRecoveryInProgress =
                    false;

                activeGroundAnchorReplacementGeneration =
                    -1;

                handledGroundAnchorReplacementGeneration =
                    _arCoreService.GroundAnchorReplacementGeneration;

                ResetRouteWorldContinuity(
                    "creating a new ARCore Session");

                ARCameraSpatialController.ResetRouteRootLock(
                    "creating a new ARCore Session");

                Log.Debug(
                    ArCoreLogTag,
                    "Camera tab auto-start: initializing new ARCore Session.");

                bool initialized =
                    _arCoreService.Initialize();

                Log.Debug(
                    ArCoreLogTag,
                    "Automatic ARCore start returned: " +
                    $"{initialized}; " +
                    $"paused={_arCoreService.IsSessionPaused}, " +
                    $"frameLoop={_arCoreService.IsFrameLoopRunning}");

                if (!initialized)
                {
                    Log.Error(
                        ArCoreLogTag,
                        "Automatic ARCore initialization failed.");

                    if (pageIsVisible)
                    {
                        await DisplayAlert(
                            "AR Camera",
                            "ARCore could not be initialized. Check Logcat for RescuAR-ARCore.",
                            "OK");
                    }

                    return false;
                }

                Log.Debug(
                    MldLogTag,
                    "ARCore automatically active. Checking navigation " +
                    "destination for MLD routing.");

                if (pageIsVisible)
                {
                    StartRouteRequestIfPossible();
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                Log.Debug(
                    ArCoreLogTag,
                    "Camera-tab ARCore auto-start cancelled.");

                return false;
            }
            catch (Exception exception)
            {
                Log.Error(
                    ArCoreLogTag,
                    $"Camera-tab ARCore activation failed: {exception}");

                if (pageIsVisible)
                {
                    await DisplayAlert(
                        "AR Camera",
                        "The AR camera could not be started. Check Logcat for RescuAR-ARCore.",
                        "OK");
                }

                return false;
            }
            finally
            {
                if (gateEntered)
                {
                    arCoreActivationGate.Release();
                }
            }
#else
            await Task.CompletedTask;
            return false;
#endif
        }

        /// <summary>
        /// OnAppearing can occur before Evergine has finished attaching its
        /// native handler and receiving a usable viewport size. Waiting here
        /// replaces the human delay that previously occurred before tapping
        /// the Initialize ARCore button.
        /// </summary>
        private async Task WaitForArCoreSurfaceReadyAsync(
            CancellationToken cancellationToken)
        {
#if ANDROID
            int elapsedMilliseconds =
                0;

            while (elapsedMilliseconds <
                   ArCoreSurfaceReadyTimeoutMilliseconds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool handlerReady =
                    evergineView.Handler is not null;

                bool sizeReady =
                    evergineView.Width >
                        1.0 &&
                    evergineView.Height >
                        1.0;

                if (handlerReady &&
                    sizeReady)
                {
                    Log.Debug(
                        ArCoreLogTag,
                        "Evergine Camera surface ready for automatic ARCore " +
                        $"startup: " +
                        $"{evergineView.Width:F0}x" +
                        $"{evergineView.Height:F0}.");

                    /*
                     * Give the Evergine handler one short settle interval to
                     * create/bind the Vulkan graphics context. This replaces
                     * the manual button's previous natural delay.
                     */
                    await Task.Delay(
                        ArCoreSurfaceSettleMilliseconds,
                        cancellationToken);

                    return;
                }

                await Task.Delay(
                    ArCoreSurfaceReadyPollMilliseconds,
                    cancellationToken);

                elapsedMilliseconds +=
                    ArCoreSurfaceReadyPollMilliseconds;
            }

            /*
             * Do not permanently block initialization if MAUI reports an
             * unusual size/handler lifecycle. ArCoreService still performs
             * its own availability/configuration checks and logs failures.
             */
            Log.Warn(
                ArCoreLogTag,
                "Timed out waiting for the Evergine surface readiness hint. " +
                "Attempting automatic ARCore initialization anyway.");
#else
            await Task.CompletedTask;
#endif
        }

        /// <summary>
        /// Requests a real Railway MLD route only when:
        /// - Camera page is active,
        /// - ARCore has an active (not paused) Session,
        /// - a verified destination has been published.
        /// </summary>
        public async Task<bool> TryRequestMldRouteAsync()
        {
#if ANDROID
            if (!pageIsVisible)
            {
                Log.Debug(
                    MldLogTag,
                    "MLD route request skipped: Camera tab is not active.");

                return false;
            }

            if (!_arCoreService.IsInitialized ||
                _arCoreService.IsSessionPaused)
            {
                Log.Debug(
                    MldLogTag,
                    "MLD route request skipped: ARCore Session is not active.");

                return false;
            }

            if (routeRequestInProgress)
            {
                Log.Debug(
                    MldLogTag,
                    "MLD route request skipped: another request is in progress.");

                return false;
            }

            NavigationDestinationBridge.DestinationSnapshot destination =
                NavigationDestinationBridge.Current;

            if (!destination.IsAvailable)
            {
                Log.Warn(
                    MldLogTag,
                    "MLD route NOT requested: no verified navigation " +
                    "destination has been published. Use " +
                    "CameraNavigationLauncher.OpenAsync(...) from the " +
                    "evacuation-center selection flow.");

                return false;
            }

            routeRequestInProgress =
                true;

            StopRouteProgress(
                "new MLD route request started");

            routeRequestCancellation?.Dispose();

            routeRequestCancellation =
                new CancellationTokenSource();

            CancellationToken cancellationToken =
                routeRequestCancellation.Token;

            try
            {
                Log.Debug(
                    MldLogTag,
                    "Requesting current GPS location for MLD origin through " +
                    "RescuAR location service.");

                if (!await _locationService.EnsurePermissionAsync(
                        cancellationToken))
                {
                    Log.Error(
                        MldLogTag,
                        "MLD route NOT requested: location permission denied.");

                    return false;
                }

                LocationReading? locationReading =
                    await _locationService.GetCurrentLocationAsync(
                        cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (locationReading is null)
                {
                    Log.Error(
                        MldLogTag,
                        "MLD route NOT requested: current GPS location is unavailable.");

                    return false;
                }

                GeoCoordinate origin =
                    locationReading.Coordinate;

                if (!origin.IsValid)
                {
                    Log.Error(
                        MldLogTag,
                        "MLD route NOT requested: current GPS coordinate is invalid.");

                    return false;
                }

                /*
                 * Re-read destination after GPS acquisition in case the user
                 * changed the selected center while location was resolving.
                 */
                destination =
                    NavigationDestinationBridge.Current;

                if (!destination.IsAvailable)
                {
                    Log.Warn(
                        MldLogTag,
                        "MLD route cancelled: destination was cleared.");

                    return false;
                }

                Log.Debug(
                    MldLogTag,
                    "MLD route inputs ready: " +
                    $"origin=({origin.Latitude:F7},{origin.Longitude:F7}), " +
                    $"destination='{destination.Name}', " +
                    $"destinationCoord=(" +
                    $"{destination.Coordinate.Latitude:F7}," +
                    $"{destination.Coordinate.Longitude:F7})");

                Log.Debug(
                    HeadingLogTag,
                    _headingAlignmentService.HasSessionCalibration
                        ? "GPS origin acquired. Reusing retained ARCore-session heading alignment."
                        : "GPS origin acquired. Capturing one-time map-to-AR heading alignment.");

                ArHeadingAlignmentService.HeadingAlignmentResult?
                    headingAlignment =
                        await _headingAlignmentService.CaptureAsync(
                            origin,
                            locationReading.AltitudeMeters,
                            cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                double mapToArYawDegrees;

                if (headingAlignment.HasValue)
                {
                    lastHeadingAlignment =
                        headingAlignment;

                    mapToArYawDegrees =
                        headingAlignment.Value
                            .MapToArYawDegrees;

                    Log.Debug(
                        HeadingLogTag,
                        "Applying heading calibration to MLD route: " +
                        $"mapToArYaw={mapToArYawDegrees:F2} deg, " +
                        $"trueCameraHeading=" +
                        $"{headingAlignment.Value.TrueCameraHeadingDegrees:F2} deg, " +
                        $"arCameraAzimuth=" +
                        $"{headingAlignment.Value.ArCameraAzimuthDegrees:F2} deg.");
                }
                else
                {
                    /*
                     * Preserve the already-working MLD -> AR vertical slice on
                     * devices/environments where Earth-referenced orientation
                     * cannot be acquired. The fallback is explicitly logged
                     * and is NOT considered geographically aligned.
                     */
                    lastHeadingAlignment =
                        null;

                    mapToArYawDegrees =
                        0.0;

                    Log.Warn(
                        HeadingLogTag,
                        "Heading calibration unavailable. Falling back to " +
                        "mapToArYaw=0.0 so route rendering remains functional.");
                }

                RouteResult? route =
                    await _mldArIntegrationService.RequestAndPublishAsync(
                        origin,
                        destination.Coordinate,
                        mapToArYawDegrees:
                            mapToArYawDegrees,
                        cancellationToken:
                            cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (route is null)
                {
                    Log.Warn(
                        MldLogTag,
                        "MLD request completed but returned no route.");

                    return false;
                }

                ARRouteBridge.RouteSnapshot routeSnapshot =
                    ARRouteBridge.Current;

                LogRouteDirectionDiagnostics(
                    route,
                    routeSnapshot,
                    origin,
                    destination.Coordinate,
                    mapToArYawDegrees);

                activeRoute =
                    route;

                activeDestinationName =
                    destination.Name;

                activeDestinationCoordinate =
                    destination.Coordinate;

                activeMapToArYawDegrees =
                    mapToArYawDegrees;

                _routeProgressTracker.SetRoute(
                    route);

                _offRouteReroutePolicy.Reset();

                UpdateTurnGuidance(
                    route,
                    0.0);

#if ANDROID
                CaptureRouteWorldStartContinuity(
                    ARCameraPoseBridge.CurrentFrame);
#endif

                StartRouteProgress();

                Log.Debug(
                    MldLogTag,
                    "MLD route request COMPLETE: " +
                    $"algorithm='{route.Algorithm}', " +
                    $"routePoints={route.Points.Count}, " +
                    $"distance={route.TotalDistanceMeters:F1} m, " +
                    $"bridgeVersion={routeSnapshot.Version}, " +
                    $"bridgePoints={routeSnapshot.Points.Count}, " +
                    $"headingAligned={lastHeadingAlignment.HasValue}, " +
                    $"headingStable={lastHeadingAlignment?.IsStable ?? false}, " +
                    $"mapToArYaw=" +
                    $"{(lastHeadingAlignment?.MapToArYawDegrees ?? 0.0):F2} deg");

                return true;
            }
            catch (OperationCanceledException)
            {
                Log.Debug(
                    MldLogTag,
                    "MLD route request cancelled.");

                return false;
            }
            catch (Exception ex)
            {
                Log.Error(
                    MldLogTag,
                    $"MLD route request FAILED: {ex}");

                return false;
            }
            finally
            {
                routeRequestInProgress =
                    false;
            }
#else
            await Task.CompletedTask;
            return false;
#endif
        }

        /// <summary>
        /// Returns true only when the route retained by this CameraPage still
        /// belongs to the destination currently published by the navigation
        /// flow. This prevents Camera tab re-entry from silently rerouting and
        /// moving the AR geometry when nothing about the trip changed.
        /// </summary>
        private bool CanResumeRetainedRoute()
        {
            if (activeRoute is null ||
                !activeDestinationCoordinate.HasValue)
            {
                return false;
            }

            NavigationDestinationBridge.DestinationSnapshot destination =
                NavigationDestinationBridge.Current;

            if (!destination.IsAvailable)
            {
                return false;
            }

            GeoCoordinate retainedCoordinate =
                activeDestinationCoordinate.Value;

            const double coordinateToleranceDegrees =
                0.0000001;

            bool sameCoordinate =
                Math.Abs(
                    destination.Coordinate.Latitude -
                    retainedCoordinate.Latitude) <=
                    coordinateToleranceDegrees &&
                Math.Abs(
                    destination.Coordinate.Longitude -
                    retainedCoordinate.Longitude) <=
                    coordinateToleranceDegrees;

            bool sameName =
                string.Equals(
                    destination.Name,
                    activeDestinationName,
                    StringComparison.Ordinal);

            return sameCoordinate &&
                sameName;
        }

        private void StartRouteRequestIfPossible()
        {
            if (!pageIsVisible)
            {
                return;
            }

            _ =
                TryRequestMldRouteAsync();
        }

        private void CancelRouteRequest(
            string reason)
        {
            CancellationTokenSource? cancellation =
                routeRequestCancellation;

            routeRequestCancellation =
                null;

            if (cancellation is null)
            {
                return;
            }

#if ANDROID
            Log.Debug(
                MldLogTag,
                $"Cancelling MLD route work: {reason}");
#endif

            try
            {
                cancellation.Cancel();
            }
            catch
            {
                // Best effort.
            }

            cancellation.Dispose();
        }

        private void SubscribeDestinationChanged()
        {
            if (destinationEventSubscribed)
            {
                return;
            }

            NavigationDestinationBridge.DestinationChanged +=
                OnNavigationDestinationChanged;

            destinationEventSubscribed =
                true;
        }

        private void UnsubscribeDestinationChanged()
        {
            if (!destinationEventSubscribed)
            {
                return;
            }

            NavigationDestinationBridge.DestinationChanged -=
                OnNavigationDestinationChanged;

            destinationEventSubscribed =
                false;
        }

        private void OnNavigationDestinationChanged(
            object? sender,
            EventArgs e)
        {
#if ANDROID
            NavigationDestinationBridge.DestinationSnapshot destination =
                NavigationDestinationBridge.Current;

            Log.Debug(
                MldLogTag,
                destination.IsAvailable
                    ? $"Destination changed while Camera is active: '{destination.Name}'."
                    : "Destination cleared while Camera is active.");
#endif

            StopRouteProgress(
                "Navigation destination changed.");

            CancelRouteRequest(
                "Navigation destination changed.");

            activeRoute =
                null;

            activeDestinationName =
                string.Empty;

            activeDestinationCoordinate =
                null;

            indoorStationaryPollCount =
                0;

            lastPdrHeadingErrorDegrees =
                null;

            lastPdrStrideScale =
                0.0;

            lastPdrConfidence =
                GpsPdrFusionPolicy.PdrConfidence.Rejected;

            lastGpsConfidence =
                GpsPdrFusionPolicy.GpsConfidence.Unavailable;

            lastGpsFusionAction =
                GpsPdrFusionPolicy.GpsFusionAction.Ignore;

            lastGpsPdrDivergenceMeters =
                null;

            lastGpsBackwardConfirmationCount =
                0;

            _gpsPdrFusionPolicy.Reset();

            _offRouteReroutePolicy.Reset();

            dynamicRerouteInProgress =
                false;

            lastOffRouteCandidate =
                false;

            lastOffRouteConfirmationCount =
                0;

            lastRerouteResult =
                "None";

            latestGpsCoordinateForDeveloperReroute =
                null;

            latestGpsAccuracyForDeveloperReroute =
                null;

            ResetTurnGuidance();

            ResetSafeZoneConfirmation(
                "navigation destination changed");

            acceptedPdrStepCount =
                0;

            rejectedPdrStepCount =
                0;

            ResetRouteWorldContinuity(
                "navigation destination changed");

            _routeProgressTracker.Clear();

            _mldArIntegrationService.ClearRoute();

            /*
             * Destination changes do not change the retained ARCore world
             * frame. Reuse its existing map-to-AR alignment.
             */
            lastHeadingAlignment =
                _headingAlignmentService.LastResult;

            StartRouteRequestIfPossible();
        }

        private void StartRouteProgress()
        {
            StopRouteProgress(
                "restarting GPS/PDR route-progress tracking");

            if (!pageIsVisible ||
                activeRoute is null)
            {
                return;
            }

            if (safeZoneConfirmed)
            {
#if ANDROID
                Log.Debug(
                    SafeZoneLogTag,
                    "Route-progress restart skipped because safe-zone arrival is already confirmed.");
#endif
                return;
            }

            RouteProgressTracker.ProgressSnapshot retainedProgress =
                _routeProgressTracker.Current;

            UpdateTurnGuidance(
                activeRoute,
                retainedProgress.HasProgress
                    ? retainedProgress.CommittedProgressMeters
                    : 0.0);

            StartPdrIfPossible();

            if (IndoorRouteTestMode &&
                FreezeRouteProgressDuringIndoorTest)
            {
#if ANDROID
                Log.Debug(
                    ProgressLogTag,
                    "Indoor stability mode: GPS/synthetic route progress is " +
                    "intentionally frozen. PDR remains active and may advance " +
                    "the retained route after direction-validated physical steps.");
#endif
                return;
            }

            routeProgressCancellation =
                new CancellationTokenSource();

            CancellationToken cancellationToken =
                routeProgressCancellation.Token;

            routeProgressTask =
                RunRouteProgressLoopAsync(
                    cancellationToken);

#if ANDROID
            Log.Debug(
                ProgressLogTag,
                "GPS route-progress loop started alongside PDR.");
#endif
        }

        private void StartPdrIfPossible()
        {
            if (!EnablePedestrianDeadReckoning ||
                !pageIsVisible ||
                activeRoute is null)
            {
                return;
            }

            try
            {
                bool started =
                    _pdrService.Start();

#if ANDROID
                if (started)
                {
                    Log.Debug(
                        PdrLogTag,
                        "PDR route-progress input ACTIVE: " +
                        $"baseStepLength={PdrStepLengthMeters:F2} m, " +
                        "directionConfidenceBands=HIGH<=25deg, " +
                        "MEDIUM<=45deg, LOW<=60deg, REJECT>60deg, " +
                        $"gpsFrozen=" +
                        $"{(IndoorRouteTestMode && FreezeRouteProgressDuringIndoorTest)}.");
                }
#endif
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Error(
                    PdrLogTag,
                    $"PDR step detector failed to start: {exception}");
#endif
            }
        }

        private void StopRouteProgress(
            string reason)
        {
            _pdrService.Stop();

            CancellationTokenSource? cancellation =
                routeProgressCancellation;

            routeProgressCancellation =
                null;

            routeProgressTask =
                null;

            if (cancellation is null)
            {
                return;
            }

#if ANDROID
            Log.Debug(
                ProgressLogTag,
                $"Stopping GPS route-progress loop: {reason}");
#endif

            try
            {
                cancellation.Cancel();
            }
            catch
            {
                // Best effort.
            }

            cancellation.Dispose();
        }

        private async Task RunRouteProgressLoopAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (safeZoneConfirmed)
                    {
                        await Task.Delay(
                            RouteProgressPollInterval,
                            cancellationToken);

                        continue;
                    }

                    if (!pageIsVisible ||
                        !_arCoreService.IsInitialized ||
                        _arCoreService.IsSessionPaused)
                    {
                        await Task.Delay(
                            RouteProgressPollInterval,
                            cancellationToken);

                        continue;
                    }

                    RouteResult? route =
                        activeRoute;

                    if (route is null)
                    {
                        await Task.Delay(
                            RouteProgressPollInterval,
                            cancellationToken);

                        continue;
                    }

                    LocationReading? reading =
                        await _locationService.GetCurrentLocationAsync(
                            cancellationToken);

                    cancellationToken.ThrowIfCancellationRequested();

                    bool publishedRealProgress =
                        false;

                    if (reading is not null)
                    {
                        lock (routeProgressFusionSync)
                        {
                            latestGpsCoordinateForDeveloperReroute =
                                reading.Coordinate;

                            latestGpsAccuracyForDeveloperReroute =
                                reading.AccuracyMeters;
                        }

                        bool shouldStartDynamicReroute =
                            false;

                        GeoCoordinate rerouteOrigin =
                            default;

                        string rerouteReason =
                            string.Empty;

                        RouteProgressTracker.RouteProgressUpdate?
                            turnGuidanceUpdate =
                                null;

                        SafeZoneConfirmationService.SafeZoneDecision?
                            safeZoneDecision =
                                null;

                        lock (routeProgressFusionSync)
                        {
                            RouteProgressTracker.ProgressSnapshot beforeGps =
                                _routeProgressTracker.Current;

                            RouteProgressTracker.RouteProgressUpdate matchedGps =
                                _routeProgressTracker.Update(
                                    reading.Coordinate,
                                    reading.AccuracyMeters);

                            OffRouteReroutePolicy.OffRouteDecision offRouteDecision =
                                _offRouteReroutePolicy.Evaluate(
                                    matchedGps,
                                    DateTimeOffset.UtcNow);

                            lastOffRouteCandidate =
                                offRouteDecision.IsCandidate;

                            lastOffRouteConfirmationCount =
                                offRouteDecision.ConfirmationCount;

#if ANDROID
                            if (offRouteDecision.IsCandidate)
                            {
                                Log.Warn(
                                    RerouteLogTag,
                                    "OFF-ROUTE GPS candidate: " +
                                    $"confirmation={offRouteDecision.ConfirmationCount}/" +
                                    $"{offRouteDecision.RequiredConfirmationCount}, " +
                                    $"crossTrack={offRouteDecision.CrossTrackErrorMeters:F1} m, " +
                                    $"accuracy=" +
                                    $"{(offRouteDecision.AccuracyMeters.HasValue ? offRouteDecision.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                                    $"reroute={offRouteDecision.ShouldReroute}, " +
                                    $"reason='{offRouteDecision.Reason}'");
                            }
#endif

                            if (offRouteDecision.ShouldReroute)
                            {
                                shouldStartDynamicReroute =
                                    true;

                                rerouteOrigin =
                                    reading.Coordinate;

                                rerouteReason =
                                    offRouteDecision.Reason;
                            }

                            RouteProgressTracker.RouteProgressUpdate fusedGps =
                                matchedGps;

                            if (matchedGps.IsAccepted &&
                                !matchedGps.IsOffRoute)
                            {
                                GpsPdrFusionPolicy.GpsFusionDecision decision =
                                    _gpsPdrFusionPolicy.EvaluateGps(
                                        beforeGps.HasProgress
                                            ? beforeGps.CommittedProgressMeters
                                            : 0.0,
                                        matchedGps);

                                lastGpsConfidence =
                                    decision.Confidence;

                                lastGpsFusionAction =
                                    decision.Action;

                                lastGpsPdrDivergenceMeters =
                                    decision.RawGpsMinusPreviousProgressMeters;

                                lastGpsBackwardConfirmationCount =
                                    decision.BackwardConfirmationCount;

                                if (Math.Abs(
                                        decision.TargetProgressMeters -
                                        matchedGps.CommittedProgressMeters) >
                                    0.001)
                                {
                                    fusedGps =
                                        _routeProgressTracker.ApplyFusionCorrection(
                                            decision.TargetProgressMeters,
                                            matchedGps,
                                            $"GPS_PDR_{decision.Action}");
                                }

                                turnGuidanceUpdate =
                                    fusedGps;

#if ANDROID
                                Log.Debug(
                                    FusionLogTag,
                                    "GPS/PDR fusion: " +
                                    $"confidence={decision.Confidence}, " +
                                    $"action={decision.Action}, " +
                                    $"rawGps={matchedGps.RawProgressMeters:F1} m, " +
                                    $"before=" +
                                    $"{(beforeGps.HasProgress ? beforeGps.CommittedProgressMeters.ToString("F1") : "0.0")} m, " +
                                    $"target={decision.TargetProgressMeters:F1} m, " +
                                    $"gpsMinusPrevious=" +
                                    $"{decision.RawGpsMinusPreviousProgressMeters:F1} m, " +
                                    $"backConfirmations=" +
                                    $"{decision.BackwardConfirmationCount}, " +
                                    $"reason='{decision.Reason}'");
#endif
                            }
                            else
                            {
                                lastGpsConfidence =
                                    GpsPdrFusionPolicy.GpsConfidence.Unavailable;

                                lastGpsFusionAction =
                                    GpsPdrFusionPolicy.GpsFusionAction.Ignore;

                                lastGpsPdrDivergenceMeters =
                                    null;

                                lastGpsBackwardConfirmationCount =
                                    0;
                            }

                            if (fusedGps.IsAccepted &&
                                fusedGps.ShouldPublishWindow)
                            {
                                publishedRealProgress =
                                    TryPublishMovingRouteWindow(
                                        route,
                                        fusedGps,
                                        "GPS/FUSION");
                            }

                            RouteProgressTracker.ProgressSnapshot afterGps =
                                _routeProgressTracker.Current;

                            if (activeDestinationCoordinate.HasValue &&
                                afterGps.HasProgress)
                            {
                                GeoCoordinate safeZoneEvaluationCoordinate =
                                    activeDestinationCoordinate.Value;

                                double safeZoneEvaluationRemainingMeters =
                                    afterGps.RemainingMeters;

                                if (EnableDeveloperSafeZoneValidation &&
                                    developerSafeZoneValidationArmed &&
                                    developerSafeZoneTargetCoordinate.HasValue &&
                                    double.IsFinite(
                                        developerSafeZoneTargetProgressMeters))
                                {
                                    safeZoneEvaluationCoordinate =
                                        developerSafeZoneTargetCoordinate.Value;

                                    safeZoneEvaluationRemainingMeters =
                                        Math.Max(
                                            0.0,
                                            developerSafeZoneTargetProgressMeters -
                                            afterGps.CommittedProgressMeters);
                                }

                                SafeZoneConfirmationService.SafeZoneDecision decision =
                                    _safeZoneConfirmationService.Evaluate(
                                        reading.Coordinate,
                                        safeZoneEvaluationCoordinate,
                                        reading.AccuracyMeters,
                                        safeZoneEvaluationRemainingMeters,
                                        reading.Timestamp);

                                safeZoneDecision =
                                    decision;

                                lastSafeZoneDecision =
                                    decision;

                                /*
                                 * Arrival takes precedence over off-route
                                 * rerouting. Evacuation centers can sit several
                                 * meters away from the road centerline; once
                                 * repeated destination-proximity checks begin,
                                 * do not reroute the user away from the safe
                                 * zone merely because the map match is noisy.
                                 */
                                if (decision.IsCandidate ||
                                    decision.IsConfirmed)
                                {
                                    shouldStartDynamicReroute =
                                        false;

                                    _offRouteReroutePolicy.ResetConfirmation();

                                    lastOffRouteCandidate =
                                        false;

                                    lastOffRouteConfirmationCount =
                                        0;
                                }
                            }
                        }

                        if (safeZoneDecision.HasValue)
                        {
                            HandleSafeZoneDecision(
                                safeZoneDecision.Value);
                        }

                        if (safeZoneConfirmed)
                        {
                            shouldStartDynamicReroute =
                                false;
                        }

                        if (turnGuidanceUpdate.HasValue &&
                            !safeZoneConfirmed)
                        {
                            UpdateTurnGuidance(
                                route,
                                turnGuidanceUpdate.Value
                                    .CommittedProgressMeters);
                        }

                        if (shouldStartDynamicReroute)
                        {
                            StartDynamicRerouteIfPossible(
                                rerouteOrigin,
                                rerouteReason);
                        }

                        if (publishedRealProgress)
                        {
                            indoorStationaryPollCount =
                                0;
                        }
                        else if (IndoorRouteTestMode)
                        {
                            indoorStationaryPollCount++;
                        }
                    }
                    else if (IndoorRouteTestMode)
                    {
                        indoorStationaryPollCount++;
                    }

                    if (IndoorRouteTestMode &&
                        !publishedRealProgress &&
                        indoorStationaryPollCount >=
                            IndoorStationaryPollsBeforeSyntheticAdvance)
                    {
                        RouteProgressTracker.RouteProgressUpdate synthetic;
                        bool publishedSynthetic =
                            false;

                        lock (routeProgressFusionSync)
                        {
                            synthetic =
                                _routeProgressTracker.AdvanceSynthetic(
                                    IndoorSyntheticAdvanceMeters);

                            if (synthetic.IsAccepted)
                            {
                                publishedSynthetic =
                                    TryPublishMovingRouteWindow(
                                        route,
                                        synthetic,
                                        "SYNTHETIC");
                            }
                        }

                        if (synthetic.IsAccepted)
                        {

                            if (publishedSynthetic)
                            {
#if ANDROID
                                Log.Warn(
                                    ProgressLogTag,
                                    "INDOOR TEST moving window advanced synthetically by " +
                                    $"{IndoorSyntheticAdvanceMeters:F1} m. " +
                                    "This validates AR route-window movement only.");
#endif
                                indoorStationaryPollCount =
                                    0;
                            }
                        }
                    }

                    await Task.Delay(
                        RouteProgressPollInterval,
                        cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during Camera-page exit / destination change.
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Error(
                    ProgressLogTag,
                    $"GPS route-progress loop failed: {exception}");
#endif
            }
            finally
            {
#if ANDROID
                Log.Debug(
                    ProgressLogTag,
                    "GPS route-progress loop exited.");
#endif
            }
        }

        private void OnDeveloperTurnTestClicked(
            object? sender,
            EventArgs e)
        {
#if ANDROID
            if (!EnableDeveloperTurnSimulation)
            {
                return;
            }

            developerTurnTestButton.IsEnabled =
                false;

            try
            {
                DeveloperTurnCase[] cases =
                new[]
                {
                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.Continue,
                        0.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.SlightLeft,
                        -40.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.Left,
                        -80.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.SharpLeft,
                        -140.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.SlightRight,
                        40.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.Right,
                        80.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.SharpRight,
                        140.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.UTurn,
                        175.0,
                        false),

                    new DeveloperTurnCase(
                        PedestrianTurnGuidanceService.TurnInstruction.Arrive,
                        0.0,
                        true)
                };

                int passed =
                    0;

                foreach (DeveloperTurnCase testCase in cases)
                {
                    RouteResult syntheticRoute =
                        CreateDeveloperTurnRoute(
                            testCase.TurnAngleDegrees,
                            testCase.ArrivalCase);

                    PedestrianTurnGuidanceService.TurnGuidanceSnapshot result =
                        _turnGuidanceService.Evaluate(
                            syntheticRoute,
                            0.0);

                    bool casePassed =
                        result.IsAvailable &&
                        result.Instruction ==
                            testCase.ExpectedInstruction;

                    if (casePassed)
                    {
                        passed++;
                    }

                    Log.Warn(
                        TurnLogTag,
                        "[DEV TURN] " +
                        $"expected={testCase.ExpectedInstruction}, " +
                        $"actual={result.Instruction}, " +
                        $"inputAngle={testCase.TurnAngleDegrees:F1} deg, " +
                        $"evaluatedAngle={result.TurnAngleDegrees:F1} deg, " +
                        $"distanceToTurn=" +
                        $"{(double.IsFinite(result.DistanceToTurnMeters) ? result.DistanceToTurnMeters.ToString("F1") : "<none>")} m, " +
                        $"text='{result.DisplayText}', " +
                        $"result={(casePassed ? "PASS" : "FAIL")}");
                }

                bool allPassed =
                    passed ==
                        cases.Length;

                Log.Warn(
                    TurnLogTag,
                    "[DEV TURN] CLASSIFIER VALIDATION COMPLETE: " +
                    $"passed={passed}/{cases.Length}, " +
                    $"result={(allPassed ? "PASS" : "FAIL")}. " +
                    "This validates controlled classifier branches only; natural route-turn field validation is still required.");

                Dispatcher.Dispatch(
                    () =>
                    {
                        turnGuidancePanel.IsVisible =
                            true;

                        turnInstructionLabel.Text =
                            allPassed
                                ? "DEV turn test: PASS"
                                : "DEV turn test: FAIL";

                        turnDistanceLabel.Text =
                            $"{passed}/{cases.Length} classifier states";
                    });
            }
            catch (Exception exception)
            {
                Log.Error(
                    TurnLogTag,
                    $"[DEV TURN] Classifier validation failed: {exception}");
            }
            finally
            {
                developerTurnTestButton.IsEnabled =
                    true;
            }
#endif
        }

        private static RouteResult CreateDeveloperTurnRoute(
            double signedTurnAngleDegrees,
            bool arrivalCase)
        {
            GeoCoordinate origin =
                new(
                    14.6500000,
                    121.1000000);

            if (arrivalCase)
            {
                GeoCoordinate arrivalEnd =
                    OffsetDeveloperCoordinate(
                        origin,
                        0.0,
                        5.0);

                return new RouteResult(
                    new[]
                    {
                        new RoutePoint(
                            origin,
                            0.0),
                        new RoutePoint(
                            arrivalEnd,
                            5.0)
                    },
                    5.0,
                    "DEV-TURN-ARRIVAL");
            }

            const double approachMeters =
                15.0;

            const double exitMeters =
                25.0;

            GeoCoordinate corner =
                OffsetDeveloperCoordinate(
                    origin,
                    0.0,
                    approachMeters);

            if (Math.Abs(
                    signedTurnAngleDegrees) <
                0.01)
            {
                GeoCoordinate straightEnd =
                    OffsetDeveloperCoordinate(
                        origin,
                        0.0,
                        approachMeters +
                            exitMeters);

                return new RouteResult(
                    new[]
                    {
                        new RoutePoint(
                            origin,
                            0.0),
                        new RoutePoint(
                            corner,
                            approachMeters),
                        new RoutePoint(
                            straightEnd,
                            approachMeters +
                                exitMeters)
                    },
                    approachMeters +
                        exitMeters,
                    "DEV-TURN-CONTINUE");
            }

            double radians =
                signedTurnAngleDegrees *
                Math.PI /
                180.0;

            double eastMeters =
                Math.Sin(
                    radians) *
                exitMeters;

            double northMeters =
                Math.Cos(
                    radians) *
                exitMeters;

            GeoCoordinate exit =
                OffsetDeveloperCoordinate(
                    corner,
                    eastMeters,
                    northMeters);

            return new RouteResult(
                new[]
                {
                    new RoutePoint(
                        origin,
                        0.0),
                    new RoutePoint(
                        corner,
                        approachMeters),
                    new RoutePoint(
                        exit,
                        approachMeters +
                            exitMeters)
                },
                approachMeters +
                    exitMeters,
                $"DEV-TURN-{signedTurnAngleDegrees:F0}");
        }

        private static GeoCoordinate OffsetDeveloperCoordinate(
            GeoCoordinate origin,
            double eastMeters,
            double northMeters)
        {
            const double metersPerDegreeLatitude =
                111320.0;

            double latitudeRadians =
                origin.Latitude *
                Math.PI /
                180.0;

            double metersPerDegreeLongitude =
                metersPerDegreeLatitude *
                Math.Cos(
                    latitudeRadians);

            return new GeoCoordinate(
                origin.Latitude +
                    northMeters /
                    metersPerDegreeLatitude,
                origin.Longitude +
                    eastMeters /
                    metersPerDegreeLongitude);
        }

        private readonly record struct DeveloperTurnCase(
            PedestrianTurnGuidanceService.TurnInstruction ExpectedInstruction,
            double TurnAngleDegrees,
            bool ArrivalCase);

        private async void OnDeveloperRerouteTestClicked(
            object? sender,
            EventArgs e)
        {
#if ANDROID
            if (!EnableDeveloperOffRouteSimulation)
            {
                return;
            }

            if (!pageIsVisible ||
                activeRoute is null ||
                !activeDestinationCoordinate.HasValue)
            {
                Log.Warn(
                    RerouteLogTag,
                    "[DEV SIM] Reroute simulation ignored: active navigation route is not ready.");

                return;
            }

            if (dynamicRerouteInProgress ||
                routeRequestInProgress)
            {
                Log.Warn(
                    RerouteLogTag,
                    "[DEV SIM] Reroute simulation ignored because route work is already active.");

                return;
            }

            GeoCoordinate? realOrigin;
            double? realAccuracy;
            OffRouteReroutePolicy.OffRouteDecision finalDecision =
                default;

            developerRerouteTestButton.IsEnabled =
                false;

            try
            {
                lock (routeProgressFusionSync)
                {
                    realOrigin =
                        latestGpsCoordinateForDeveloperReroute;

                    realAccuracy =
                        latestGpsAccuracyForDeveloperReroute;

                    if (!realOrigin.HasValue ||
                        !realOrigin.Value.IsValid)
                    {
                        Log.Warn(
                            RerouteLogTag,
                            "[DEV SIM] No valid real GPS fix is available yet. Wait for GPS progress logs, then tap again.");

                        return;
                    }

                    /*
                     * Make the test deterministic. A normal on-route GPS poll
                     * cannot reset the sequence while these three synthetic
                     * policy evaluations execute because the GPS loop uses the
                     * same routeProgressFusionSync lock.
                     */
                    _offRouteReroutePolicy.ResetConfirmation();

                    DateTimeOffset now =
                        DateTimeOffset.UtcNow;

                    for (int confirmation = 1;
                         confirmation <= 3;
                         confirmation++)
                    {
                        finalDecision =
                            _offRouteReroutePolicy.EvaluateDeveloperSimulation(
                                DeveloperSimulatedCrossTrackMeters,
                                DeveloperSimulatedGpsAccuracyMeters,
                                now.AddMilliseconds(
                                    confirmation));

                        lastOffRouteCandidate =
                            finalDecision.IsCandidate;

                        lastOffRouteConfirmationCount =
                            finalDecision.ConfirmationCount;

                        Log.Warn(
                            RerouteLogTag,
                            "[DEV SIM] OFF-ROUTE GPS candidate: " +
                            $"confirmation={finalDecision.ConfirmationCount}/" +
                            $"{finalDecision.RequiredConfirmationCount}, " +
                            $"syntheticCrossTrack={finalDecision.CrossTrackErrorMeters:F1} m, " +
                            $"syntheticAccuracy=" +
                            $"{(finalDecision.AccuracyMeters.HasValue ? finalDecision.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                            $"realGpsAccuracy=" +
                            $"{(realAccuracy.HasValue ? realAccuracy.Value.ToString("F1") : "<unknown>")} m, " +
                            $"reroute={finalDecision.ShouldReroute}, " +
                            $"reason='{finalDecision.Reason}'");
                    }
                }

                if (!realOrigin.HasValue ||
                    !finalDecision.ShouldReroute)
                {
                    lastRerouteResult =
                        "DevSimDidNotTrigger";

                    Log.Error(
                        RerouteLogTag,
                        "[DEV SIM] Expected 3/3 confirmation did not trigger reroute.");

                    return;
                }

                lastRerouteResult =
                    "DevSimTriggered";

                Log.Warn(
                    RerouteLogTag,
                    "[DEV SIM] 3/3 CONFIRMED. Starting REAL Railway reroute from latest GPS origin: " +
                    $"({realOrigin.Value.Latitude:F7},{realOrigin.Value.Longitude:F7}). " +
                    "Only the confirmation is simulated; network routing and AR replacement publication are real.");

                await TryDynamicRerouteAsync(
                    realOrigin.Value,
                    "DEVELOPER_SIMULATION_3_OF_3");
            }
            catch (Exception exception)
            {
                lastRerouteResult =
                    "DevSimFailed";

                Log.Error(
                    RerouteLogTag,
                    $"[DEV SIM] Reroute simulation FAILED: {exception}");
            }
            finally
            {
                developerRerouteTestButton.IsEnabled =
                    true;
            }
#else
            await Task.CompletedTask;
#endif
        }

        private void StartDynamicRerouteIfPossible(
            GeoCoordinate origin,
            string reason)
        {
#if ANDROID
            if (safeZoneConfirmed ||
                !pageIsVisible ||
                !origin.IsValid ||
                !activeDestinationCoordinate.HasValue)
            {
                return;
            }

            if (dynamicRerouteInProgress ||
                routeRequestInProgress)
            {
                Log.Debug(
                    RerouteLogTag,
                    "Dynamic reroute trigger ignored because route work is already active.");

                return;
            }

            _ =
                TryDynamicRerouteAsync(
                    origin,
                    reason);
#endif
        }

        private async Task<bool> TryDynamicRerouteAsync(
            GeoCoordinate origin,
            string reason)
        {
#if ANDROID
            if (safeZoneConfirmed ||
                !pageIsVisible ||
                !activeDestinationCoordinate.HasValue ||
                routeRequestInProgress ||
                dynamicRerouteInProgress)
            {
                return false;
            }

            GeoCoordinate destination =
                activeDestinationCoordinate.Value;

            string destinationName =
                activeDestinationName;

            routeRequestInProgress =
                true;

            dynamicRerouteInProgress =
                true;

            lastRerouteResult =
                "Requesting";

            routeRequestCancellation?.Dispose();

            routeRequestCancellation =
                new CancellationTokenSource();

            CancellationToken cancellationToken =
                routeRequestCancellation.Token;

            try
            {
                Log.Warn(
                    RerouteLogTag,
                    "DYNAMIC REROUTE STARTED: " +
                    $"reason='{reason}', " +
                    $"origin=({origin.Latitude:F7},{origin.Longitude:F7}), " +
                    $"destination='{destinationName}'. " +
                    "The current AR route remains visible until a replacement route is ready.");

                RouteResult? replacementRoute =
                    await _mldArIntegrationService.RequestRouteAsync(
                        origin,
                        destination,
                        cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (replacementRoute is null ||
                    replacementRoute.Points.Count <
                        2)
                {
                    lastRerouteResult =
                        "NoRoute";

                    Log.Warn(
                        RerouteLogTag,
                        "Dynamic reroute returned no usable route. Retaining current guidance.");

                    return false;
                }

                if (!activeDestinationCoordinate.HasValue ||
                    activeDestinationCoordinate.Value !=
                        destination ||
                    !string.Equals(
                        activeDestinationName,
                        destinationName,
                        StringComparison.Ordinal))
                {
                    lastRerouteResult =
                        "DestinationChanged";

                    Log.Debug(
                        RerouteLogTag,
                        "Dynamic reroute discarded because the navigation destination changed.");

                    return false;
                }

                ARCameraPoseBridge.SpatialSnapshot spatial =
                    ARCameraPoseBridge.CurrentFrame;

                if (!spatial.IsTracking ||
                    !spatial.Pose.IsTracking ||
                    !spatial.Anchor.IsAvailable)
                {
                    lastRerouteResult =
                        "WaitingForAR";

                    Log.Warn(
                        RerouteLogTag,
                        "Replacement MLD route is ready, but ARCore/ground anchor is not currently usable. " +
                        "Retaining the existing route; a later confirmed off-route sequence may retry.");

                    return false;
                }

                float arOriginOffsetX =
                    spatial.Pose.PositionX -
                    spatial.Anchor.PositionX;

                float arOriginOffsetZ =
                    spatial.Pose.PositionZ -
                    spatial.Anchor.PositionZ;

                bool published;

                lock (routeProgressFusionSync)
                {
                    published =
                        _mldArIntegrationService.PublishProgressWindow(
                            replacementRoute,
                            0.0,
                            origin,
                            activeMapToArYawDegrees,
                            arOriginOffsetX,
                            arOriginOffsetZ,
                            clearRouteOnFailure:
                                false);

                    if (published)
                    {
                        activeRoute =
                            replacementRoute;

                        _routeProgressTracker.SetRoute(
                            replacementRoute);

                        _routeProgressTracker.MarkWindowPublished(
                            0.0);

                        _gpsPdrFusionPolicy.Reset();

                        _offRouteReroutePolicy.MarkRerouteCompleted(
                            DateTimeOffset.UtcNow);

                        lastOffRouteCandidate =
                            false;

                        lastOffRouteConfirmationCount =
                            0;
                    }
                }

                if (!published)
                {
                    lastRerouteResult =
                        "PublishFailed";

                    Log.Warn(
                        RerouteLogTag,
                        "Replacement route could not be published. Existing AR route retained.");

                    return false;
                }

                CaptureRouteWorldStartContinuity(
                    spatial);

                UpdateTurnGuidance(
                    replacementRoute,
                    0.0);

                lastRerouteResult =
                    "Complete";

                Log.Warn(
                    RerouteLogTag,
                    "DYNAMIC REROUTE COMPLETE: " +
                    $"points={replacementRoute.Points.Count}, " +
                    $"distance={replacementRoute.TotalDistanceMeters:F1} m, " +
                    $"routeVersion={ARRouteBridge.Current.Version}, " +
                    $"arOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m.");

                return true;
            }
            catch (OperationCanceledException)
            {
                lastRerouteResult =
                    "Cancelled";

                Log.Debug(
                    RerouteLogTag,
                    "Dynamic reroute cancelled.");

                return false;
            }
            catch (Exception exception)
            {
                lastRerouteResult =
                    "Failed";

                Log.Error(
                    RerouteLogTag,
                    $"Dynamic reroute FAILED: {exception}");

                return false;
            }
            finally
            {
                dynamicRerouteInProgress =
                    false;

                routeRequestInProgress =
                    false;
            }
#else
            await Task.CompletedTask;
            return false;
#endif
        }

        private void UpdateTurnGuidance(
            RouteResult route,
            double progressMeters)
        {
            PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance =
                _turnGuidanceService.Evaluate(
                    route,
                    progressMeters);

            lastTurnGuidance =
                guidance;

            int distanceBucket =
                guidance.IsAvailable &&
                double.IsFinite(
                    guidance.DistanceToTurnMeters)
                    ? (int)Math.Floor(
                        guidance.DistanceToTurnMeters /
                        5.0)
                    : -1;

#if ANDROID
            if (guidance.IsAvailable &&
                (guidance.Instruction !=
                    lastLoggedTurnInstruction ||
                 distanceBucket !=
                    lastLoggedTurnDistanceBucket))
            {
                Log.Debug(
                    TurnLogTag,
                    "TURN GUIDANCE: " +
                    $"instruction={guidance.Instruction}, " +
                    $"text='{guidance.DisplayText}', " +
                    $"distanceToTurn=" +
                    $"{(double.IsFinite(guidance.DistanceToTurnMeters) ? guidance.DistanceToTurnMeters.ToString("F1") : "<none>")} m, " +
                    $"turnAngle={guidance.TurnAngleDegrees:F1} deg, " +
                    $"remaining={guidance.RemainingRouteMeters:F1} m, " +
                    $"progress={progressMeters:F1} m");

                lastLoggedTurnInstruction =
                    guidance.Instruction;

                lastLoggedTurnDistanceBucket =
                    distanceBucket;
            }
#endif

            Dispatcher.Dispatch(
                () =>
                {
                    if (!guidance.IsAvailable ||
                        emergencyAdvisoryVisible ||
                        safeZoneConfirmed)
                    {
                        turnGuidancePanel.IsVisible =
                            false;

                        return;
                    }

                    turnGuidancePanel.IsVisible =
                        true;

                    turnInstructionLabel.Text =
                        guidance.DisplayText;

                    if (double.IsFinite(
                            guidance.DistanceToTurnMeters))
                    {
                        turnDistanceLabel.Text =
                            $"In {Math.Max(0.0, guidance.DistanceToTurnMeters):F0} m";
                    }
                    else
                    {
                        turnDistanceLabel.Text =
                            $"{Math.Max(0.0, guidance.RemainingRouteMeters):F0} m remaining";
                    }
                });
        }

        private void ResetTurnGuidance()
        {
            lastTurnGuidance =
                PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable;

            lastLoggedTurnInstruction =
                PedestrianTurnGuidanceService.TurnInstruction.Continue;

            lastLoggedTurnDistanceBucket =
                -1;

            Dispatcher.Dispatch(
                () =>
                {
                    turnGuidancePanel.IsVisible =
                        false;

                    turnInstructionLabel.Text =
                        "Continue straight";

                    turnDistanceLabel.Text =
                        string.Empty;
                });
        }

        private void HandleSafeZoneDecision(
            SafeZoneConfirmationService.SafeZoneDecision decision)
        {
#if ANDROID
            if (decision.IsCandidate &&
                decision.ConfirmationCount !=
                    lastLoggedSafeZoneConfirmationCount)
            {
                Log.Info(
                    SafeZoneLogTag,
                    "ARRIVAL CANDIDATE: " +
                    $"confirmation={decision.ConfirmationCount}/" +
                    $"{decision.RequiredConfirmationCount}, " +
                    $"distanceToDestination={decision.DistanceToDestinationMeters:F1} m, " +
                    $"remaining={decision.RemainingRouteMeters:F1} m, " +
                    $"accuracy=" +
                    $"{(decision.AccuracyMeters.HasValue ? decision.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                    $"confirmed={decision.IsConfirmed}, " +
                    $"reason='{decision.Reason}'");
            }
            else if (!decision.IsCandidate &&
                     lastLoggedSafeZoneConfirmationCount >
                         0 &&
                     decision.ConfirmationCount ==
                         0)
            {
                Log.Debug(
                    SafeZoneLogTag,
                    "Arrival confirmation sequence RESET: " +
                    $"distanceToDestination={decision.DistanceToDestinationMeters:F1} m, " +
                    $"remaining={decision.RemainingRouteMeters:F1} m, " +
                    $"accuracy=" +
                    $"{(decision.AccuracyMeters.HasValue ? decision.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                    $"reason='{decision.Reason}'");
            }
#endif

            lastLoggedSafeZoneConfirmationCount =
                decision.ConfirmationCount;

            if (!decision.IsConfirmed ||
                safeZoneConfirmed)
            {
                return;
            }

            safeZoneConfirmed =
                true;

            HideEmergencyAdvisoryOverlay(
                "safe zone confirmed",
                restoreTurnGuidance: false);

            ClearFloodVisualization(
                "safe zone confirmed");

            StopRouteProgress(
                "safe-zone arrival confirmed");

            ResetTurnGuidance();

#if ANDROID
            Log.Info(
                SafeZoneLogTag,
                "SAFE ZONE CONFIRMED: " +
                $"destination='{activeDestinationName}', " +
                $"distanceToDestination={decision.DistanceToDestinationMeters:F1} m, " +
                $"remaining={decision.RemainingRouteMeters:F1} m, " +
                $"accuracy=" +
                $"{(decision.AccuracyMeters.HasValue ? decision.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                $"confirmations={decision.ConfirmationCount}/" +
                $"{decision.RequiredConfirmationCount}.");
#endif

            string destinationName =
                EnableDeveloperSafeZoneValidation &&
                developerSafeZoneValidationArmed
                    ? "DEV Safe Zone Test"
                    : string.IsNullOrWhiteSpace(
                        activeDestinationName)
                        ? "Evacuation Center"
                        : activeDestinationName;

            string accuracyText =
                decision.AccuracyMeters.HasValue
                    ? $" GPS accuracy: {decision.AccuracyMeters.Value:F0} m."
                    : string.Empty;

            Dispatcher.Dispatch(
                () =>
                {
                    turnGuidancePanel.IsVisible =
                        false;

                    safeZoneDestinationLabel.Text =
                        destinationName;

                    safeZoneDetailsLabel.Text =
                        $"Arrival confirmed about " +
                        $"{decision.DistanceToDestinationMeters:F0} m from the destination." +
                        accuracyText;

                    safeZoneConfirmationOverlay.IsVisible =
                        true;
                });
        }

        private void ResetSafeZoneConfirmation(
            string reason)
        {
            bool hadArrivalState =
                safeZoneConfirmed ||
                lastSafeZoneDecision.ConfirmationCount >
                    0;

            _safeZoneConfirmationService.Reset();

            lastSafeZoneDecision =
                SafeZoneConfirmationService.SafeZoneDecision.Unavailable;

            safeZoneConfirmed =
                false;

            lastLoggedSafeZoneConfirmationCount =
                -1;

            developerSafeZoneValidationArmed =
                false;

            developerSafeZoneTargetCoordinate =
                null;

            developerSafeZoneTargetProgressMeters =
                double.NaN;

#if ANDROID
            if (hadArrivalState)
            {
                Log.Debug(
                    SafeZoneLogTag,
                    $"Safe-zone confirmation state reset: {reason}.");
            }
#endif

            Dispatcher.Dispatch(
                () =>
                {
                    safeZoneConfirmationOverlay.IsVisible =
                        false;

                    safeZoneDestinationLabel.Text =
                        "Evacuation Center";

                    safeZoneDetailsLabel.Text =
                        "Arrival confirmed.";

                    if (developerSafeZoneTestButton is not null)
                    {
                        developerSafeZoneTestButton.Text =
                            "DEV: Arm Safe Zone Test";

                        developerSafeZoneTestButton.IsEnabled =
                            true;
                    }
                });
        }

        private void OnDeveloperSafeZoneTestClicked(
            object? sender,
            EventArgs e)
        {
#if ANDROID
            if (!EnableDeveloperSafeZoneValidation ||
                safeZoneConfirmed)
            {
                return;
            }

            RouteResult? route =
                activeRoute;

            RouteProgressTracker.ProgressSnapshot progress =
                _routeProgressTracker.Current;

            if (route is null ||
                route.Points.Count < 2 ||
                !progress.HasProgress)
            {
                Log.Warn(
                    SafeZoneLogTag,
                    "[DEV SAFE ZONE] Cannot arm yet. Wait until a route is active and GPS/PDR route progress is available.");

                return;
            }

            double targetProgressMeters =
                Math.Min(
                    route.TotalDistanceMeters,
                    progress.CommittedProgressMeters +
                    DeveloperSafeZoneTargetAheadMeters);

            if (targetProgressMeters -
                    progress.CommittedProgressMeters <
                DeveloperSafeZoneTargetAheadMeters *
                    0.75)
            {
                Log.Warn(
                    SafeZoneLogTag,
                    "[DEV SAFE ZONE] Active route is too close to its real destination to place the requested test target ahead.");

                return;
            }

            if (!TryGetRouteCoordinateAtProgress(
                    route,
                    targetProgressMeters,
                    out GeoCoordinate targetCoordinate))
            {
                Log.Warn(
                    SafeZoneLogTag,
                    "[DEV SAFE ZONE] Could not interpolate the temporary target coordinate from the active route.");

                return;
            }

            _safeZoneConfirmationService.Reset();

            lastSafeZoneDecision =
                SafeZoneConfirmationService.SafeZoneDecision.Unavailable;

            lastLoggedSafeZoneConfirmationCount =
                -1;

            developerSafeZoneValidationArmed =
                true;

            developerSafeZoneTargetCoordinate =
                targetCoordinate;

            developerSafeZoneTargetProgressMeters =
                targetProgressMeters;

            developerSafeZoneTestButton.Text =
                $"DEV Safe Zone Armed ({DeveloperSafeZoneTargetAheadMeters:F0} m target)";

            developerSafeZoneTestButton.IsEnabled =
                false;

            Log.Warn(
                SafeZoneLogTag,
                "[DEV SAFE ZONE] ARMED: " +
                $"currentProgress={progress.CommittedProgressMeters:F1} m, " +
                $"targetCenterAhead={DeveloperSafeZoneTargetAheadMeters:F1} m, " +
                $"targetProgress={targetProgressMeters:F1} m, " +
                $"target=({targetCoordinate.Latitude:F7},{targetCoordinate.Longitude:F7}), " +
                $"productionArrivalRadius={SafeZoneConfirmationService.ArrivalRadiusMeters:F1} m. " +
                "The DEV target intentionally starts inside the production arrival radius. Stay near the arming point and wait for three DISTINCT qualifying GPS observations; production thresholds remain unchanged.");
#endif
        }

        private static bool TryGetRouteCoordinateAtProgress(
            RouteResult route,
            double progressMeters,
            out GeoCoordinate coordinate)
        {
            coordinate =
                default;

            if (route.Points.Count == 0 ||
                !double.IsFinite(progressMeters))
            {
                return false;
            }

            IReadOnlyList<RoutePoint> points =
                route.Points;

            if (progressMeters <=
                points[0].DistanceFromStartMeters)
            {
                coordinate =
                    points[0].Coordinate;

                return coordinate.IsValid;
            }

            for (int index = 1;
                 index < points.Count;
                 index++)
            {
                RoutePoint previous =
                    points[index - 1];

                RoutePoint current =
                    points[index];

                if (progressMeters >
                    current.DistanceFromStartMeters)
                {
                    continue;
                }

                double segmentDistanceMeters =
                    current.DistanceFromStartMeters -
                    previous.DistanceFromStartMeters;

                if (!double.IsFinite(segmentDistanceMeters) ||
                    segmentDistanceMeters <= 0.001)
                {
                    coordinate =
                        current.Coordinate;

                    return coordinate.IsValid;
                }

                double fraction =
                    Math.Clamp(
                        (progressMeters -
                         previous.DistanceFromStartMeters) /
                        segmentDistanceMeters,
                        0.0,
                        1.0);

                coordinate =
                    new GeoCoordinate(
                        previous.Coordinate.Latitude +
                        ((current.Coordinate.Latitude -
                          previous.Coordinate.Latitude) *
                         fraction),
                        previous.Coordinate.Longitude +
                        ((current.Coordinate.Longitude -
                          previous.Coordinate.Longitude) *
                         fraction));

                return coordinate.IsValid;
            }

            coordinate =
                points[^1].Coordinate;

            return coordinate.IsValid;
        }

        private void ApplyFloodVisualization(
            FloodDepthVisualizationService.FloodVisualizationSnapshot snapshot,
            string reason)
        {
            if (!snapshot.IsAvailable)
            {
                ClearFloodVisualization(reason);
                return;
            }

            currentFloodVisualization =
                snapshot;

            /*
             * Stage 7B: only an explicitly LOCAL flood depth is allowed to
             * become world-space water geometry. River gauge levels and generic
             * advisories remain informational because they do not describe the
             * water depth at the phone.
             *
             * ARFloodDepthBridge crosses into the Evergine draw thread. The
             * renderer then uses the existing frame-coherent ARCore ground
             * anchor and meter scale; the MAUI layer no longer fakes water
             * height using screen pixels.
             */
            bool hasLocalArDepth =
                snapshot.Mode ==
                    FloodDepthVisualizationService.FloodVisualizationMode.LocalDepth &&
                snapshot.LocalDepthMeters.HasValue &&
                double.IsFinite(
                    snapshot.LocalDepthMeters.Value) &&
                snapshot.LocalDepthMeters.Value >
                    0.0;

            if (hasLocalArDepth)
            {
                ARFloodDepthBridge.PublishLocalDepth(
                    snapshot.LocalDepthMeters!.Value,
                    snapshot.SourceText);
            }
            else
            {
                ARFloodDepthBridge.Clear(
                    $"{snapshot.Mode} has no trusted local street-depth value");
            }

            Dispatcher.Dispatch(
                () =>
                {
                    floodVisualizationTitleLabel.Text =
                        snapshot.Title;

                    floodVisualizationPrimaryLabel.Text =
                        snapshot.PrimaryText;

                    floodVisualizationSecondaryLabel.Text =
                        snapshot.SecondaryText;

                    floodVisualizationSourceLabel.Text =
                        hasLocalArDepth
                            ? snapshot.SourceText +
                              " • AR ground-relative visualization"
                            : snapshot.SourceText;

                    /*
                     * Only the compact information card remains in MAUI. The
                     * actual flood body is rendered in Evergine AR space.
                     */
                    floodVisualizationLayer.IsVisible =
                        pageIsVisible &&
                        !safeZoneConfirmed;
                });

#if ANDROID
            string localDepthText =
                snapshot.LocalDepthMeters.HasValue
                    ? $"{snapshot.LocalDepthMeters.Value:F2}m"
                    : "<none>";

            string riverLevelText =
                snapshot.ReportedRiverLevelMeters.HasValue
                    ? $"{snapshot.ReportedRiverLevelMeters.Value:F1}m"
                    : "<none>";

            Log.Info(
                FloodDepthLogTag,
                "FLOOD VISUALIZATION APPLIED: " +
                $"mode={snapshot.Mode}, " +
                $"localDepth={localDepthText}, " +
                $"reportedRiverLevel={riverLevelText}, " +
                $"arSpaceWater={hasLocalArDepth}, " +
                $"reason='{reason}'.");
#endif
        }

        private void SetFloodVisualizationVisibility(
            bool visible,
            string reason)
        {
            bool hasLocalArDepth =
                currentFloodVisualization.IsAvailable &&
                currentFloodVisualization.Mode ==
                    FloodDepthVisualizationService.FloodVisualizationMode.LocalDepth &&
                currentFloodVisualization.LocalDepthMeters.HasValue &&
                currentFloodVisualization.LocalDepthMeters.Value >
                    0.0;

            if (!visible)
            {
                ARFloodDepthBridge.Clear(
                    reason);
            }
            else if (hasLocalArDepth)
            {
                ARFloodDepthBridge.PublishLocalDepth(
                    currentFloodVisualization.LocalDepthMeters!.Value,
                    currentFloodVisualization.SourceText);
            }

            Dispatcher.Dispatch(
                () =>
                    floodVisualizationLayer.IsVisible =
                        visible &&
                        currentFloodVisualization.IsAvailable &&
                        pageIsVisible &&
                        !safeZoneConfirmed);

#if ANDROID
            Log.Debug(
                FloodDepthLogTag,
                $"Flood visualization visibility={visible}; " +
                $"arSpaceWater={(visible && hasLocalArDepth)}; " +
                $"reason='{reason}'.");
#endif
        }

        private void ClearFloodVisualization(
            string reason)
        {
            bool wasAvailable =
                currentFloodVisualization.IsAvailable;

            currentFloodVisualization =
                FloodDepthVisualizationService.FloodVisualizationSnapshot.Unavailable;

            ARFloodDepthBridge.Clear(
                reason);

            Dispatcher.Dispatch(
                () =>
                    floodVisualizationLayer.IsVisible =
                        false);

#if ANDROID
            if (wasAvailable)
            {
                Log.Debug(
                    FloodDepthLogTag,
                    $"FLOOD VISUALIZATION CLEARED: {reason}.");
            }
#endif
        }

        private void OnDeveloperFloodDepthTestClicked(
            object? sender,
            EventArgs e)
        {
            if (!EnableDeveloperFloodDepthValidation)
            {
                return;
            }

            double? nextDepth =
                DeveloperFloodDepthSequenceMeters[
                    developerFloodDepthSequenceIndex];

            developerFloodDepthSequenceIndex =
                (developerFloodDepthSequenceIndex + 1) %
                DeveloperFloodDepthSequenceMeters.Length;

            if (!nextDepth.HasValue)
            {
                ClearFloodVisualization(
                    "developer flood-depth validation cycled OFF");

                developerFloodDepthTestButton.Text =
                    "DEV: AR Flood Depth 0.30 m";

#if ANDROID
                Log.Info(
                    FloodDepthLogTag,
                    "[DEV FLOOD] visualization OFF.");
#endif
                return;
            }

            FloodDepthVisualizationService.FloodVisualizationSnapshot snapshot =
                _floodDepthVisualizationService.FromLocalDepth(
                    nextDepth.Value,
                    "DEV synthetic local depth",
                    "Camera validation");

            ApplyFloodVisualization(
                snapshot,
                "developer local-depth validation");

            double? followingDepth =
                DeveloperFloodDepthSequenceMeters[
                    developerFloodDepthSequenceIndex];

            developerFloodDepthTestButton.Text =
                followingDepth.HasValue
                    ? $"DEV: AR Flood Depth {followingDepth.Value:F2} m"
                    : "DEV: AR Flood Depth OFF";

#if ANDROID
            Log.Info(
                FloodDepthLogTag,
                "[DEV FLOOD] AR-SPACE LOCAL DEPTH VISUALIZED: " +
                $"depth={nextDepth.Value:F2}m, " +
                $"groundRelative=True, " +
                $"next='{developerFloodDepthTestButton.Text}'.");
#endif
        }

        private void SubscribeEmergencyAdvisories()
        {
            if (emergencyAdvisoryEventSubscribed)
            {
                return;
            }

            RealtimeAdvisoryManager.OnNewAdvisoryPushed +=
                OnNewEmergencyAdvisoryPushed;

            emergencyAdvisoryEventSubscribed =
                true;

            /*
             * CameraPage must not depend on Home having been instantiated.
             * The existing manager is idempotent and starts only one timer.
             */
            RealtimeAdvisoryManager.StartRealtimeListener();

#if ANDROID
            Log.Debug(
                EmergencyAlertLogTag,
                "Camera subscribed to the existing real-time emergency advisory source.");
#endif
        }

        private void UnsubscribeEmergencyAdvisories()
        {
            if (!emergencyAdvisoryEventSubscribed)
            {
                return;
            }

            RealtimeAdvisoryManager.OnNewAdvisoryPushed -=
                OnNewEmergencyAdvisoryPushed;

            emergencyAdvisoryEventSubscribed =
                false;

#if ANDROID
            Log.Debug(
                EmergencyAlertLogTag,
                "Camera unsubscribed from real-time emergency advisories.");
#endif
        }

        private void OnNewEmergencyAdvisoryPushed(
            DisasterAdvisory advisory)
        {
            if (advisory is null ||
                !pageIsVisible ||
                safeZoneConfirmed)
            {
                return;
            }

            FloodDepthVisualizationService.FloodVisualizationSnapshot floodSnapshot =
                _floodDepthVisualizationService.FromAdvisory(
                    advisory);

            if (floodSnapshot.IsAvailable)
            {
                ApplyFloodVisualization(
                    floodSnapshot,
                    "new verified flood advisory");
            }

            ShowEmergencyAdvisoryOverlay(
                advisory);
        }

        private void ShowEmergencyAdvisoryOverlay(
            DisasterAdvisory advisory)
        {
            CancelEmergencyAdvisoryAutoStart(
                "replacing/refreshing emergency advisory");

            currentEmergencyAdvisory =
                advisory;

            emergencyAdvisoryVisible =
                true;

            bool isHighSeverity =
                IsHighSeverityAdvisory(
                    advisory);

            string categoryLabel =
                GetEmergencyCategoryLabel(
                    advisory);

            string severityLabel =
                GetEmergencySeverityLabel(
                    advisory);

            string descriptor =
                $"{categoryLabel} Advisory - {severityLabel} Severity";

            string heroTitle =
                GetEmergencyHeroTitle(
                    advisory);

            string bodyMessage =
                GetEmergencyBodyMessage(
                    advisory);

            bool isFlood =
                IsFloodAdvisory(
                    advisory);

            bool isEarthquake =
                IsEarthquakeAdvisory(
                    advisory);

#if ANDROID
            Log.Info(
                EmergencyAlertLogTag,
                "CAMERA EMERGENCY ADVISORY SHOWN: " +
                $"id='{advisory.Id}', " +
                $"level='{advisory.DisplayAlertLevel}', " +
                $"category='{advisory.Category}', " +
                $"title='{advisory.Title}', " +
                $"highSeverity={isHighSeverity}, " +
                $"autoStartSeconds=" +
                $"{(isHighSeverity ? HighSeverityEmergencyAutoStartSeconds : 0)}.");
#endif

            Dispatcher.Dispatch(
                () =>
                {
                    if (!pageIsVisible ||
                        safeZoneConfirmed ||
                        !IsCurrentEmergencyAdvisory(
                            advisory))
                    {
                        emergencyAdvisoryVisible =
                            false;

                        return;
                    }

                    emergencyAdvisoryTopSeverityLabel.Text =
                        descriptor;

                    emergencyAdvisoryBodySeverityLabel.Text =
                        descriptor;

                    emergencyAdvisoryHeroTitleLabel.Text =
                        heroTitle;

                    emergencyAdvisoryBodyMessageLabel.Text =
                        bodyMessage;

                    ApplyEmergencySeverityTheme(
                        advisory);

                    emergencyFloodIcon.IsVisible =
                        isFlood;

                    emergencyEarthquakeIcon.IsVisible =
                        isEarthquake;

                    emergencyGenericIcon.IsVisible =
                        !isFlood &&
                        !isEarthquake;

                    emergencyAdvisoryCountdownLabel.IsVisible =
                        isHighSeverity;

                    emergencyAdvisoryCountdownLabel.Text =
                        $"Popup will close in {HighSeverityEmergencyAutoStartSeconds} seconds. " +
                        "AR Evacuation Guidance will proceed...";

                    emergencyStartGuidanceButton.IsEnabled =
                        true;

                    emergencyStartGuidanceButton.Text =
                        "Start AR Evacuation Guidance  >";

                    /*
                     * Avoid competing navigation and emergency banners. AR,
                     * GPS, and PDR continue behind the full-screen treatment.
                     */
                    turnGuidancePanel.IsVisible =
                        false;

                    emergencyAdvisoryOverlay.IsVisible =
                        true;
                });

            if (isHighSeverity)
            {
                StartEmergencyAdvisoryAutoStart(
                    advisory);
            }
        }

        private static bool IsModerateSeverityAdvisory(
            DisasterAdvisory advisory)
        {
            string severity =
                advisory.DisplayAlertLevel?.Trim().ToLowerInvariant() ??
                string.Empty;

            return
                severity is "moderate" or
                    "medium" or
                    "warning" or
                    "level 2" or
                    "alarm" ||
                severity.Contains(
                    "moderate",
                    StringComparison.Ordinal) ||
                severity.Contains(
                    "medium",
                    StringComparison.Ordinal) ||
                severity.Contains(
                    "warning",
                    StringComparison.Ordinal) ||
                severity.Contains(
                    "level 2",
                    StringComparison.Ordinal);
        }

        private void ApplyEmergencySeverityTheme(
            DisasterAdvisory advisory)
        {
            /*
             * Match the existing RescuAR advisory palette:
             *
             * Moderate / Warning
             *   normal popup badge background = #FEF3C7
             *   normal popup badge text       = #D97706
             *
             * The AR treatment uses the same amber/orange family with
             * translucency so the live camera remains visible underneath.
             * High/Critical retains the approved red treatment.
             */
            bool isModerate =
                IsModerateSeverityAdvisory(
                    advisory);

            if (isModerate)
            {
                emergencyAdvisoryOverlay.BackgroundColor =
                    Color.FromArgb(
                        "#D0D97706");

                emergencyAdvisoryTopBanner.BackgroundColor =
                    Color.FromArgb(
                        "#D9FEF3C7");

                emergencyAdvisoryTopSeverityLabel.TextColor =
                    Color.FromArgb(
                        "#92400E");

                emergencyAdvisoryBodySeverityLabel.TextColor =
                    Colors.White;

                emergencyStartGuidanceButton.BackgroundColor =
                    Color.FromArgb(
                        "#F59E0B");

#if ANDROID
                Log.Debug(
                    EmergencyAlertLogTag,
                    "Camera emergency advisory theme applied: MODERATE/ORANGE.");
#endif
                return;
            }

            emergencyAdvisoryOverlay.BackgroundColor =
                Color.FromArgb(
                    "#D0B00000");

            emergencyAdvisoryTopBanner.BackgroundColor =
                Color.FromArgb(
                    "#70FF5A5A");

            emergencyAdvisoryTopSeverityLabel.TextColor =
                Color.FromArgb(
                    "#8E1010");

            emergencyAdvisoryBodySeverityLabel.TextColor =
                Colors.White;

            emergencyStartGuidanceButton.BackgroundColor =
                Color.FromArgb(
                    "#FF3B43");

#if ANDROID
            Log.Debug(
                EmergencyAlertLogTag,
                "Camera emergency advisory theme applied: HIGH/DEFAULT RED.");
#endif
        }

        private void StartEmergencyAdvisoryAutoStart(
            DisasterAdvisory advisory)
        {
            CancelEmergencyAdvisoryAutoStart(
                "starting a new high-severity countdown");

            CancellationTokenSource cancellation =
                new();

            emergencyAdvisoryAutoStartCancellation =
                cancellation;

#if ANDROID
            Log.Warn(
                EmergencyAlertLogTag,
                "HIGH-SEVERITY AR AUTO-START ARMED: " +
                $"id='{advisory.Id}', " +
                $"countdown={HighSeverityEmergencyAutoStartSeconds}s.");
#endif

            _ =
                RunEmergencyAdvisoryAutoStartAsync(
                    advisory,
                    cancellation);
        }

        private async Task RunEmergencyAdvisoryAutoStartAsync(
            DisasterAdvisory advisory,
            CancellationTokenSource cancellation)
        {
            try
            {
                for (int secondsRemaining =
                         HighSeverityEmergencyAutoStartSeconds;
                     secondsRemaining > 0;
                     secondsRemaining--)
                {
                    if (!pageIsVisible ||
                        safeZoneConfirmed ||
                        !emergencyAdvisoryVisible ||
                        !IsCurrentEmergencyAdvisory(
                            advisory))
                    {
                        return;
                    }

                    int countdownValue =
                        secondsRemaining;

                    Dispatcher.Dispatch(
                        () =>
                        {
                            if (emergencyAdvisoryVisible &&
                                IsCurrentEmergencyAdvisory(
                                    advisory))
                            {
                                emergencyAdvisoryCountdownLabel.Text =
                                    $"Popup will close in {countdownValue} seconds. " +
                                    "AR Evacuation Guidance will proceed...";
                            }
                        });

#if ANDROID
                    Log.Debug(
                        EmergencyAlertLogTag,
                        "High-severity AR auto-start countdown: " +
                        $"{countdownValue}s remaining, " +
                        $"id='{advisory.Id}'.");
#endif

                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        cancellation.Token);
                }

                cancellation.Token.ThrowIfCancellationRequested();

                if (!pageIsVisible ||
                    safeZoneConfirmed ||
                    !emergencyAdvisoryVisible ||
                    !IsCurrentEmergencyAdvisory(
                        advisory))
                {
                    return;
                }

                if (ReferenceEquals(
                        emergencyAdvisoryAutoStartCancellation,
                        cancellation))
                {
                    emergencyAdvisoryAutoStartCancellation =
                        null;
                }

#if ANDROID
                Log.Warn(
                    EmergencyAlertLogTag,
                    "HIGH-SEVERITY AR AUTO-START COUNTDOWN COMPLETE. " +
                    $"Starting evacuation guidance for advisory id='{advisory.Id}'.");
#endif

                await StartEmergencyArGuidanceAsync(
                    advisory,
                    "automatic high-severity 5-second countdown");
            }
            catch (OperationCanceledException)
            {
#if ANDROID
                Log.Debug(
                    EmergencyAlertLogTag,
                    $"High-severity AR auto-start cancelled for advisory id='{advisory.Id}'.");
#endif
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Error(
                    EmergencyAlertLogTag,
                    $"High-severity AR auto-start failed: {exception}");
#endif
            }
            finally
            {
                if (ReferenceEquals(
                        emergencyAdvisoryAutoStartCancellation,
                        cancellation))
                {
                    emergencyAdvisoryAutoStartCancellation =
                        null;
                }

                cancellation.Dispose();
            }
        }

        private void CancelEmergencyAdvisoryAutoStart(
            string reason)
        {
            CancellationTokenSource? cancellation =
                emergencyAdvisoryAutoStartCancellation;

            emergencyAdvisoryAutoStartCancellation =
                null;

            if (cancellation is null)
            {
                return;
            }

            try
            {
                cancellation.Cancel();
            }
            catch
            {
                // Best effort only.
            }

            cancellation.Dispose();

#if ANDROID
            Log.Debug(
                EmergencyAlertLogTag,
                $"Emergency AR auto-start countdown cancelled: {reason}.");
#endif
        }

        private async void OnEmergencyStartGuidanceClicked(
            object? sender,
            EventArgs e)
        {
            DisasterAdvisory? advisory =
                currentEmergencyAdvisory;

            if (advisory is null)
            {
                return;
            }

            CancelEmergencyAdvisoryAutoStart(
                "user selected Start AR Evacuation Guidance");

            await StartEmergencyArGuidanceAsync(
                advisory,
                "user selected Start AR Evacuation Guidance");
        }

        private async Task<bool> StartEmergencyArGuidanceAsync(
            DisasterAdvisory advisory,
            string trigger)
        {
            if (emergencyGuidanceStartInProgress ||
                !pageIsVisible ||
                safeZoneConfirmed ||
                !IsCurrentEmergencyAdvisory(advisory))
            {
                return false;
            }

            emergencyGuidanceStartInProgress =
                true;

            Dispatcher.Dispatch(
                () =>
                {
                    emergencyStartGuidanceButton.IsEnabled =
                        false;
                });

            try
            {
#if ANDROID
                Log.Warn(
                    EmergencyAlertLogTag,
                    "EMERGENCY AR GUIDANCE START REQUESTED: " +
                    $"trigger='{trigger}', " +
                    $"id='{advisory.Id}', " +
                    $"level='{advisory.DisplayAlertLevel}', " +
                    $"category='{advisory.Category}'.");
#endif

                /*
                 * The user-visible commitment happens immediately. The
                 * emergency screen must close at the end of the five-second
                 * countdown (or as soon as the Start button is pressed), not
                 * after a potentially slow GPS acquisition. Route setup then
                 * continues asynchronously behind the live Camera page.
                 */
                HideEmergencyAdvisoryOverlay(
                    $"AR evacuation guidance proceeding ({trigger})",
                    restoreTurnGuidance: false);

#if ANDROID
                Log.Info(
                    EmergencyAlertLogTag,
                    "EMERGENCY OVERLAY CLOSED; route startup is continuing in the background.");
#endif

                /*
                 * If the user is already navigating to a verified evacuation
                 * destination, never silently replace it. The emergency action
                 * simply resumes/reveals the existing AR guidance.
                 */
                NavigationDestinationBridge.DestinationSnapshot destination =
                    NavigationDestinationBridge.Current;

                if (destination.IsAvailable)
                {
#if ANDROID
                    Log.Info(
                        EmergencyAlertLogTag,
                        "Emergency guidance will retain the existing destination: " +
                        $"'{destination.Name}'.");
#endif

                    if (activeRoute is null &&
                        !routeRequestInProgress)
                    {
                        StartRouteRequestIfPossible();
                    }

                    return true;
                }

                /*
                 * No destination exists yet. Resolve the current position and
                 * reuse the app's existing local evacuation-center repository
                 * to choose the nearest verified center. This keeps Stage 6
                 * dependent on the same destination/MLD pipeline as normal UI
                 * navigation instead of creating a second routing path.
                 */
                if (!await _locationService.EnsurePermissionAsync())
                {
#if ANDROID
                    Log.Warn(
                        EmergencyAlertLogTag,
                        "Emergency AR guidance could not start: location permission was not granted.");
#endif

                    await DisplayAlert(
                        "Location Required",
                        "Location permission is required to start AR evacuation guidance.",
                        "OK");

                    return false;
                }

                /*
                 * Prefer the cached Android/MAUI location first. It normally
                 * returns immediately and is sufficient for choosing the
                 * nearest verified evacuation center. A fresh Best-accuracy
                 * request can take the full 15-second service timeout, which
                 * previously left the emergency overlay appearing frozen even
                 * though the five-second countdown had completed.
                 */
                LocationReading? reading =
                    await _locationService.GetLastKnownLocationAsync();

                bool usedLastKnownLocation =
                    reading is not null &&
                    reading.Coordinate.IsValid &&
                    DateTimeOffset.UtcNow - reading.Timestamp <=
                        TimeSpan.FromMinutes(10) &&
                    (!reading.AccuracyMeters.HasValue ||
                     reading.AccuracyMeters.Value <= 250.0);

                if (!usedLastKnownLocation)
                {
#if ANDROID
                    if (reading is not null)
                    {
                        Log.Debug(
                            EmergencyAlertLogTag,
                            "Cached location is too old/inaccurate for emergency destination selection; requesting a fresh location.");
                    }
#endif

                    reading =
                        await _locationService.GetCurrentLocationAsync();
                }

#if ANDROID
                if (reading is not null &&
                    reading.Coordinate.IsValid)
                {
                    string locationSource =
                        usedLastKnownLocation
                            ? "LAST_KNOWN"
                            : "CURRENT";

                    string accuracyText =
                        reading.AccuracyMeters.HasValue
                            ? $"{reading.AccuracyMeters.Value:F1}m"
                            : "<unknown>";

                    Log.Info(
                        EmergencyAlertLogTag,
                        "Emergency destination selection location acquired: " +
                        $"source='{locationSource}', " +
                        $"accuracy={accuracyText}, " +
                        $"timestamp={reading.Timestamp:O}.");
                }
#endif

                if (reading is null ||
                    !reading.Coordinate.IsValid)
                {
#if ANDROID
                    Log.Warn(
                        EmergencyAlertLogTag,
                        "Emergency AR guidance could not start: no valid GPS location is available.");
#endif

                    await DisplayAlert(
                        "Location Unavailable",
                        "Your location could not be determined. Keep Location enabled and try Start AR Evacuation Guidance again.",
                        "OK");

                    return false;
                }

                /*
                 * The overlay has already been intentionally closed, so the
                 * advisory is no longer current UI state. Do not cancel the
                 * committed navigation request merely because
                 * currentEmergencyAdvisory was cleared by that close.
                 */
                if (!pageIsVisible ||
                    safeZoneConfirmed)
                {
                    return false;
                }

                var nearest =
                    AreaStatusService.Instance.GetNearestEvacuationCenter(
                        reading.Coordinate.Latitude,
                        reading.Coordinate.Longitude);

                RescuAR.App.Models.EvacuationCenter? nearestCenter =
                    nearest.Center;

                if (nearestCenter is null ||
                    !double.IsFinite(nearestCenter.Latitude) ||
                    !double.IsFinite(nearestCenter.Longitude))
                {
#if ANDROID
                    Log.Warn(
                        EmergencyAlertLogTag,
                        "Emergency AR guidance could not start: no verified evacuation center is available.");
#endif

                    await DisplayAlert(
                        "Safe Zone Unavailable",
                        "No verified evacuation center is currently available for AR guidance.",
                        "OK");

                    return false;
                }

                bool destinationPublished =
                    CameraNavigationLauncher.Publish(
                        nearestCenter);

                if (!destinationPublished)
                {
#if ANDROID
                    Log.Warn(
                        EmergencyAlertLogTag,
                        $"Emergency AR guidance destination publish failed for '{nearestCenter.Name}'.");
#endif

                    await DisplayAlert(
                        "Guidance Unavailable",
                        "The nearest evacuation center could not be prepared for AR navigation.",
                        "OK");

                    return false;
                }

#if ANDROID
                Log.Info(
                    EmergencyAlertLogTag,
                    "EMERGENCY AR DESTINATION SELECTED: " +
                    $"name='{nearestCenter.Name}', " +
                    $"distance={nearest.DistanceInMeters:F1} m, " +
                    $"origin=({reading.Coordinate.Latitude:F7},{reading.Coordinate.Longitude:F7}), " +
                    $"destination=({nearestCenter.Latitude:F7},{nearestCenter.Longitude:F7}).");
#endif

                /*
                 * CameraNavigationLauncher.Publish(...) raises
                 * NavigationDestinationBridge.DestinationChanged. CameraPage's
                 * existing handler resets stale navigation state and starts the
                 * normal MLD request automatically.
                 */
#if ANDROID
                Log.Info(
                    EmergencyAlertLogTag,
                    "EMERGENCY AR GUIDANCE STARTED through the existing navigation destination pipeline.");
#endif

                return true;
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Error(
                    EmergencyAlertLogTag,
                    $"Emergency AR guidance start failed: {exception}");
#endif

                if (pageIsVisible)
                {
                    await DisplayAlert(
                        "AR Evacuation Guidance",
                        "AR evacuation guidance could not be started. Please try again.",
                        "OK");
                }

                return false;
            }
            finally
            {
                emergencyGuidanceStartInProgress =
                    false;

                Dispatcher.Dispatch(
                    () =>
                    {
                        if (emergencyAdvisoryVisible)
                        {
                            emergencyStartGuidanceButton.IsEnabled =
                                true;
                        }
                    });
            }
        }

        private void HideEmergencyAdvisoryOverlay(
            string reason,
            bool restoreTurnGuidance = true)
        {
            CancelEmergencyAdvisoryAutoStart(
                reason);

            bool wasVisible =
                emergencyAdvisoryVisible;

            emergencyAdvisoryVisible =
                false;

            currentEmergencyAdvisory =
                null;

            Dispatcher.Dispatch(
                () =>
                {
                    emergencyAdvisoryOverlay.IsVisible =
                        false;

                    emergencyAdvisoryCountdownLabel.IsVisible =
                        false;

                    emergencyStartGuidanceButton.IsEnabled =
                        true;

                    if (!restoreTurnGuidance ||
                        safeZoneConfirmed ||
                        !pageIsVisible ||
                        !lastTurnGuidance.IsAvailable)
                    {
                        return;
                    }

                    turnInstructionLabel.Text =
                        lastTurnGuidance.DisplayText;

                    if (double.IsFinite(
                            lastTurnGuidance.DistanceToTurnMeters))
                    {
                        turnDistanceLabel.Text =
                            $"In {Math.Max(0.0, lastTurnGuidance.DistanceToTurnMeters):F0} m";
                    }
                    else
                    {
                        turnDistanceLabel.Text =
                            $"{Math.Max(0.0, lastTurnGuidance.RemainingRouteMeters):F0} m remaining";
                    }

                    turnGuidancePanel.IsVisible =
                        true;
                });

#if ANDROID
            if (wasVisible)
            {
                Log.Debug(
                    EmergencyAlertLogTag,
                    $"Camera emergency advisory hidden: {reason}.");
            }
#endif
        }

        private void OnEmergencyAdvisoryCloseClicked(
            object? sender,
            EventArgs e)
        {
            HideEmergencyAdvisoryOverlay(
                "user dismissed advisory");
        }

        private bool IsCurrentEmergencyAdvisory(
            DisasterAdvisory advisory,
            bool requireVisible = false)
        {
            if (advisory is null ||
                currentEmergencyAdvisory is null)
            {
                return false;
            }

            if (requireVisible &&
                !emergencyAdvisoryVisible)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(advisory.Id) ||
                !string.IsNullOrWhiteSpace(currentEmergencyAdvisory.Id))
            {
                return string.Equals(
                    advisory.Id,
                    currentEmergencyAdvisory.Id,
                    StringComparison.Ordinal);
            }

            return ReferenceEquals(
                advisory,
                currentEmergencyAdvisory);
        }

        private static bool IsHighSeverityAdvisory(
            DisasterAdvisory advisory)
        {
            static bool IsHighValue(
                string? value)
            {
                string level =
                    value?.Trim() ??
                    string.Empty;

                if (string.IsNullOrWhiteSpace(level))
                {
                    return false;
                }

                return level.Equals(
                           "high",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Equals(
                           "high severity",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Equals(
                           "critical",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Equals(
                           "critical severity",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Equals(
                           "level 3",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Equals(
                           "evacuate",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Equals(
                           "severe",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Equals(
                           "severe severity",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.Contains(
                           "critical",
                           StringComparison.OrdinalIgnoreCase) ||
                       level.StartsWith(
                           "high",
                           StringComparison.OrdinalIgnoreCase);
            }

            return IsHighValue(advisory.Severity) ||
                   IsHighValue(advisory.AlertLevel) ||
                   IsHighValue(advisory.DisplayAlertLevel);
        }

        private static bool IsFloodAdvisory(
            DisasterAdvisory advisory)
        {
            string source =
                $"{advisory.Category} {advisory.Title}";

            return source.Contains(
                       "flood",
                       StringComparison.OrdinalIgnoreCase) ||
                   source.Contains(
                       "river",
                       StringComparison.OrdinalIgnoreCase) ||
                   source.Contains(
                       "inundation",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEarthquakeAdvisory(
            DisasterAdvisory advisory)
        {
            string source =
                $"{advisory.Category} {advisory.Title}";

            return source.Contains(
                       "earthquake",
                       StringComparison.OrdinalIgnoreCase) ||
                   source.Contains(
                       "seismic",
                       StringComparison.OrdinalIgnoreCase) ||
                   source.Contains(
                       "ground shaking",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string GetEmergencyCategoryLabel(
            DisasterAdvisory advisory)
        {
            if (IsFloodAdvisory(advisory))
            {
                return "Flood";
            }

            if (IsEarthquakeAdvisory(advisory))
            {
                return "Earthquake";
            }

            string category =
                advisory.Category?.Trim() ??
                string.Empty;

            return string.IsNullOrWhiteSpace(category)
                ? "Emergency"
                : category;
        }

        private static string GetEmergencySeverityLabel(
            DisasterAdvisory advisory)
        {
            if (IsHighSeverityAdvisory(advisory))
            {
                return "High";
            }

            string level =
                advisory.DisplayAlertLevel.Trim();

            const string severitySuffix =
                " Severity";

            if (level.EndsWith(
                    severitySuffix,
                    StringComparison.OrdinalIgnoreCase))
            {
                level =
                    level[..^severitySuffix.Length]
                        .Trim();
            }

            return string.IsNullOrWhiteSpace(level)
                ? "Standby"
                : level;
        }

        private static string GetEmergencyHeroTitle(
            DisasterAdvisory advisory)
        {
            if (IsFloodAdvisory(advisory))
            {
                return "Flooding\nNearby!";
            }

            if (IsEarthquakeAdvisory(advisory))
            {
                return "Duck, Cover,\nand Hold!";
            }

            string title =
                advisory.Title?.Trim() ??
                string.Empty;

            return string.IsNullOrWhiteSpace(title)
                ? "Emergency Nearby!"
                : title;
        }

        private static string GetEmergencyBodyMessage(
            DisasterAdvisory advisory)
        {
            if (advisory.HasActionPlan)
            {
                return advisory.DisplayActionPlan;
            }

            if (!string.IsNullOrWhiteSpace(advisory.Message) ||
                !string.IsNullOrWhiteSpace(advisory.Description))
            {
                return advisory.DisplayMessage;
            }

            if (IsFloodAdvisory(advisory))
            {
                return "Evacuate to the nearest safe zone when instructed by verified authorities and use AR Evacuation Guidance to assist your route.";
            }

            if (IsEarthquakeAdvisory(advisory))
            {
                return "Stay put while the ground is shaking. Duck, cover, and hold. Proceed with AR Evacuation Guidance only when it is safe to move.";
            }

            return "Follow the latest verified emergency guidance and use AR Evacuation Guidance when evacuation is required.";
        }

        private async void OnFinishNavigationClicked(
            object? sender,
            EventArgs e)
        {
#if ANDROID
            Log.Info(
                SafeZoneLogTag,
                "User finished safe-zone navigation. Clearing the active destination.");
#endif

            NavigationDestinationBridge.Clear();

            try
            {
                if (Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync(
                        "//Home");
                }
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Warn(
                    SafeZoneLogTag,
                    $"Navigation was cleared, but returning to Home failed: {exception.Message}");
#endif
            }
        }

        private void OnPdrStepDetected(
            object? sender,
            PedestrianDeadReckoningService.PdrStepDetectedEventArgs e)
        {
#if ANDROID
            if (!EnablePedestrianDeadReckoning ||
                safeZoneConfirmed ||
                !pageIsVisible ||
                !_arCoreService.IsInitialized ||
                _arCoreService.IsSessionPaused)
            {
                return;
            }

            RouteResult? route =
                activeRoute;

            if (route is null)
            {
                return;
            }

            if (!TryGetPdrDirectionAgreement(
                    out double cameraAzimuthDegrees,
                    out double routeAzimuthDegrees,
                    out double headingErrorDegrees,
                    out string unavailableReason))
            {
                rejectedPdrStepCount++;

                Log.Debug(
                    PdrLogTag,
                    "PDR step held: route-direction validation unavailable. " +
                    $"step={e.StepNumber}, reason={unavailableReason}");

                return;
            }

            lastPdrHeadingErrorDegrees =
                headingErrorDegrees;

            GpsPdrFusionPolicy.PdrConfidenceDecision pdrConfidence =
                _gpsPdrFusionPolicy.EvaluatePdrHeading(
                    headingErrorDegrees);

            lastPdrConfidence =
                pdrConfidence.Confidence;

            lastPdrStrideScale =
                pdrConfidence.StrideScale;

            if (!pdrConfidence.IsAccepted)
            {
                rejectedPdrStepCount++;

                Log.Debug(
                    PdrLogTag,
                    "PDR step REJECTED by confidence gate: " +
                    $"step={e.StepNumber}, " +
                    $"cameraArAzimuth={cameraAzimuthDegrees:F1} deg, " +
                    $"routeArAzimuth={routeAzimuthDegrees:F1} deg, " +
                    $"error={headingErrorDegrees:F1} deg, " +
                    $"confidence={pdrConfidence.Confidence}, " +
                    $"reason='{pdrConfidence.Reason}'.");

                return;
            }

            double fusedStepAdvanceMeters =
                PdrStepLengthMeters *
                pdrConfidence.StrideScale;

            bool published =
                false;

            RouteProgressTracker.RouteProgressUpdate update;

            lock (routeProgressFusionSync)
            {
                update =
                    _routeProgressTracker.AdvanceDeadReckoning(
                        fusedStepAdvanceMeters);

                if (update.IsAccepted &&
                    update.ShouldPublishWindow)
                {
                    published =
                        TryPublishMovingRouteWindow(
                            route,
                            update,
                            "PDR");
                }
            }

            if (!update.IsAccepted)
            {
                rejectedPdrStepCount++;

                Log.Debug(
                    PdrLogTag,
                    "PDR step could not advance route progress: " +
                    $"step={e.StepNumber}, " +
                    $"reason={update.RejectionReason}");

                return;
            }

            acceptedPdrStepCount++;

            UpdateTurnGuidance(
                route,
                update.CommittedProgressMeters);

            Log.Debug(
                PdrLogTag,
                "PDR step ACCEPTED: " +
                $"step={e.StepNumber}, " +
                $"acceptedSteps={acceptedPdrStepCount}, " +
                $"confidence={pdrConfidence.Confidence}, " +
                $"strideScale={pdrConfidence.StrideScale:F2}, " +
                $"advance={fusedStepAdvanceMeters:F2} m, " +
                $"progress={update.CommittedProgressMeters:F1} m, " +
                $"headingError={headingErrorDegrees:F1} deg, " +
                $"publishWindow={published}.");
#endif
        }

        /// <summary>
        /// Compares phone/rear-camera forward direction to the current cyan
        /// route tangent in the SAME ARCore coordinate frame.
        ///
        /// This remains intentionally AR-relative. PDR confidence therefore
        /// does not depend on a second compass reading or on the heading
        /// calibration's IsStable flag.
        /// </summary>
        private bool TryGetPdrDirectionAgreement(
            out double cameraAzimuthDegrees,
            out double routeAzimuthDegrees,
            out double headingErrorDegrees,
            out string unavailableReason)
        {
            cameraAzimuthDegrees =
                0.0;

            routeAzimuthDegrees =
                0.0;

            headingErrorDegrees =
                180.0;

            unavailableReason =
                string.Empty;

            ARCameraPoseBridge.SpatialSnapshot spatial =
                ARCameraPoseBridge.CurrentFrame;

            if (!spatial.IsTracking ||
                !spatial.Pose.IsTracking)
            {
                unavailableReason =
                    "ARCore camera is not tracking";

                return false;
            }

            if (!spatial.Anchor.IsAvailable)
            {
                unavailableReason =
                    "ground anchor is unavailable";

                return false;
            }

            ARRouteBridge.RouteSnapshot route =
                ARRouteBridge.Current;

            if (!route.IsAvailable ||
                route.Points.Count <
                    2)
            {
                unavailableReason =
                    "rendered route tangent is unavailable";

                return false;
            }

            ARCameraPoseBridge.PoseSnapshot pose =
                spatial.Pose;

            Quaternion rotation =
                new(
                    pose.RotationX,
                    pose.RotationY,
                    pose.RotationZ,
                    pose.RotationW);

            float lengthSquared =
                rotation.LengthSquared();

            if (!float.IsFinite(
                    lengthSquared) ||
                lengthSquared <
                    0.0001f)
            {
                unavailableReason =
                    "ARCore camera quaternion is invalid";

                return false;
            }

            rotation =
                Quaternion.Normalize(
                    rotation);

            Vector3 cameraForward =
                Vector3.Transform(
                    new Vector3(
                        0.0f,
                        0.0f,
                        -1.0f),
                    rotation);

            double cameraHorizontalMagnitude =
                Math.Sqrt(
                    cameraForward.X *
                        cameraForward.X +
                    cameraForward.Z *
                        cameraForward.Z);

            if (!double.IsFinite(
                    cameraHorizontalMagnitude) ||
                cameraHorizontalMagnitude <
                    0.10)
            {
                unavailableReason =
                    "phone camera is too close to vertical";

                return false;
            }

            ArHorizontalRoutePoint first =
                route.Points[0];

            ArHorizontalRoutePoint second =
                route.Points[1];

            double routeDeltaX =
                second.X -
                first.X;

            double routeDeltaZ =
                second.Z -
                first.Z;

            double routeHorizontalMagnitude =
                Math.Sqrt(
                    routeDeltaX *
                        routeDeltaX +
                    routeDeltaZ *
                        routeDeltaZ);

            if (!double.IsFinite(
                    routeHorizontalMagnitude) ||
                routeHorizontalMagnitude <
                    0.05)
            {
                unavailableReason =
                    "current route segment is too short";

                return false;
            }

            /*
             * Same AR azimuth convention used by heading alignment:
             *
             * 0° = +Z, 90° = +X.
             */
            cameraAzimuthDegrees =
                Normalize360Degrees(
                    RadiansToDegrees(
                        Math.Atan2(
                            cameraForward.X,
                            cameraForward.Z)));

            routeAzimuthDegrees =
                Normalize360Degrees(
                    RadiansToDegrees(
                        Math.Atan2(
                            routeDeltaX,
                            routeDeltaZ)));

            headingErrorDegrees =
                Math.Abs(
                    NormalizeSignedDegrees(
                        cameraAzimuthDegrees -
                        routeAzimuthDegrees));

            return true;
        }

        private bool TryPublishMovingRouteWindow(
            RouteResult route,
            RouteProgressTracker.RouteProgressUpdate update,
            string progressSource = "GPS")
        {
#if ANDROID
            ARCameraPoseBridge.SpatialSnapshot spatial =
                ARCameraPoseBridge.CurrentFrame;

            bool tracking =
                spatial.IsTracking &&
                spatial.Pose.IsTracking;

            if (!tracking)
            {
                Log.Debug(
                    ProgressLogTag,
                    "Moving route window waiting: ARCore is not tracking.");

                return false;
            }

            if (!spatial.Anchor.IsAvailable)
            {
                Log.Debug(
                    ProgressLogTag,
                    "Moving route window waiting: ground anchor is unavailable.");

                return false;
            }

            /*
             * The route root remains attached to the retained ground anchor.
             * Rebase the short local route window horizontally near the
             * current AR camera position so the guidance moves with the user.
             *
             * GPS and PDR now share this same short-window publisher.
             * Neither source drives the Evergine camera; both only advance
             * route progress and republish route geometry.
             */
            float arOriginOffsetX =
                spatial.Pose.PositionX -
                spatial.Anchor.PositionX;

            float arOriginOffsetZ =
                spatial.Pose.PositionZ -
                spatial.Anchor.PositionZ;

            bool published =
                _mldArIntegrationService.PublishProgressWindow(
                    route,
                    update.CommittedProgressMeters,
                    update.SnappedCoordinate,
                    activeMapToArYawDegrees,
                    arOriginOffsetX,
                    arOriginOffsetZ);

            if (!published)
            {
                return false;
            }

            _routeProgressTracker.MarkWindowPublished(
                update.CommittedProgressMeters);

            CaptureRouteWorldStartContinuity(
                spatial);

            Log.Debug(
                ProgressLogTag,
                $"{progressSource} MOVING WINDOW: " +
                $"segment={update.SegmentIndex}, " +
                $"progress={update.CommittedProgressMeters:F1} m, " +
                $"remaining={update.RemainingMeters:F1} m, " +
                $"crossTrack={update.CrossTrackErrorMeters:F1} m, " +
                $"gpsAccuracy=" +
                $"{(update.AccuracyMeters.HasValue ? update.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                $"arOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m");

            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Stores the cyan route's first point in the retained ARCore
        /// session's WORLD X/Z frame:
        ///
        ///     routeWorldStart = anchorWorld + routeLocalFirstPoint
        ///
        /// This value survives loss/replacement of the ground Anchor and is
        /// the continuity target retained by V4/V5.
        /// </summary>
        private void CaptureRouteWorldStartContinuity(
            ARCameraPoseBridge.SpatialSnapshot spatial)
        {
#if ANDROID
            if (!spatial.IsTracking ||
                !spatial.Pose.IsTracking ||
                !spatial.Anchor.IsAvailable)
            {
                return;
            }

            ARRouteBridge.RouteSnapshot route =
                ARRouteBridge.Current;

            if (!route.IsAvailable ||
                route.Points.Count <
                    1)
            {
                return;
            }

            ArHorizontalRoutePoint firstPoint =
                route.Points[0];

            retainedRouteWorldStartX =
                spatial.Anchor.PositionX +
                firstPoint.X;

            retainedRouteWorldStartZ =
                spatial.Anchor.PositionZ +
                firstPoint.Z;

            retainedRouteWorldStartRouteVersion =
                route.Version;

            hasRetainedRouteWorldStart =
                true;
#endif
        }

        /// <summary>
        /// Clears only the continuity bookmark. This is appropriate when a
        /// genuinely new ARCore Session/world frame or a different navigation
        /// destination is created.
        /// </summary>
        private void ResetRouteWorldContinuity(
            string reason)
        {
            hasRetainedRouteWorldStart =
                false;

            retainedRouteWorldStartX =
                0.0f;

            retainedRouteWorldStartZ =
                0.0f;

            retainedRouteWorldStartRouteVersion =
                -1;

#if ANDROID
            Log.Debug(
                "RescuAR-AnchorRecovery",
                $"Route-world continuity reset: {reason}.");
#endif
        }

        /// <summary>
        /// Calculates the unshifted first X/Z point produced by the same
        /// projection/alignment path used by MLDARIntegrationService.
        ///
        /// Usually this is approximately (0,0), but calculating it explicitly
        /// makes the V4 compensation exact even if the snapped reference and
        /// interpolated route start differ slightly.
        /// </summary>
        private bool TryGetUnshiftedRecoveryFirstPoint(
            RouteResult route,
            double startDistanceMeters,
            GeoCoordinate referenceCoordinate,
            out float firstX,
            out float firstZ)
        {
            firstX =
                0.0f;

            firstZ =
                0.0f;

            IReadOnlyList<LocalRoutePoint> localPoints =
                LocalRouteProjector.ProjectWindow(
                    route,
                    startDistanceMeters,
                    referenceCoordinate);

            if (localPoints.Count <
                1)
            {
                return false;
            }

            IReadOnlyList<ArHorizontalRoutePoint> aligned =
                ArRouteAlignment.Rotate(
                    localPoints,
                    activeMapToArYawDegrees);

            if (aligned.Count <
                1)
            {
                return false;
            }

            firstX =
                aligned[0].X;

            firstZ =
                aligned[0].Z;

            return true;
        }

        /// <summary>
        /// Re-publishes the currently active short route window against the
        /// newly recovered ground anchor.
        ///
        /// The full MLD RouteResult and route-progress state are retained.
        /// Only the local AR X/Z placement is recalculated for the replacement
        /// anchor so navigation does not restart from zero.
        /// </summary>
        private bool TryRebaseRouteAfterAnchorRecovery(
            ARCameraPoseBridge.SpatialSnapshot spatial)
        {
#if ANDROID
            RouteResult? route =
                activeRoute;

            if (route is null ||
                route.Points.Count <
                    2)
            {
                Log.Debug(
                    "RescuAR-AnchorRecovery",
                    "Ground anchor recovered, but no active MLD route exists " +
                    "to rebase.");

                return false;
            }

            if (!spatial.IsTracking ||
                !spatial.Pose.IsTracking ||
                !spatial.Anchor.IsAvailable)
            {
                return false;
            }

            RouteProgressTracker.ProgressSnapshot progress =
                _routeProgressTracker.Current;

            double startDistanceMeters;
            GeoCoordinate referenceCoordinate;

            if (progress.HasProgress &&
                progress.SnappedCoordinate.IsValid)
            {
                startDistanceMeters =
                    progress.CommittedProgressMeters;

                referenceCoordinate =
                    progress.SnappedCoordinate;
            }
            else
            {
                startDistanceMeters =
                    route.Points[0]
                        .DistanceFromStartMeters;

                referenceCoordinate =
                    route.Points[0]
                        .Coordinate;
            }

            float baseFirstX =
                0.0f;

            float baseFirstZ =
                0.0f;

            bool hasBaseFirstPoint =
                TryGetUnshiftedRecoveryFirstPoint(
                    route,
                    startDistanceMeters,
                    referenceCoordinate,
                    out baseFirstX,
                    out baseFirstZ);

            float arOriginOffsetX;
            float arOriginOffsetZ;

            bool continuityApplied =
                hasRetainedRouteWorldStart &&
                hasBaseFirstPoint;

            if (continuityApplied)
            {
                /*
                 * Preserve the PREVIOUS route world start:
                 *
                 * desiredWorldStart
                 *     =
                 * newAnchor
                 * + unshiftedFirstPoint
                 * + compensationOffset
                 *
                 * therefore:
                 *
                 * compensationOffset
                 *     =
                 * desiredWorldStart
                 * - newAnchor
                 * - unshiftedFirstPoint
                 */
                arOriginOffsetX =
                    retainedRouteWorldStartX -
                    spatial.Anchor.PositionX -
                    baseFirstX;

                arOriginOffsetZ =
                    retainedRouteWorldStartZ -
                    spatial.Anchor.PositionZ -
                    baseFirstZ;
            }
            else
            {
                /*
                 * Safe fallback for an extremely early loss where no healthy
                 * route/anchor pair was observed before recovery.
                 */
                arOriginOffsetX =
                    0.0f;

                arOriginOffsetZ =
                    0.0f;

                Log.Warn(
                    "RescuAR-AnchorRecovery",
                    "No complete route-world continuity bookmark was available. " +
                    "Falling back to V3 replacement-anchor-origin rebase.");
            }

            bool published =
                _mldArIntegrationService.PublishProgressWindow(
                    route,
                    startDistanceMeters,
                    referenceCoordinate,
                    activeMapToArYawDegrees,
                    arOriginOffsetX,
                    arOriginOffsetZ);

            if (!published)
            {
                Log.Warn(
                    "RescuAR-AnchorRecovery",
                    "Replacement anchor is valid, but the active route window " +
                    "could not be republished.");

                return false;
            }

            _routeProgressTracker.MarkWindowPublished(
                startDistanceMeters);

            ARRouteBridge.RouteSnapshot recoveredRoute =
                ARRouteBridge.Current;

            string firstPointText =
                recoveredRoute.Points.Count >
                    0
                    ? $"({recoveredRoute.Points[0].X:F2}," +
                      $"{recoveredRoute.Points[0].Z:F2})"
                    : "<none>";

            float recoveredWorldStartX =
                spatial.Anchor.PositionX;

            float recoveredWorldStartZ =
                spatial.Anchor.PositionZ;

            if (recoveredRoute.Points.Count >
                0)
            {
                recoveredWorldStartX +=
                    recoveredRoute.Points[0].X;

                recoveredWorldStartZ +=
                    recoveredRoute.Points[0].Z;
            }

            double continuityErrorMeters =
                continuityApplied
                    ? Math.Sqrt(
                        Math.Pow(
                            recoveredWorldStartX -
                            retainedRouteWorldStartX,
                            2.0) +
                        Math.Pow(
                            recoveredWorldStartZ -
                            retainedRouteWorldStartZ,
                            2.0))
                    : double.NaN;

            Log.Debug(
                "RescuAR-AnchorRecovery",
                continuityApplied
                    ? "Active route window REBASED with V4 WORLD CONTINUITY: " +
                      $"progress={startDistanceMeters:F1} m, " +
                      $"newAnchor=(" +
                      $"{spatial.Anchor.PositionX:F2}," +
                      $"{spatial.Anchor.PositionY:F2}," +
                      $"{spatial.Anchor.PositionZ:F2}), " +
                      $"continuityTarget=(" +
                      $"{retainedRouteWorldStartX:F2}," +
                      $"{retainedRouteWorldStartZ:F2}), " +
                      $"baseFirst=(" +
                      $"{baseFirstX:F2}," +
                      $"{baseFirstZ:F2}), " +
                      $"compensation=(" +
                      $"{arOriginOffsetX:F2}," +
                      $"{arOriginOffsetZ:F2}) m, " +
                      $"firstLocalRoutePoint={firstPointText}, " +
                      $"recoveredWorldStart=(" +
                      $"{recoveredWorldStartX:F2}," +
                      $"{recoveredWorldStartZ:F2}), " +
                      $"continuityError={continuityErrorMeters:F3} m, " +
                      $"routeVersion={recoveredRoute.Version}"
                    : "Active route window REBASED using V3 fallback: " +
                      $"progress={startDistanceMeters:F1} m, " +
                      $"newAnchor=(" +
                      $"{spatial.Anchor.PositionX:F2}," +
                      $"{spatial.Anchor.PositionY:F2}," +
                      $"{spatial.Anchor.PositionZ:F2}), " +
                      "compensation=(0.00,0.00) m, " +
                      $"firstLocalRoutePoint={firstPointText}, " +
                      $"routeVersion={recoveredRoute.Version}");

            /*
             * The newly published route is now the authoritative continuity
             * state for any later recovery.
             */
            CaptureRouteWorldStartContinuity(
                spatial);

            return true;
#else
            return false;
#endif
        }

        private static void LogRouteDirectionDiagnostics(
            RouteResult route,
            ARRouteBridge.RouteSnapshot routeSnapshot,
            GeoCoordinate origin,
            GeoCoordinate destination,
            double mapToArYawDegrees)
        {
#if ANDROID
            if (!TryGetFirstGeographicLegBearing(
                    route,
                    out double geographicFirstLegBearingDegrees))
            {
                Log.Warn(
                    HeadingLogTag,
                    "Route direction diagnostic unavailable: route has no " +
                    "non-zero geographic first leg.");

                return;
            }

            double directDestinationBearingDegrees =
                CalculateInitialBearingDegrees(
                    origin,
                    destination);

            double predictedArAzimuthDegrees =
                Normalize360Degrees(
                    geographicFirstLegBearingDegrees +
                    mapToArYawDegrees);

            if (!TryGetFirstArLegAzimuth(
                    routeSnapshot,
                    out double renderedArAzimuthDegrees))
            {
                Log.Warn(
                    HeadingLogTag,
                    "Route direction diagnostic unavailable: published AR " +
                    "window has no non-zero first leg.");

                return;
            }

            double axisAgreementErrorDegrees =
                NormalizeSignedDegrees(
                    renderedArAzimuthDegrees -
                    predictedArAzimuthDegrees);

            Log.Debug(
                HeadingLogTag,
                "ROUTE DIRECTION: " +
                $"firstLegTrueBearing={geographicFirstLegBearingDegrees:F2} deg, " +
                $"directDestinationBearing={directDestinationBearingDegrees:F2} deg, " +
                $"mapToArYaw={mapToArYawDegrees:F2} deg, " +
                $"predictedArFirstLegAzimuth={predictedArAzimuthDegrees:F2} deg, " +
                $"renderedArFirstLegAzimuth={renderedArAzimuthDegrees:F2} deg, " +
                $"axisAgreementError={axisAgreementErrorDegrees:F2} deg");

            Log.Debug(
                HeadingLogTag,
                "The cyan arrow points along the FIRST LOCAL ROUTE LEG, not " +
                "directly at the evacuation center. The visible window is " +
                "only the nearby route section.");
#endif
        }

        private static bool TryGetFirstGeographicLegBearing(
            RouteResult route,
            out double bearingDegrees)
        {
            bearingDegrees =
                0.0;

            if (route.Points.Count <
                2)
            {
                return false;
            }

            GeoCoordinate start =
                route.Points[0]
                    .Coordinate;

            for (int i = 1;
                 i < route.Points.Count;
                 i++)
            {
                GeoCoordinate end =
                    route.Points[i]
                        .Coordinate;

                if (start.DistanceTo(
                        end) <
                    0.10)
                {
                    continue;
                }

                bearingDegrees =
                    CalculateInitialBearingDegrees(
                        start,
                        end);

                return true;
            }

            return false;
        }

        private static bool TryGetFirstArLegAzimuth(
            ARRouteBridge.RouteSnapshot routeSnapshot,
            out double azimuthDegrees)
        {
            azimuthDegrees =
                0.0;

            if (!routeSnapshot.IsAvailable ||
                routeSnapshot.Points.Count <
                    2)
            {
                return false;
            }

            var start =
                routeSnapshot.Points[0];

            for (int i = 1;
                 i < routeSnapshot.Points.Count;
                 i++)
            {
                var end =
                    routeSnapshot.Points[i];

                double deltaX =
                    end.X -
                    start.X;

                double deltaZ =
                    end.Z -
                    start.Z;

                double length =
                    Math.Sqrt(
                        deltaX * deltaX +
                        deltaZ * deltaZ);

                if (length <
                    0.01)
                {
                    continue;
                }

                azimuthDegrees =
                    Normalize360Degrees(
                        RadiansToDegrees(
                            Math.Atan2(
                                deltaX,
                                deltaZ)));

                return true;
            }

            return false;
        }

        private static double CalculateInitialBearingDegrees(
            GeoCoordinate from,
            GeoCoordinate to)
        {
            double latitude1 =
                DegreesToRadians(
                    from.Latitude);

            double latitude2 =
                DegreesToRadians(
                    to.Latitude);

            double deltaLongitude =
                DegreesToRadians(
                    to.Longitude -
                    from.Longitude);

            double y =
                Math.Sin(
                    deltaLongitude) *
                Math.Cos(
                    latitude2);

            double x =
                Math.Cos(
                    latitude1) *
                Math.Sin(
                    latitude2) -
                Math.Sin(
                    latitude1) *
                Math.Cos(
                    latitude2) *
                Math.Cos(
                    deltaLongitude);

            return Normalize360Degrees(
                RadiansToDegrees(
                    Math.Atan2(
                        y,
                        x)));
        }

        private static double Normalize360Degrees(
            double degrees)
        {
            double normalized =
                degrees %
                360.0;

            if (normalized <
                0.0)
            {
                normalized +=
                    360.0;
            }

            return normalized;
        }

        private static double NormalizeSignedDegrees(
            double degrees)
        {
            double normalized =
                Normalize360Degrees(
                    degrees);

            if (normalized >
                180.0)
            {
                normalized -=
                    360.0;
            }

            return normalized;
        }

        private static double DegreesToRadians(
            double degrees)
        {
            return degrees *
                Math.PI /
                180.0;
        }

        private static double RadiansToDegrees(
            double radians)
        {
            return radians *
                180.0 /
                Math.PI;
        }

        private void OnDiagnosticTimerTick(
            object? sender,
            EventArgs e)
        {
#if ANDROID
            ARCameraPoseBridge.SpatialSnapshot spatial =
                ARCameraPoseBridge.CurrentFrame;

            ARRouteBridge.RouteSnapshot route =
                ARRouteBridge.Current;

            int activeSegments =
                ARRouteRenderer.ActiveSegmentCount;

            bool tracking =
                spatial.IsTracking &&
                spatial.Pose.IsTracking;

            /*
             * Recovery State V6
             * -----------------
             *
             * A temporary Anchor.IsAvailable=false is NOT replacement recovery.
             *
             * ARCore commonly reports the retained Anchor PAUSED for a short
             * period after camera tracking returns. Recovery V2 already gives
             * that Anchor 1250 ms to relocalize naturally.
             *
             * CameraPage therefore reacts only when the Android recovery
             * service increments GroundAnchorReplacementGeneration, which
             * happens after the stale Anchor has actually been released.
             */
            long serviceReplacementGeneration =
                _arCoreService.GroundAnchorReplacementGeneration;

            if (serviceReplacementGeneration >
                handledGroundAnchorReplacementGeneration)
            {
                if (!anchorRecoveryInProgress ||
                    activeGroundAnchorReplacementGeneration !=
                        serviceReplacementGeneration)
                {
                    anchorRecoveryInProgress =
                        true;

                    activeGroundAnchorReplacementGeneration =
                        serviceReplacementGeneration;

                    Log.Warn(
                        "RescuAR-AnchorRecovery",
                        "V6 actual stale-anchor replacement detected: " +
                        $"replacementGeneration=" +
                        $"{serviceReplacementGeneration}. " +
                        "The natural-relocalization grace period has already " +
                        "finished and the retained Anchor was released. " +
                        "V4/V5 route continuity will be applied when a " +
                        "replacement floor Anchor is TRACKING.");
                }
            }

            if (spatial.Anchor.IsAvailable)
            {
                if (!hasObservedGroundAnchor)
                {
                    hasObservedGroundAnchor =
                        true;
                }

                if (anchorRecoveryInProgress)
                {
                    Log.Debug(
                        "RescuAR-AnchorRecovery",
                        "Replacement ground anchor is TRACKING. Applying " +
                        "V4/V5 world-position continuity: " +
                        $"replacementGeneration=" +
                        $"{activeGroundAnchorReplacementGeneration}.");

                    bool recoveryRouteReady =
                        activeRoute is null ||
                        TryRebaseRouteAfterAnchorRecovery(
                            spatial);

                    if (recoveryRouteReady)
                    {
                        handledGroundAnchorReplacementGeneration =
                            Math.Max(
                                handledGroundAnchorReplacementGeneration,
                                activeGroundAnchorReplacementGeneration);

                        anchorRecoveryInProgress =
                            false;

                        activeGroundAnchorReplacementGeneration =
                            -1;

                        /*
                         * The service can now clear its replacement-search
                         * state. The route was republished only once for the
                         * actual replacement; there was no bridge Clear().
                         */
                        _arCoreService.TryRecoverGroundAnchorIfNeeded();

                        CaptureRouteWorldStartContinuity(
                            spatial);

                        Log.Debug(
                            "RescuAR-AnchorRecovery",
                            "V6 replacement-anchor recovery COMPLETE. " +
                            $"handledReplacementGeneration=" +
                            $"{handledGroundAnchorReplacementGeneration}.");
                    }
                    else
                    {
                        Log.Warn(
                            "RescuAR-AnchorRecovery",
                            "Replacement anchor is TRACKING, but route continuity " +
                            "rebasing has not completed. The existing V5-locked " +
                            "route placement is retained and rebasing will retry " +
                            "on the next diagnostic tick.");
                    }
                }
                else if (!hasRetainedRouteWorldStart)
                {
                    /*
                     * Establish the first continuity bookmark once a healthy
                     * route + ground-anchor pair exists.
                     *
                     * V5 deliberately does NOT recapture this every diagnostic
                     * tick because live Anchor refinement is not navigation
                     * movement.
                     */
                    CaptureRouteWorldStartContinuity(
                        spatial);
                }

                /*
                 * IMPORTANT:
                 * Do NOT call TryRecoverGroundAnchorIfNeeded() merely because
                 * an Anchor is currently healthy. If a queued grace worker is
                 * active, it performs its own final state re-check and will log
                 * natural relocalization without CameraPage disturbing it.
                 */
            }
            else if (hasObservedGroundAnchor)
            {
                /*
                 * This is either:
                 *
                 *  A) temporary retained-anchor PAUSED state during the
                 *     natural 1250 ms relocalization grace period; or
                 *
                 *  B) an actual replacement floor search after the service has
                 *     released the stale Anchor.
                 *
                 * The service owns that distinction. CameraPage does NOT clear
                 * or republish ARRouteBridge here.
                 *
                 * V5 already hides the route whenever tracking/anchor validity
                 * is unavailable while retaining the same route-root X/Z lock.
                 */
                _arCoreService.TryRecoverGroundAnchorIfNeeded();
            }

            /*
             * The bridge can change during an actual replacement rebase.
             * Refresh the diagnostic snapshots so STATUS describes the state
             * after recovery work, not the snapshot captured at timer entry.
             */
            route =
                ARRouteBridge.Current;

            activeSegments =
                ARRouteRenderer.ActiveSegmentCount;

            bool routeShouldBeVisible =
                pageIsVisible &&
                !_arCoreService.IsSessionPaused &&
                tracking &&
                spatial.Anchor.IsAvailable &&
                route.IsAvailable &&
                activeSegments >
                    0;

            RouteProgressTracker.ProgressSnapshot progress =
                _routeProgressTracker.Current;

            Log.Debug(
                RouteLogTag,
                "STATUS: " +
                $"cameraPageActive={pageIsVisible}, " +
                $"sessionPaused={_arCoreService.IsSessionPaused}, " +
                $"frameLoop={_arCoreService.IsFrameLoopRunning}, " +
                $"tracking={tracking}, " +
                $"anchor={spatial.Anchor.IsAvailable}, " +
                $"anchorRecovery={anchorRecoveryInProgress}, " +
                $"replacementGeneration=" +
                $"{_arCoreService.GroundAnchorReplacementGeneration}, " +
                $"handledReplacementGeneration=" +
                $"{handledGroundAnchorReplacementGeneration}, " +
                $"routeContinuity={hasRetainedRouteWorldStart}, " +
                $"destination={NavigationDestinationBridge.Current.IsAvailable}, " +
                $"headingAligned={lastHeadingAlignment.HasValue}, " +
                $"headingStable={lastHeadingAlignment?.IsStable ?? false}, " +
                $"mapToArYaw=" +
                $"{(lastHeadingAlignment?.MapToArYawDegrees ?? 0.0):F1}, " +
                $"routePublished={route.IsAvailable}, " +
                $"routeVersion={route.Version}, " +
                $"routePoints={route.Points.Count}, " +
                $"rendererVersion={ARRouteRenderer.AppliedRouteVersion}, " +
                $"activeSegments={activeSegments}, " +
                $"indoorTest={IndoorRouteTestMode}, " +
                $"gpsProgressFrozen=" +
                $"{(IndoorRouteTestMode && FreezeRouteProgressDuringIndoorTest)}, " +
                $"pdrEnabled={EnablePedestrianDeadReckoning}, " +
                $"pdrRunning={_pdrService.IsRunning}, " +
                $"pdrDetectedSteps={_pdrService.DetectedStepCount}, " +
                $"pdrAcceptedSteps={acceptedPdrStepCount}, " +
                $"pdrRejectedSteps={rejectedPdrStepCount}, " +
                $"pdrHeadingError=" +
                $"{(lastPdrHeadingErrorDegrees.HasValue ? lastPdrHeadingErrorDegrees.Value.ToString("F1") : "<none>")}deg, " +
                $"pdrConfidence={lastPdrConfidence}, " +
                $"pdrStrideScale={lastPdrStrideScale:F2}, " +
                $"gpsConfidence={lastGpsConfidence}, " +
                $"gpsFusionAction={lastGpsFusionAction}, " +
                $"gpsMinusPdr=" +
                $"{(lastGpsPdrDivergenceMeters.HasValue ? lastGpsPdrDivergenceMeters.Value.ToString("F1") : "<none>")}m, " +
                $"gpsBackConfirmations={lastGpsBackwardConfirmationCount}, " +
                $"offRouteCandidate={lastOffRouteCandidate}, " +
                $"offRouteConfirmations={lastOffRouteConfirmationCount}, " +
                $"rerouteInProgress={dynamicRerouteInProgress}, " +
                $"rerouteResult={lastRerouteResult}, " +
                $"devRerouteSimulation={EnableDeveloperOffRouteSimulation}, " +
                $"devTurnSimulation={EnableDeveloperTurnSimulation}, " +
                $"turnInstruction=" +
                $"{(lastTurnGuidance.IsAvailable ? lastTurnGuidance.Instruction.ToString() : "<none>")}, " +
                $"turnDistance=" +
                $"{(lastTurnGuidance.IsAvailable && double.IsFinite(lastTurnGuidance.DistanceToTurnMeters) ? lastTurnGuidance.DistanceToTurnMeters.ToString("F1") : "<none>")}m, " +
                $"safeZoneCandidate={lastSafeZoneDecision.IsCandidate}, " +
                $"safeZoneConfirmations={lastSafeZoneDecision.ConfirmationCount}/" +
                $"{lastSafeZoneDecision.RequiredConfirmationCount}, " +
                $"safeZoneConfirmed={safeZoneConfirmed}, " +
                $"safeZoneDistance=" +
                $"{(lastSafeZoneDecision.IsAvailable && double.IsFinite(lastSafeZoneDecision.DistanceToDestinationMeters) ? lastSafeZoneDecision.DistanceToDestinationMeters.ToString("F1") : "<none>")}m, " +
                $"devSafeZoneValidation={EnableDeveloperSafeZoneValidation}, " +
                $"devSafeZoneArmed={developerSafeZoneValidationArmed}, " +
                $"devSafeZoneTargetProgress=" +
                $"{(double.IsFinite(developerSafeZoneTargetProgressMeters) ? developerSafeZoneTargetProgressMeters.ToString("F1") : "<none>")}m, " +
                $"progressActive={progress.HasProgress}, " +
                $"progress={progress.CommittedProgressMeters:F1}m, " +
                $"remaining={progress.RemainingMeters:F1}m, " +
                $"crossTrack=" +
                $"{(double.IsFinite(progress.CrossTrackErrorMeters) ? progress.CrossTrackErrorMeters.ToString("F1") : "<none>")}m, " +
                $"offRoute={progress.IsOffRoute}, " +
                $"routeVisibleExpected={routeShouldBeVisible}");
#endif
        }
    }
}
