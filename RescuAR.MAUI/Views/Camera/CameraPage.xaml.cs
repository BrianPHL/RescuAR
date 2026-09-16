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
using RescuAR.Navigation.Hazards;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.State;
using System.Numerics;
using Microsoft.Maui.Networking;

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

        private const string HazardRerouteLogTag =
            "RescuAR-HazardReroute";

        [System.Diagnostics.Conditional("DEBUG")]
        private static void LogDetailedDebug(
            string tag,
            string message)
        {
#if ANDROID
            Log.Debug(
                tag,
                message);
#endif
        }


        private readonly MyApplication evergineApplication;
        private readonly IArCoreService _arCoreService;
        private readonly HybridRoutingService _hybridRoutingService;
        private readonly MLDARIntegrationService _mldArIntegrationService;
        private readonly ArHeadingAlignmentService _headingAlignmentService;
        private readonly ILocationService _locationService;
        private readonly RouteProgressTracker _routeProgressTracker;
        private readonly PedestrianDeadReckoningService _pdrService;
        private readonly GpsPdrFusionPolicy _gpsPdrFusionPolicy;
        private readonly PdrHeadingSmoother _pdrHeadingSmoother;
        private readonly OffRouteReroutePolicy _offRouteReroutePolicy;
        private readonly RouteReplacementPolicy _routeReplacementPolicy;
        private readonly HeadingRevalidationPolicy _headingRevalidationPolicy;
        private readonly PedestrianTurnGuidanceService _turnGuidanceService;
        private readonly SafeZoneConfirmationService _safeZoneConfirmationService;
        private readonly FloodDepthVisualizationService _floodDepthVisualizationService;
        private readonly HazardReroutingService _hazardReroutingService;

        /*
         * GPS and PDR can both update the same monotonic route progress state.
         * Serialize their short tracker + route-publication transactions so a
         * GPS sample cannot publish an older window immediately after a step.
         */
        private readonly object routeProgressFusionSync =
            new();

        private readonly IDispatcherTimer diagnosticTimer;

#if DEBUG
        private const long DetailedStatusLogIntervalMilliseconds =
            10_000;
#else
        private const long DetailedStatusLogIntervalMilliseconds =
            30_000;
#endif

        private const long DynamicUiRefreshIntervalMilliseconds =
            2_000;

        private long lastDetailedStatusLogTimestamp =
            long.MinValue;

        private long lastDynamicUiRefreshTimestamp =
            long.MinValue;

        private CancellationTokenSource? routeRequestCancellation;
        private CancellationTokenSource? routeProgressCancellation;
        private Task? routeProgressTask;

        private CancellationTokenSource? connectivityFailoverCancellation;
        private bool connectivityEventSubscribed;
        private bool networkLossFailoverInProgress;

        private const int NetworkLossConfirmationMilliseconds =
            1500;

        private const int NetworkFailoverBusyRetryMilliseconds =
            250;

        private const int NetworkFailoverMaximumBusyWaitMilliseconds =
            5000;

        private const int NetworkFailoverArReadyWaitMilliseconds =
            5000;

        private bool routeRequestInProgress;
        private bool destinationEventSubscribed;
        private bool emergencyAdvisoryEventSubscribed;
        private bool emergencyAdvisoryVisible;
        private bool pageIsVisible;

        private static readonly float[] CameraZoomLevels =
        {
            1.0f,
            2.0f,
            3.0f
        };

        private int currentCameraZoomLevelIndex;
        private bool flashlightToggleInProgress;

        private DisasterAdvisory? currentEmergencyAdvisory;

        // Retain the latest verified advisory for the compact Figma status
        // banner even after the full-screen advisory treatment is dismissed.
        private DisasterAdvisory? lastEmergencyAdvisoryForStatus;

        private DateTimeOffset? navigationSessionStartedAt;

        private CancellationTokenSource? emergencyAdvisoryAutoStartCancellation;

        private bool emergencyGuidanceStartInProgress;

        private enum CameraModuleViewMode
        {
            ArCamera = 0,
            Map2D = 1,
            FloodDepth = 2
        }

        private CameraModuleViewMode currentCameraModuleView =
            CameraModuleViewMode.ArCamera;

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

        private double activeDestinationSafeZoneRadiusMeters =
            SafeZoneConfirmationService.ArrivalRadiusMeters;

        private double activeMapToArYawDegrees;

        /*
         * AR ROUTE VISUAL POLICY
         *
         * Navigation starts in APPROACH mode. Until GPS has repeatedly shown
         * that the user is actually inside the routed pedestrian corridor, the
         * cyan visual points from the user's current position to the nearest
         * matched route corridor instead of projecting the long route through
         * the user's present off-road location.
         *
         * After two MEDIUM-or-better matches inside the accuracy-aware entry
         * radius, the visual becomes a 40 m road-following corridor using the
         * actual MLD/A* polyline geometry, including bends and turns. Once
         * established, this long corridor is retained through ordinary GPS
         * jitter and only falls back to the approach/recovery arrow when the
         * three-sample off-route policy verifies trustworthy displacement.
         *
         * A finite forward horizon is intentional. Rendering a kilometer-scale
         * route from one ARCore ground anchor would magnify heading/world-frame
         * error and waste draw calls. The 40 m local window is rebuilt from
         * current route progress and the current nearby AR frame.
         */
        private enum ArRouteVisualMode
        {
            ApproachOrOffCourseShort = 0,
            RoadFollowingLong = 1
        }

        private const double RoadFollowingRouteVisualWindowMeters =
            LocalArNavigationPolicy.RoadFollowingWindowMeters;

        private const double ApproachRouteVisualWindowMeters =
            LocalArNavigationPolicy.ApproachWindowMeters;

        private const int RoadFollowingEntryRequiredSamples =
            2;

        private ArRouteVisualMode arRouteVisualMode =
            ArRouteVisualMode.ApproachOrOffCourseShort;

        private int roadFollowingReentryConfirmationCount;

        private bool recoveryConnectorVerified;

        /*
         * TEMPORARY ADVISER-CONSULTATION OVERRIDE
         *
         * Keep the last accepted cyan RouteWindow visible when GPS freshness,
         * accuracy, or route identity deteriorates. This does not accept weak
         * GPS progress, bypass heading/spatial tracking requirements, publish a
         * recovery connector, or change rerouting decisions.
         *
         * Set false immediately after adviser consultation to restore the
         * production confidence gate.
         */
        private static readonly bool
            EnableConsultationRouteVisibilityOverride =
                true;

        private bool consultationRouteVisibilityOverrideActive;

        /*
         * TEST SWITCH:
         * Normal navigation baseline. Set true only for deliberate indoor
         * GPS-freeze diagnostics.
         */
        private static readonly bool IndoorRouteTestMode =
            false;

        /*
         * MILESTONE 3 DEVELOPER VALIDATION HARNESS
         *
         * TEMPORARY: keep true only while validating the confirmed-off-route
         * -> Railway reroute -> replacement-route publication pipeline.
         *
         * This does NOT change the real accuracy-aware route corridor. It only
         * exposes a test button that injects three policy
         * confirmations while routing from the latest REAL GPS coordinate.
         *
         * Set false after Milestone 3 validation; the button then disappears.
         */
        private static readonly bool EnableDeveloperOffRouteSimulation =
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
        private static readonly bool EnableDeveloperTurnSimulation =
            false;

        /*
         * STAGE 5 DEVELOPER SAFE-ZONE VALIDATION
         *
         * Production destinations now use facility-specific safe-zone radii.
         * This controlled harness intentionally keeps the original 30 m radius
         * so Stage 5 regression testing remains deterministic. When armed, only
         * SafeZoneConfirmationService evaluation is temporarily pointed at a
         * test coordinate 20 m ahead along the CURRENT active route. The real
         * evacuation-center destination and its facility geofence are unchanged.
         * Disable after Stage 5 validation.
         */
        private static readonly bool EnableDeveloperSafeZoneValidation =
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
        private static readonly bool EnableDeveloperFloodDepthValidation =
            true;

        private static readonly double?[] DeveloperFloodDepthSequenceMeters =
        {
            0.30,
            0.60,
            1.00,
            1.50,
            null
        };

        /*
         * STAGE 10 DEVELOPER DYNAMIC-HAZARD VALIDATION
         *
         * This controlled harness injects one geographic road hazard ahead
         * on the CURRENT route, then exercises the real route/hazard
         * intersection -> hazard-aware MLD/A* -> AR replacement pipeline. It is
         * independent from production Approved Community Reports.
         */
        private static readonly bool EnableDeveloperDynamicHazardValidation =
            true;

        private const double DeveloperHazardAheadMeters =
            45.0;

        private const double DeveloperHazardRadiusMeters =
            12.0;

        private bool developerHazardValidationArmed;

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
        private static readonly bool FreezeRouteProgressDuringIndoorTest =
            true;

        /*
         * PDR MILESTONE 1
         *
         * PDR is intentionally allowed while indoor GPS progress is frozen.
         * This lets us validate physical step -> route progress -> moving AR
         * window without letting poor indoor GPS move the route.
         */
        private static readonly bool EnablePedestrianDeadReckoning =
            true;

        private const double PdrStepLengthMeters =
            0.70;

        /*
         * Direction gating occurs entirely in the retained ARCore world:
         *
         * rolling ARCore walking displacement + smoothed camera forward
         *                           vs.
         *              current rendered route tangent.
         *
         * This reduces sensitivity to one phone rotation while keeping route
         * context as the acceptance constraint. Earth-referenced calibration
         * still determines how the geographic route is rendered.
         */
        private double? lastPdrHeadingErrorDegrees;

        private string lastPdrHeadingSource =
            "Unavailable";

        private double lastPdrMotionCoherence;

        private double lastPdrStrideScale;

        private GpsPdrFusionPolicy.PdrConfidence lastPdrConfidence =
            GpsPdrFusionPolicy.PdrConfidence.Rejected;

        private GpsPdrFusionPolicy.GpsConfidence lastGpsConfidence =
            GpsPdrFusionPolicy.GpsConfidence.Unavailable;

        private RouteMatchConfidence lastRouteMatchConfidence =
            RouteMatchConfidence.Unavailable;

        private ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot
            lastArGuidanceConfidence =
                ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot.NotReady;

        private GpsPdrFusionPolicy.GpsFusionAction lastGpsFusionAction =
            GpsPdrFusionPolicy.GpsFusionAction.Ignore;

        private double? lastGpsPdrDivergenceMeters;

        private int lastGpsBackwardConfirmationCount;

        private int lastGpsRouteIdentityConfirmationCount;

        private double lastGpsReliabilityWeight;

        private long acceptedPdrStepCount;

        private long rejectedPdrStepCount;

        private bool dynamicRerouteInProgress;

        private bool lastOffRouteCandidate;

        private int lastOffRouteConfirmationCount;

        private string lastRerouteResult =
            "None";

        private GeoCoordinate? latestGpsCoordinateForDeveloperReroute;

        private double? latestGpsAccuracyForDeveloperReroute;

        private DateTimeOffset? latestGpsTimestampForRouting;

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
         * it actually releases an anchor, either because it became stale or
         * because it left the moving local AR radius. Only that durable event
         * starts CameraPage's local route-rebase phase.
         */
        private long handledGroundAnchorReplacementGeneration;

        private long activeGroundAnchorReplacementGeneration =
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

            this.evergineApplication =
                new MyApplication();

            this.evergineView.Application =
                this.evergineApplication;

            _arCoreService =
                arCoreService;

            currentCameraZoomLevelIndex =
                FindClosestCameraZoomLevelIndex(
                    _arCoreService.CameraZoomRatio);

            RefreshCameraControlUi();

            handledGroundAnchorReplacementGeneration =
                _arCoreService.GroundAnchorReplacementGeneration;

            _hybridRoutingService =
                new HybridRoutingService(
                    new MLDRoutingService(),
                    NavigationDataBootstrap.GetRoadGraphAsync,
                    () =>
                        Connectivity.Current.NetworkAccess ==
                            NetworkAccess.Internet);

            _mldArIntegrationService =
                new MLDARIntegrationService(
                    _hybridRoutingService);

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

            _pdrHeadingSmoother =
                new PdrHeadingSmoother();

            _offRouteReroutePolicy =
                new OffRouteReroutePolicy();

            _routeReplacementPolicy =
                new RouteReplacementPolicy();

            _headingRevalidationPolicy =
                new HeadingRevalidationPolicy();

            _turnGuidanceService =
                new PedestrianTurnGuidanceService();

            _safeZoneConfirmationService =
                new SafeZoneConfirmationService();

            _floodDepthVisualizationService =
                new FloodDepthVisualizationService();

            _hazardReroutingService =
                new HazardReroutingService();

            _pdrService.StepDetected +=
                OnPdrStepDetected;

            diagnosticTimer =
                Dispatcher.CreateTimer();

            diagnosticTimer.Interval =
                TimeSpan.FromSeconds(
                    1);

            diagnosticTimer.Tick +=
                OnDiagnosticTimerTick;

            ApplyCameraModuleView(
                CameraModuleViewMode.ArCamera,
                "initial Camera module view");
        }

        private void ApplyCameraModuleView(
            CameraModuleViewMode mode,
            string reason)
        {
            currentCameraModuleView =
                mode;

            bool arCameraMode =
                mode ==
                    CameraModuleViewMode.ArCamera;

            bool mapMode =
                mode ==
                    CameraModuleViewMode.Map2D;

            bool floodMode =
                mode ==
                    CameraModuleViewMode.FloodDepth;

            ARCameraSpatialController.SetRouteRenderingEnabled(
                arCameraMode &&
                    lastArGuidanceConfidence.AllowsRouteGeometry,
                $"Camera module view = {mode}; " +
                $"guidanceState={lastArGuidanceConfidence.State}; {reason}");

            SetFloodVisualizationVisibility(
                floodMode &&
                    currentFloodVisualization.IsAvailable,
                $"Camera module view = {mode}; {reason}");

            Dispatcher.Dispatch(
                () =>
                {
                    mapModeLayer.IsVisible =
                        mapMode;

                    evergineView.IsVisible =
                        !mapMode;

                    floodDepthOcclusionView.IsVisible =
                        floodMode;

                    cameraModeStatusBanner.IsVisible =
                        arCameraMode;

                    floodWaitingBanner.IsVisible =
                        floodMode &&
                        !currentFloodVisualization.IsAvailable &&
                        !safeZoneConfirmed;

                    floodVisualizationLayer.IsVisible =
                        floodMode &&
                        currentFloodVisualization.IsAvailable &&
                        !safeZoneConfirmed;

                    cameraHeaderBackGroup.IsVisible =
                        !floodMode;

                    cameraZoomControls.IsVisible =
                        !mapMode;

                    cameraModeSwitcherButton.IsVisible =
                        !safeZoneConfirmed;

                    navigationAwarenessSheet.IsVisible =
                        false;

                    floodSimulationConfigurationSheet.IsVisible =
                        false;

                    arGuidanceSelectedIcon.IsVisible =
                        arCameraMode;

                    mapGuidanceSelectedIcon.IsVisible =
                        mapMode;

                    floodGuidanceSelectedIcon.IsVisible =
                        floodMode;

                    cameraModeSwitcherMapIcon.IsVisible =
                        !floodMode;

                    cameraModeSwitcherFloodIcon.IsVisible =
                        floodMode;

                    arDeveloperControls.IsVisible =
                        false;

                    developerSafeZoneTestButton.IsVisible =
                        EnableDeveloperSafeZoneValidation;

                    developerTurnTestButton.IsVisible =
                        false;

                    developerRerouteTestButton.IsVisible =
                        false;

                    developerHazardRerouteTestButton.IsVisible =
                        EnableDeveloperDynamicHazardValidation;

                    if (!arCameraMode)
                    {
                        turnGuidancePanel.IsVisible =
                            false;
                    }
                    else if (!emergencyAdvisoryVisible &&
                             !safeZoneConfirmed &&
                             lastTurnGuidance.IsAvailable)
                    {
                        ApplyPrototypeTurnGuidance(
                            lastTurnGuidance);

                        turnGuidancePanel.IsVisible =
                            true;
                    }

                    RefreshEmergencyStatusBanner();
                    RefreshCameraModuleDynamicUi();
                });

#if ANDROID
            Log.Debug(
                "RescuAR-CameraUI",
                "Camera module sub-tab changed: " +
                $"mode={mode}, reason='{reason}'.");
#endif

            UpdateArCoreActivityForCurrentView(
                reason);
        }

        private void UpdateArCoreActivityForCurrentView(
            string reason)
        {
            if (!pageIsVisible)
            {
                return;
            }

            if (currentCameraModuleView ==
                CameraModuleViewMode.Map2D)
            {
                arCoreAutoStartCancellation?.Cancel();
                arCoreAutoStartCancellation?.Dispose();

                arCoreAutoStartCancellation =
                    null;

                if (_arCoreService.IsInitialized &&
                    !_arCoreService.IsSessionPaused)
                {
                    _arCoreService.PauseCameraSession();
                }

#if ANDROID
                Log.Info(
                    ArCoreLogTag,
                    "AR WORKLOAD STATE: camera/session and 3D surface " +
                    $"SUSPENDED for 2D Map; reason='{reason}'.");
#endif
                return;
            }

            if (_arCoreService.IsInitialized &&
                !_arCoreService.IsSessionPaused)
            {
                return;
            }

            arCoreAutoStartCancellation?.Cancel();
            arCoreAutoStartCancellation?.Dispose();

            arCoreAutoStartCancellation =
                new CancellationTokenSource();

            _ =
                EnsureArCoreActiveAsync(
                    arCoreAutoStartCancellation.Token);
        }

        private void RefreshCameraModuleDynamicUi()
        {
            bool arCameraMode =
                currentCameraModuleView ==
                    CameraModuleViewMode.ArCamera;

            bool floodMode =
                currentCameraModuleView ==
                    CameraModuleViewMode.FloodDepth;

            bool mapMode =
                currentCameraModuleView ==
                    CameraModuleViewMode.Map2D;

            bool hasDestination =
                NavigationDestinationBridge.Current.IsAvailable;

            endNavigationButton.IsVisible =
                arCameraMode &&
                hasDestination &&
                !safeZoneConfirmed;

            exploreSafeZonesButton.IsVisible =
                arCameraMode &&
                !hasDestination &&
                !safeZoneConfirmed;

            developerFloodDepthTestButton.IsVisible =
                floodMode &&
                EnableDeveloperFloodDepthValidation &&
                !safeZoneConfirmed;

            cameraModeSwitcherButton.IsVisible =
                !safeZoneConfirmed;

            mapModeStatusLabel.Text =
                "Placeholder";

            if (floodMode &&
                currentFloodVisualization.IsAvailable)
            {
                floodModeDepthSummaryLabel.Text =
                    currentFloodVisualization.PrimaryText;
            }
            else if (floodMode)
            {
                floodModeDepthSummaryLabel.Text =
                    "Waiting for simulation...";
            }

            floodWaitingBanner.IsVisible =
                floodMode &&
                !currentFloodVisualization.IsAvailable &&
                !safeZoneConfirmed;

            RefreshEmergencyStatusBanner();
            RefreshArTrackingStatusBanner();
        }

        private void RefreshArTrackingStatusBanner()
        {
            bool headingTrusted =
                lastHeadingAlignment.HasValue &&
                lastHeadingAlignment.Value.IsAvailable &&
                lastHeadingAlignment.Value.IsStable;

            ARCameraSpatialController.SetRouteHeadingTrust(
                headingTrusted,
                headingTrusted
                    ? "Stable map-to-AR heading alignment is available."
                    : "Map-to-AR heading alignment is unavailable or unstable.");

            ARCameraSpatialController.SpatialContinuitySnapshot continuity =
                ARCameraSpatialController.CurrentSpatialContinuity;

            ARRouteBridge.RouteSnapshot route =
                ARRouteBridge.Current;

            RouteProgressTracker.ProgressSnapshot progress =
                _routeProgressTracker.Current;

            DateTimeOffset? latestGpsTimestamp;

            GpsPdrFusionPolicy.GpsConfidence gpsConfidence;

            RouteMatchConfidence routeMatchConfidence;

            bool routeIdentitySuspended;

            bool verifiedRecoveryConnector;

            lock (routeProgressFusionSync)
            {
                latestGpsTimestamp =
                    latestGpsTimestampForRouting;

                gpsConfidence =
                    lastGpsConfidence;

                routeMatchConfidence =
                    lastRouteMatchConfidence;

                routeIdentitySuspended =
                    _gpsPdrFusionPolicy.IsRouteIdentitySuspended;

                verifiedRecoveryConnector =
                    recoveryConnectorVerified;
            }

            DateTimeOffset now =
                DateTimeOffset.UtcNow;

            TimeSpan gpsAge =
                latestGpsTimestamp.HasValue
                    ? now -
                        latestGpsTimestamp.Value
                    : TimeSpan.MaxValue;

            bool gpsFresh =
                gpsAge >=
                    TimeSpan.FromSeconds(
                        -2) &&
                gpsAge <=
                    TimeSpan.FromSeconds(
                        10);

            ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot
                policyConfidence =
                ARGuidanceConfidencePolicy.Evaluate(
                    NavigationDestinationBridge.Current.IsAvailable,
                    route.IsAvailable,
                    route.NavigationState.VisualKind,
                    progress.HasProgress,
                    gpsFresh,
                    gpsConfidence,
                    routeMatchConfidence,
                    routeIdentitySuspended,
                    headingTrusted,
                    continuity,
                    dynamicRerouteInProgress,
                    verifiedRecoveryConnector);

            consultationRouteVisibilityOverrideActive =
                ShouldEnableConsultationRouteVisibilityOverride(
                    policyConfidence,
                    route,
                    progress,
                    headingTrusted,
                    continuity);

            ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot confidence =
                consultationRouteVisibilityOverrideActive
                    ? policyConfidence with
                    {
                        AllowsRouteGeometry = true,
                        DisplayMessage =
                            "CONSULTATION PREVIEW — GPS accuracy reduced; " +
                            "do not use for evacuation"
                    }
                    : policyConfidence;

            bool confidenceChanged =
                confidence.State !=
                    lastArGuidanceConfidence.State ||
                confidence.AllowsRouteGeometry !=
                    lastArGuidanceConfidence.AllowsRouteGeometry ||
                confidence.DisplayMessage !=
                    lastArGuidanceConfidence.DisplayMessage;

            lastArGuidanceConfidence =
                confidence;

            bool arCameraMode =
                currentCameraModuleView ==
                    CameraModuleViewMode.ArCamera;

            ARCameraSpatialController.SetRouteRenderingEnabled(
                arCameraMode &&
                    confidence.AllowsRouteGeometry,
                $"guidanceState={confidence.State}, " +
                $"score={confidence.Score}/100, " +
                $"reason='{confidence.DisplayMessage}'");

#if ANDROID
            if (confidenceChanged)
            {
                Log.Info(
                    RouteLogTag,
                    "AR GUIDANCE CONFIDENCE: " +
                    $"state={confidence.State}, " +
                    $"score={confidence.Score}/100, " +
                    $"routeVisible={confidence.AllowsRouteGeometry}, " +
                    $"gpsFresh={gpsFresh}, " +
                    $"gps={gpsConfidence}, " +
                    $"routeMatch={routeMatchConfidence}, " +
                    $"spatial={continuity.State}, " +
                    $"consultationOverride=" +
                    $"{consultationRouteVisibilityOverrideActive}, " +
                    $"reason='{confidence.DisplayMessage}'.");
            }
#endif

            bool shouldShow =
                arCameraMode &&
                NavigationDestinationBridge.Current.IsAvailable &&
                confidence.State !=
                    ARGuidanceConfidencePolicy.GuidanceConfidenceState.Full;

            arTrackingStatusBanner.IsVisible =
                shouldShow;

            turnGuidancePanel.Margin =
                new Thickness(
                    8,
                    shouldShow
                        ? 88
                        : 48,
                    8,
                    0);

            RefreshTurnGuidancePanelForCurrentState();

            if (!shouldShow)
            {
                return;
            }

            bool severe =
                confidence.State ==
                    ARGuidanceConfidencePolicy.GuidanceConfidenceState.Hidden;

            arTrackingStatusBanner.BackgroundColor =
                Color.FromArgb(
                    severe
                        ? "#FBE1E3"
                        : "#FFF3CD");

            arTrackingStatusBanner.Stroke =
                new SolidColorBrush(
                    Color.FromArgb(
                        severe
                            ? "#F2B4BA"
                            : "#E6B800"));

            arTrackingStatusLabel.TextColor =
                Color.FromArgb(
                    severe
                        ? "#B4232B"
                        : "#7A5700");

            arTrackingStatusLabel.Text =
                confidence.DisplayMessage;
        }

        private bool ShouldEnableConsultationRouteVisibilityOverride(
            ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot
                policyConfidence,
            ARRouteBridge.RouteSnapshot route,
            RouteProgressTracker.ProgressSnapshot progress,
            bool headingTrusted,
            ARCameraSpatialController.SpatialContinuitySnapshot spatial)
        {
            if (!EnableConsultationRouteVisibilityOverride ||
                policyConfidence.AllowsRouteGeometry ||
                !NavigationDestinationBridge.Current.IsAvailable ||
                !route.IsAvailable ||
                !route.NavigationState.IsAvailable ||
                route.NavigationState.VisualKind !=
                    RouteVisualKind.RouteWindow ||
                !progress.HasRoute ||
                !headingTrusted ||
                dynamicRerouteInProgress)
            {
                return false;
            }

            return spatial.State ==
                       ARCameraSpatialController.SpatialContinuityState.Live ||
                   spatial.State ==
                       ARCameraSpatialController.SpatialContinuityState.ShortHold;
        }

        private void RefreshTurnGuidancePanelForCurrentState()
        {
            if (dynamicRerouteInProgress)
            {
                return;
            }

            RouteResult? acceptedRoute;

            ARRouteBridge.RouteSnapshot route;

            lock (routeProgressFusionSync)
            {
                acceptedRoute =
                    activeRoute;

                route =
                    ARRouteBridge.Current;
            }

            bool routeIdentityMatches =
                acceptedRoute is not null &&
                string.Equals(
                    route.Algorithm,
                    acceptedRoute.Algorithm,
                    StringComparison.Ordinal) &&
                double.IsFinite(
                    route.TotalDistanceMeters) &&
                double.IsFinite(
                    acceptedRoute.TotalDistanceMeters) &&
                Math.Abs(
                    route.TotalDistanceMeters -
                    acceptedRoute.TotalDistanceMeters) <=
                        0.50;

            bool shouldShow =
                currentCameraModuleView ==
                    CameraModuleViewMode.ArCamera &&
                pageIsVisible &&
                !emergencyAdvisoryVisible &&
                !safeZoneConfirmed &&
                NavigationDestinationBridge.Current.IsAvailable &&
                routeIdentityMatches &&
                route.IsAvailable &&
                route.NavigationState.IsAvailable &&
                route.NavigationState.VisualKind ==
                    RouteVisualKind.RouteWindow &&
                lastTurnGuidance.IsAvailable;

            if (!shouldShow)
            {
                turnGuidancePanel.IsVisible =
                    false;

                return;
            }

            ApplyPrototypeTurnGuidance(
                lastTurnGuidance);

            turnGuidancePanel.IsVisible =
                true;
        }

        private void RefreshEmergencyStatusBanner()
        {
            if (currentCameraModuleView !=
                    CameraModuleViewMode.ArCamera)
            {
                cameraModeStatusBanner.IsVisible =
                    false;

                return;
            }

            cameraModeStatusBanner.IsVisible =
                !safeZoneConfirmed;

            DisasterAdvisory? advisory =
                lastEmergencyAdvisoryForStatus;

            if (advisory is null)
            {
                cameraModeStatusBanner.BackgroundColor =
                    Color.FromArgb("#DDF7E8");

                cameraModeStatusBanner.Stroke =
                    new SolidColorBrush(
                        Color.FromArgb("#BDE9D0"));

                cameraModeStatusTitleLabel.TextColor =
                    Color.FromArgb("#07814D");

                cameraModeStatusSubtitleLabel.TextColor =
                    Color.FromArgb("#16815A");

                cameraModeStatusTitleLabel.Text =
                    "No Emergency";

                cameraModeStatusSubtitleLabel.Text =
                    "You will be notified if an emergency is detected nearby.";

                return;
            }

            bool moderate =
                IsModerateSeverityAdvisory(
                    advisory);

            if (moderate)
            {
                cameraModeStatusBanner.BackgroundColor =
                    Color.FromArgb("#F9EBCB");

                cameraModeStatusBanner.Stroke =
                    new SolidColorBrush(
                        Color.FromArgb("#F2CA73"));

                cameraModeStatusTitleLabel.TextColor =
                    Color.FromArgb("#9A6400");

                cameraModeStatusSubtitleLabel.TextColor =
                    Color.FromArgb("#9A6400");
            }
            else
            {
                cameraModeStatusBanner.BackgroundColor =
                    Color.FromArgb("#FBE1E3");

                cameraModeStatusBanner.Stroke =
                    new SolidColorBrush(
                        Color.FromArgb("#F2B4BA"));

                cameraModeStatusTitleLabel.TextColor =
                    Color.FromArgb("#D71920");

                cameraModeStatusSubtitleLabel.TextColor =
                    Color.FromArgb("#D71920");
            }

            cameraModeStatusTitleLabel.Text =
                "Emergency Detected";

            cameraModeStatusSubtitleLabel.Text =
                $"{GetEmergencyCategoryLabel(advisory)} Advisory - " +
                $"{GetEmergencySeverityLabel(advisory)} Severity";
        }

        private void OnArCameraModeClicked(
            object? sender,
            TappedEventArgs e)
        {
            ApplyCameraModuleView(
                CameraModuleViewMode.ArCamera,
                "AR Camera sub-tab selected");
        }

        private void OnMapModeClicked(
            object? sender,
            TappedEventArgs e)
        {
            ApplyCameraModuleView(
                CameraModuleViewMode.Map2D,
                "2D Map sub-tab selected");
        }

        private void OnFloodDepthModeClicked(
            object? sender,
            TappedEventArgs e)
        {
            ApplyCameraModuleView(
                CameraModuleViewMode.FloodDepth,
                "Flood Depth sub-tab selected");
        }

        private void OnCameraZoomInClicked(
            object? sender,
            TappedEventArgs e)
        {
            ChangeCameraZoomLevel(
                +1);
        }

        private void OnCameraZoomOutClicked(
            object? sender,
            TappedEventArgs e)
        {
            ChangeCameraZoomLevel(
                -1);
        }

        private void ChangeCameraZoomLevel(
            int direction)
        {
            int targetIndex =
                Math.Clamp(
                    currentCameraZoomLevelIndex +
                        Math.Sign(direction),
                    0,
                    CameraZoomLevels.Length - 1);

            if (targetIndex ==
                currentCameraZoomLevelIndex)
            {
                return;
            }

            float zoomRatio =
                CameraZoomLevels[
                    targetIndex];

            _arCoreService.SetCameraZoomRatio(
                zoomRatio);

            currentCameraZoomLevelIndex =
                targetIndex;

            RefreshCameraControlUi();

#if ANDROID
            Log.Info(
                "RescuAR-CameraUI",
                $"Camera zoom changed to {zoomRatio:0.#}x.");
#endif
        }

        private async void OnCameraFlashlightClicked(
            object? sender,
            TappedEventArgs e)
        {
            if (flashlightToggleInProgress)
            {
                return;
            }

            flashlightToggleInProgress =
                true;

            try
            {
                /*
                 * The Camera page auto-starts ARCore, but a very fast tap can
                 * arrive before initialization has finished. Reuse the same
                 * serialized activation path rather than trying to touch the
                 * physical camera directly.
                 */
                if (!_arCoreService.IsInitialized ||
                    _arCoreService.IsSessionPaused)
                {
                    bool arCoreReady =
                        await EnsureArCoreActiveAsync(
                            CancellationToken.None);

                    if (!arCoreReady)
                    {
                        await DisplayAlert(
                            "Flashlight unavailable",
                            "The AR camera is not ready yet.",
                            "OK");

                        return;
                    }
                }

                bool requestedState =
                    !_arCoreService.IsFlashlightOn;

                bool changed =
                    await _arCoreService.SetFlashlightAsync(
                        requestedState);

                RefreshCameraControlUi();

                if (!changed)
                {
                    await DisplayAlert(
                        "Flashlight unavailable",
                        "The active camera does not provide a usable flashlight.",
                        "OK");
                }
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Warn(
                    "RescuAR-CameraUI",
                    $"Camera flashlight toggle failed: {exception.Message}");
#endif

                RefreshCameraControlUi();

                await DisplayAlert(
                    "Flashlight unavailable",
                    "The flashlight could not be changed while the AR camera is active.",
                    "OK");
            }
            finally
            {
                flashlightToggleInProgress =
                    false;
            }
        }

        private void RefreshCameraControlUi()
        {
            float currentZoom =
                _arCoreService.CameraZoomRatio;

            currentCameraZoomLevelIndex =
                FindClosestCameraZoomLevelIndex(
                    currentZoom);

            cameraZoomRatioLabel.Text =
                $"{CameraZoomLevels[currentCameraZoomLevelIndex]:0.#}x";

            cameraZoomInIcon.Opacity =
                currentCameraZoomLevelIndex <
                    CameraZoomLevels.Length - 1
                    ? 1.0
                    : 0.35;

            cameraZoomOutIcon.Opacity =
                currentCameraZoomLevelIndex > 0
                    ? 1.0
                    : 0.35;

            bool flashlightOn =
                _arCoreService.IsFlashlightOn;

            cameraFlashlightButton.BackgroundColor =
                Color.FromArgb(
                    flashlightOn
                        ? "#FFF2A8"
                        : "#ECFFFFFF");

            cameraFlashlightIcon.Opacity =
                flashlightOn
                    ? 1.0
                    : 0.78;
        }

        private static int FindClosestCameraZoomLevelIndex(
            float zoomRatio)
        {
            int closestIndex =
                0;

            float closestDistance =
                float.MaxValue;

            for (int index = 0;
                 index < CameraZoomLevels.Length;
                 index++)
            {
                float distance =
                    Math.Abs(
                        CameraZoomLevels[index] -
                        zoomRatio);

                if (distance <
                    closestDistance)
                {
                    closestDistance =
                        distance;

                    closestIndex =
                        index;
                }
            }

            return closestIndex;
        }

        private void OnNavigationAwarenessClicked(
            object? sender,
            TappedEventArgs e)
        {
            navigationAwarenessSheet.IsVisible =
                true;

            arDeveloperControls.IsVisible =
                currentCameraModuleView ==
                    CameraModuleViewMode.ArCamera &&
                (EnableDeveloperSafeZoneValidation ||
                 EnableDeveloperDynamicHazardValidation);
        }

        private void OnNavigationAwarenessCloseClicked(
            object? sender,
            TappedEventArgs e)
        {
            navigationAwarenessSheet.IsVisible =
                false;
        }

        private void OnArGuidanceOptionClicked(
            object? sender,
            TappedEventArgs e)
        {
            navigationAwarenessSheet.IsVisible =
                false;

            ApplyCameraModuleView(
                CameraModuleViewMode.ArCamera,
                "AR Evacuation Guidance selected from Navigation & Awareness");
        }

        private void OnMapGuidanceOptionClicked(
            object? sender,
            TappedEventArgs e)
        {
            navigationAwarenessSheet.IsVisible =
                false;

            ApplyCameraModuleView(
                CameraModuleViewMode.Map2D,
                "2D Map Guidance selected from Navigation & Awareness");
        }

        private void OnFloodGuidanceOptionClicked(
            object? sender,
            TappedEventArgs e)
        {
            navigationAwarenessSheet.IsVisible =
                false;

            ApplyCameraModuleView(
                CameraModuleViewMode.FloodDepth,
                "Flood Depth Visualization selected from Navigation & Awareness");
        }

        private async void OnExploreSafeZonesClicked(
            object? sender,
            EventArgs e)
        {
            try
            {
                if (Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync(
                        "Prepare/EvacuationCenterInfo");
                }
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Warn(
                    "RescuAR-CameraUI",
                    $"Explore Safe Zones navigation failed: {exception.Message}");
#endif
            }
        }

        private void OnFloodSimulationConfigureClicked(
            object? sender,
            EventArgs e)
        {
            if (!EnableDeveloperFloodDepthValidation)
            {
                return;
            }

            navigationAwarenessSheet.IsVisible =
                false;

            if (currentFloodVisualization.LocalDepthMeters.HasValue)
            {
                floodSimulationDepthSlider.Value =
                    Math.Clamp(
                        currentFloodVisualization.LocalDepthMeters.Value,
                        floodSimulationDepthSlider.Minimum,
                        floodSimulationDepthSlider.Maximum);
            }

            floodSimulationDepthValueLabel.Text =
                floodSimulationDepthSlider.Value.ToString("0.0");

            floodSimulationConfigurationSheet.IsVisible =
                true;
        }

        private void OnFloodSimulationSliderChanged(
            object? sender,
            ValueChangedEventArgs e)
        {
            if (floodSimulationDepthValueLabel is null)
            {
                return;
            }

            floodSimulationDepthValueLabel.Text =
                e.NewValue.ToString("0.0");
        }

        private void OnFloodSimulationSheetCloseClicked(
            object? sender,
            TappedEventArgs e)
        {
            floodSimulationConfigurationSheet.IsVisible =
                false;
        }

        private void OnSaveFloodSimulationClicked(
            object? sender,
            EventArgs e)
        {
            if (!EnableDeveloperFloodDepthValidation)
            {
                return;
            }

            double depth =
                Math.Clamp(
                    floodSimulationDepthSlider.Value,
                    0.1,
                    3.0);

            FloodDepthVisualizationService.FloodVisualizationSnapshot snapshot =
                _floodDepthVisualizationService.FromLocalDepth(
                    depth,
                    "DEV synthetic local depth",
                    "Camera simulation");

            ApplyFloodVisualization(
                snapshot,
                "Figma flood-depth simulation configuration");

            floodSimulationConfigurationSheet.IsVisible =
                false;

#if ANDROID
            Log.Info(
                FloodDepthLogTag,
                "[DEV FLOOD] FIGMA CONFIGURATION APPLIED: " +
                $"depth={depth:F2}m, groundRelative=True.");
#endif
        }

        private async void OnCameraHeaderBackClicked(
            object? sender,
            TappedEventArgs e)
        {
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
                    "RescuAR-CameraUI",
                    $"Camera header Back navigation failed: {exception.Message}");
#endif
            }
        }

        private async void OnCameraSettingsClicked(
            object? sender,
            TappedEventArgs e)
        {
            try
            {
                if (Shell.Current is not null)
                {
                    /*
                     * The current source does not expose a dedicated Settings
                     * route. Profile is the existing account/settings entry
                     * point, so the reference gear uses that destination.
                     */
                    await Shell.Current.GoToAsync(
                        "//Profile");
                }
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Warn(
                    "RescuAR-CameraUI",
                    $"Camera settings navigation failed: {exception.Message}");
#endif
            }
        }

        private async void OnCameraNotificationsClicked(
            object? sender,
            TappedEventArgs e)
        {
            try
            {
                if (Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync(
                        "AdvisoryFeedPage");
                }
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Warn(
                    "RescuAR-CameraUI",
                    $"Camera notifications navigation failed: {exception.Message}");
#endif
            }
        }

        private void OnEndNavigationClicked(
            object? sender,
            EventArgs e)
        {
            if (!NavigationDestinationBridge.Current.IsAvailable)
            {
                return;
            }

#if ANDROID
            Log.Info(
                "RescuAR-CameraUI",
                "User ended active navigation from the Camera reference UI.");
#endif

            NavigationDestinationBridge.Clear();

            Dispatcher.Dispatch(
                RefreshCameraModuleDynamicUi);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            pageIsVisible =
                true;

            lastDetailedStatusLogTimestamp =
                long.MinValue;

            lastDynamicUiRefreshTimestamp =
                long.MinValue;

            RefreshCameraControlUi();

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
            SubscribeConnectivityChanges();

            ScheduleNetworkLossFailoverIfNeeded(
                "Camera tab entered");

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

            ApplyCameraModuleView(
                currentCameraModuleView,
                "Camera tab entered");
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
            UnsubscribeConnectivityChanges();
            CancelConnectivityFailover(
                "Camera tab exited");
            HideEmergencyAdvisoryOverlay(
                "Camera tab exited",
                restoreTurnGuidance: false);

            navigationAwarenessSheet.IsVisible =
                false;

            floodSimulationConfigurationSheet.IsVisible =
                false;

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

                if (!pageIsVisible ||
                    currentCameraModuleView ==
                        CameraModuleViewMode.Map2D)
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
                        currentCameraModuleView ==
                            CameraModuleViewMode.Map2D)
                    {
                        _arCoreService.PauseCameraSession();

                        return false;
                    }

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

                if (!pageIsVisible ||
                    currentCameraModuleView ==
                        CameraModuleViewMode.Map2D)
                {
                    Log.Debug(
                        ArCoreLogTag,
                        "Automatic ARCore initialization cancelled because " +
                        "the active view no longer needs the camera.");

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

                if (!pageIsVisible ||
                    currentCameraModuleView ==
                        CameraModuleViewMode.Map2D)
                {
                    return false;
                }

                /*
                 * A newly created Session establishes a new arbitrary ARCore
                 * world yaw. Reset only Session-scoped spatial state here.
                 */
                _headingAlignmentService.ResetSessionCalibration(
                    "creating a new ARCore Session");

                _headingRevalidationPolicy.Reset();

                _pdrHeadingSmoother.Reset();

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

                if (!pageIsVisible ||
                    currentCameraModuleView ==
                        CameraModuleViewMode.Map2D)
                {
                    _arCoreService.PauseCameraSession();

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

                lock (routeProgressFusionSync)
                {
                    latestGpsCoordinateForDeveloperReroute =
                        locationReading.Coordinate;

                    latestGpsAccuracyForDeveloperReroute =
                        locationReading.AccuracyMeters;

                    latestGpsTimestampForRouting =
                        locationReading.Timestamp;
                }

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
                        : "GPS origin acquired. Capturing initial map-to-AR heading alignment.");

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
                        arWindowMeters:
                            GetCurrentArRouteVisualWindowMeters(),
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

                navigationSessionStartedAt ??=
                    DateTimeOffset.UtcNow;

                activeDestinationName =
                    destination.Name;

                activeDestinationCoordinate =
                    destination.Coordinate;

                activeDestinationSafeZoneRadiusMeters =
                    destination.SafeZoneRadiusMeters;

                activeMapToArYawDegrees =
                    mapToArYawDegrees;

                _routeProgressTracker.SetRoute(
                    route);

                lastRouteMatchConfidence =
                    RouteMatchConfidence.Unavailable;

                _offRouteReroutePolicy.Reset();

                _routeReplacementPolicy.Reset();

                _headingRevalidationPolicy.Reset();

                UpdateTurnGuidance();

                StartRouteProgress();

                Log.Debug(
                    MldLogTag,
                    "Navigation route request COMPLETE: " +
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

        private void SubscribeConnectivityChanges()
        {
            if (connectivityEventSubscribed)
            {
                return;
            }

            Connectivity.Current.ConnectivityChanged +=
                OnConnectivityChanged;

            connectivityEventSubscribed =
                true;

#if ANDROID
            Log.Debug(
                RerouteLogTag,
                "Connectivity failover watcher subscribed: " +
                $"networkAccess={Connectivity.Current.NetworkAccess}.");
#endif
        }

        private void UnsubscribeConnectivityChanges()
        {
            if (!connectivityEventSubscribed)
            {
                return;
            }

            Connectivity.Current.ConnectivityChanged -=
                OnConnectivityChanged;

            connectivityEventSubscribed =
                false;
        }

        private void OnConnectivityChanged(
            object? sender,
            ConnectivityChangedEventArgs e)
        {
#if ANDROID
            Log.Warn(
                RerouteLogTag,
                "CONNECTIVITY CHANGED: " +
                $"networkAccess={e.NetworkAccess}, " +
                $"activeAlgorithm='{activeRoute?.Algorithm ?? "<none>"}'.");
#endif

            if (e.NetworkAccess ==
                NetworkAccess.Internet)
            {
                CancelConnectivityFailover(
                    "Internet connectivity restored");

#if ANDROID
                if (activeRoute is not null &&
                    IsAStarRoute(
                        activeRoute))
                {
                    Log.Debug(
                        RerouteLogTag,
                        "Internet restored while an offline A* route is active. " +
                        "Keeping the current route; MLD becomes preferred again " +
                        "for the next new route/reroute request.");
                }
#endif
                return;
            }

            ScheduleNetworkLossFailoverIfNeeded(
                $"ConnectivityChanged:{e.NetworkAccess}");
        }

        /// <summary>
        /// When a route produced by MLD is active and Internet access is lost,
        /// retain that route visually while preparing exactly one replacement
        /// route through the existing offline A* implementation.
        /// </summary>
        private void ScheduleNetworkLossFailoverIfNeeded(
            string reason)
        {
#if ANDROID
            if (!pageIsVisible ||
                safeZoneConfirmed ||
                activeRoute is null ||
                !IsMldRoute(
                    activeRoute) ||
                !activeDestinationCoordinate.HasValue ||
                Connectivity.Current.NetworkAccess ==
                    NetworkAccess.Internet ||
                networkLossFailoverInProgress ||
                connectivityFailoverCancellation is not null)
            {
                return;
            }

            connectivityFailoverCancellation =
                new CancellationTokenSource();

            CancellationToken cancellationToken =
                connectivityFailoverCancellation.Token;

            Log.Warn(
                RerouteLogTag,
                "MLD route is active while Internet is unavailable. " +
                $"Confirming network loss for {NetworkLossConfirmationMilliseconds} ms " +
                "before switching the active trip to offline A*. " +
                $"reason='{reason}'.");

            _ =
                RunNetworkLossFailoverAsync(
                    reason,
                    cancellationToken);
#endif
        }

        private async Task RunNetworkLossFailoverAsync(
            string reason,
            CancellationToken cancellationToken)
        {
#if ANDROID
            if (networkLossFailoverInProgress)
            {
                return;
            }

            try
            {
                await Task.Delay(
                    NetworkLossConfirmationMilliseconds,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (Connectivity.Current.NetworkAccess ==
                        NetworkAccess.Internet ||
                    !pageIsVisible ||
                    safeZoneConfirmed ||
                    activeRoute is null ||
                    !IsMldRoute(
                        activeRoute) ||
                    !activeDestinationCoordinate.HasValue)
                {
                    return;
                }

                networkLossFailoverInProgress =
                    true;

                int busyWaitMilliseconds =
                    0;

                while ((routeRequestInProgress ||
                        dynamicRerouteInProgress) &&
                       busyWaitMilliseconds <
                           NetworkFailoverMaximumBusyWaitMilliseconds)
                {
                    await Task.Delay(
                        NetworkFailoverBusyRetryMilliseconds,
                        cancellationToken);

                    busyWaitMilliseconds +=
                        NetworkFailoverBusyRetryMilliseconds;
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (routeRequestInProgress ||
                    dynamicRerouteInProgress)
                {
                    Log.Warn(
                        RerouteLogTag,
                        "Automatic MLD -> A* transition deferred because another " +
                        "route operation remained active. Existing MLD guidance is retained.");

                    return;
                }

                if (Connectivity.Current.NetworkAccess ==
                        NetworkAccess.Internet ||
                    activeRoute is null ||
                    !IsMldRoute(
                        activeRoute))
                {
                    return;
                }

                int arWaitMilliseconds =
                    0;

                while (arWaitMilliseconds <
                    NetworkFailoverArReadyWaitMilliseconds)
                {
                    ARCameraPoseBridge.SpatialSnapshot spatial =
                        ARCameraPoseBridge.CurrentFrame;

                    if (spatial.IsTracking &&
                        spatial.Pose.IsTracking &&
                        spatial.Anchor.IsAvailable)
                    {
                        break;
                    }

                    await Task.Delay(
                        250,
                        cancellationToken);

                    arWaitMilliseconds +=
                        250;
                }

                GeoCoordinate? failoverOrigin =
                    await ResolveNetworkFailoverOriginAsync(
                        cancellationToken);

                if (!failoverOrigin.HasValue ||
                    !failoverOrigin.Value.IsValid)
                {
                    Log.Warn(
                        RerouteLogTag,
                        "Automatic MLD -> A* transition could not obtain a valid " +
                        "offline GPS origin. Existing MLD route remains active.");

                    return;
                }

                if (Connectivity.Current.NetworkAccess ==
                    NetworkAccess.Internet)
                {
                    Log.Debug(
                        RerouteLogTag,
                        "Internet recovered before offline replacement began; " +
                        "MLD route retained.");

                    return;
                }

                Log.Warn(
                    RerouteLogTag,
                    "CONFIRMED NETWORK LOSS: transitioning active navigation " +
                    "from MLD to offline A*. The current MLD AR route will stay " +
                    "visible until the A* replacement is ready. " +
                    $"origin=({failoverOrigin.Value.Latitude:F7}," +
                    $"{failoverOrigin.Value.Longitude:F7}), reason='{reason}'.");

                bool switched =
                    await TryDynamicRerouteAsync(
                        failoverOrigin.Value,
                        "NETWORK_LOSS_MLD_TO_ASTAR",
                        forceOfflineAStar:
                            true);

                if (!switched &&
                    activeRoute is not null &&
                    IsMldRoute(
                        activeRoute))
                {
                    Log.Warn(
                        RerouteLogTag,
                        "MLD -> A* transition did not complete. The existing MLD " +
                        "route was intentionally retained rather than clearing guidance.");
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when Internet returns, destination changes, or page exits.
            }
            catch (Exception ex)
            {
                Log.Error(
                    RerouteLogTag,
                    "Automatic MLD -> A* transition FAILED. Existing route retained. " +
                    ex);
            }
            finally
            {
                networkLossFailoverInProgress =
                    false;

                CancellationTokenSource? completedCancellation =
                    connectivityFailoverCancellation;

                connectivityFailoverCancellation =
                    null;

                completedCancellation?.Dispose();
            }
#else
            await Task.CompletedTask;
#endif
        }

        private async Task<GeoCoordinate?> ResolveNetworkFailoverOriginAsync(
            CancellationToken cancellationToken)
        {
            GeoCoordinate? recentCoordinate;
            DateTimeOffset? recentTimestamp;

            lock (routeProgressFusionSync)
            {
                recentCoordinate =
                    latestGpsCoordinateForDeveloperReroute;

                recentTimestamp =
                    latestGpsTimestampForRouting;
            }

            if (recentCoordinate.HasValue &&
                recentCoordinate.Value.IsValid &&
                recentTimestamp.HasValue &&
                DateTimeOffset.UtcNow -
                    recentTimestamp.Value <=
                        TimeSpan.FromSeconds(
                            15))
            {
#if ANDROID
                Log.Debug(
                    RerouteLogTag,
                    "Using recent GPS route-progress fix as the offline A* " +
                    "failover origin.");
#endif
                return recentCoordinate.Value;
            }

            LocationReading? lastKnown =
                await _locationService.GetLastKnownLocationAsync(
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (lastKnown is not null &&
                lastKnown.Coordinate.IsValid &&
                DateTimeOffset.UtcNow -
                    lastKnown.Timestamp <=
                        TimeSpan.FromSeconds(
                            30))
            {
#if ANDROID
                Log.Debug(
                    RerouteLogTag,
                    "Using recent last-known GPS fix as the offline A* " +
                    "failover origin.");
#endif
                return lastKnown.Coordinate;
            }

#if ANDROID
            Log.Debug(
                RerouteLogTag,
                "Requesting a fresh GPS fix for the offline A* failover origin.");
#endif

            LocationReading? current =
                await _locationService.GetCurrentLocationAsync(
                    cancellationToken);

            return current?.Coordinate.IsValid ==
                true
                    ? current.Coordinate
                    : null;
        }

        private void CancelConnectivityFailover(
            string reason)
        {
            CancellationTokenSource? cancellation =
                connectivityFailoverCancellation;

            connectivityFailoverCancellation =
                null;

            if (cancellation is null)
            {
                return;
            }

#if ANDROID
            Log.Debug(
                RerouteLogTag,
                $"Cancelling pending network-loss failover: {reason}");
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

        private static bool IsMldRoute(
            RouteResult route)
        {
            return route.Algorithm.Contains(
                "MLD",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAStarRoute(
            RouteResult route)
        {
            return route.Algorithm.Contains(
                "AStar",
                StringComparison.OrdinalIgnoreCase) ||
                route.Algorithm.Contains(
                    "A*",
                    StringComparison.OrdinalIgnoreCase);
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

            navigationSessionStartedAt =
                null;

            activeDestinationName =
                string.Empty;

            activeDestinationCoordinate =
                null;

            activeDestinationSafeZoneRadiusMeters =
                SafeZoneConfirmationService.ArrivalRadiusMeters;

            indoorStationaryPollCount =
                0;

            lastPdrHeadingErrorDegrees =
                null;

            lastPdrHeadingSource =
                "Unavailable";

            lastPdrMotionCoherence =
                0.0;

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

            lastGpsRouteIdentityConfirmationCount =
                0;

            lastGpsReliabilityWeight =
                0.0;

            _gpsPdrFusionPolicy.Reset();

            _pdrHeadingSmoother.Reset();

            _offRouteReroutePolicy.Reset();

            _routeReplacementPolicy.Reset();

            _headingRevalidationPolicy.Reset();

            _hazardReroutingService.ResetSessionState();

            developerHazardValidationArmed =
                false;

            if (developerHazardRerouteTestButton is not null)
            {
                developerHazardRerouteTestButton.Text =
                    "DEV: Simulate Route Hazard";
            }

            dynamicRerouteInProgress =
                false;

            lastOffRouteCandidate =
                false;

            lastOffRouteConfirmationCount =
                0;

            ResetArRouteVisualMode(
                "navigation destination changed");

            lastRerouteResult =
                "None";

            latestGpsCoordinateForDeveloperReroute =
                null;

            latestGpsAccuracyForDeveloperReroute =
                null;

            latestGpsTimestampForRouting =
                null;

            lastArGuidanceConfidence =
                ARGuidanceConfidencePolicy.GuidanceConfidenceSnapshot.NotReady;

            ARCameraSpatialController.SetRouteRenderingEnabled(
                false,
                "Navigation destination changed; confidence must be rebuilt.");

            CancelConnectivityFailover(
                "navigation destination changed");

            ResetTurnGuidance();

            ResetSafeZoneConfirmation(
                "navigation destination changed");

            acceptedPdrStepCount =
                0;

            rejectedPdrStepCount =
                0;

            _routeProgressTracker.Clear();

            _mldArIntegrationService.ClearRoute();

            /*
             * Destination changes do not change the retained ARCore world
             * frame. Reuse its existing map-to-AR alignment.
             */
            lastHeadingAlignment =
                _headingAlignmentService.LastResult;

            Dispatcher.Dispatch(
                RefreshCameraModuleDynamicUi);

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

            _hazardReroutingService.ScheduleRemoteRefreshIfDue(
                Connectivity.Current.NetworkAccess ==
                    NetworkAccess.Internet);

            UpdateTurnGuidance();

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
                        "rollingHeadingWindow=4s, " +
                        "directionConfidenceBands=HIGH<=25deg, " +
                        "MEDIUM<=45deg, LOW<=70deg, REJECT>70deg, " +
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

            lastRouteMatchConfidence =
                RouteMatchConfidence.Unavailable;

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

                            latestGpsTimestampForRouting =
                                reading.Timestamp;
                        }

                        bool shouldStartDynamicReroute =
                            false;

                        bool verifiedOffCourseThisCycle =
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
                                    reading.AccuracyMeters,
                                    reading.CourseDegrees,
                                    reading.SpeedMetersPerSecond);

                            /*
                             * A rejected/no-fix GPS observation has no new
                             * segment identity evidence. Preserve the last
                             * real route match so one accuracy outage cannot
                             * immediately disable otherwise valid PDR.
                             */
                            if (matchedGps.MatchConfidence !=
                                RouteMatchConfidence.Unavailable)
                            {
                                lastRouteMatchConfidence =
                                    matchedGps.MatchConfidence;
                            }

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
                                    $"matchConfidence={offRouteDecision.MatchConfidence}, " +
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

                                verifiedOffCourseThisCycle =
                                    true;

                                rerouteOrigin =
                                    reading.Coordinate;

                                rerouteReason =
                                    offRouteDecision.Reason;
                            }

                            RouteProgressTracker.RouteProgressUpdate fusedGps =
                                matchedGps;

                            GpsPdrFusionPolicy.GpsFusionDecision gpsFusionDecision =
                                _gpsPdrFusionPolicy.EvaluateGps(
                                    beforeGps.HasProgress
                                        ? beforeGps.CommittedProgressMeters
                                        : 0.0,
                                    beforeGps.HasProgress,
                                    matchedGps);

                            lastGpsConfidence =
                                gpsFusionDecision.Confidence;

                            lastGpsFusionAction =
                                gpsFusionDecision.Action;

                            lastGpsPdrDivergenceMeters =
                                gpsFusionDecision.RawGpsMinusPreviousProgressMeters;

                            lastGpsBackwardConfirmationCount =
                                gpsFusionDecision.BackwardConfirmationCount;

                            lastGpsRouteIdentityConfirmationCount =
                                gpsFusionDecision.RouteIdentityConfirmationCount;

                            lastGpsReliabilityWeight =
                                gpsFusionDecision.ReliabilityWeight;

                            if (matchedGps.IsAccepted &&
                                !matchedGps.IsOffRoute)
                            {
                                if (Math.Abs(
                                        gpsFusionDecision.TargetProgressMeters -
                                        matchedGps.CommittedProgressMeters) >
                                    0.001)
                                {
                                    fusedGps =
                                        _routeProgressTracker.ApplyFusionCorrection(
                                            gpsFusionDecision.TargetProgressMeters,
                                            matchedGps,
                                            $"GPS_PDR_{gpsFusionDecision.Action}");
                                }

                                turnGuidanceUpdate =
                                    fusedGps;
                            }

#if ANDROID
                            LogDetailedDebug(
                                FusionLogTag,
                                "GPS/PDR fusion: " +
                                $"confidence={gpsFusionDecision.Confidence}, " +
                                $"reliability={gpsFusionDecision.ReliabilityWeight:F2}, " +
                                $"action={gpsFusionDecision.Action}, " +
                                $"rawGps={matchedGps.RawProgressMeters:F1} m, " +
                                $"before=" +
                                $"{(beforeGps.HasProgress ? beforeGps.CommittedProgressMeters.ToString("F1") : "0.0")} m, " +
                                $"target={gpsFusionDecision.TargetProgressMeters:F1} m, " +
                                $"gpsMinusPrevious=" +
                                $"{gpsFusionDecision.RawGpsMinusPreviousProgressMeters:F1} m, " +
                                $"backConfirmations=" +
                                $"{gpsFusionDecision.BackwardConfirmationCount}, " +
                                $"identityConfirmations=" +
                                $"{gpsFusionDecision.RouteIdentityConfirmationCount}, " +
                                $"routeIdentitySuspended=" +
                                $"{_gpsPdrFusionPolicy.IsRouteIdentitySuspended}, " +
                                $"reason='{gpsFusionDecision.Reason}'");
#endif

                            if (verifiedOffCourseThisCycle)
                            {
                                /*
                                 * Switch before publishing this GPS cycle so
                                 * the cyan visual immediately becomes the
                                 * access arrow instead of waiting for another
                                 * location poll after the 3/3 confirmation.
                                 */
                                EnterVerifiedOffCourseVisualMode(
                                    rerouteReason);
                            }

                            bool routeVisualModeChanged =
                                UpdateRoadFollowingVisualModeFromAcceptedGps(
                                    fusedGps);

                            publishedRealProgress =
                                TryApplyHeadingRevalidation(
                                    route,
                                    reading,
                                    fusedGps);

                            if (!publishedRealProgress &&
                                arRouteVisualMode ==
                                ArRouteVisualMode.ApproachOrOffCourseShort)
                            {
                                /*
                                 * Use the raw matched GPS update here even
                                 * when it is outside the accuracy-aware route
                                 * corridor. RouteProgressTracker still gives
                                 * us the nearest snapped route coordinate,
                                 * which is exactly where the access arrow
                                 * needs to point.
                                 */
                                publishedRealProgress =
                                    TryPublishApproachToRouteVisual(
                                        route,
                                        matchedGps,
                                        "GPS");
                            }
                            else if (!publishedRealProgress &&
                                     fusedGps.IsAccepted &&
                                     (fusedGps.ShouldPublishWindow ||
                                      routeVisualModeChanged))
                            {
                                publishedRealProgress =
                                    TryPublishMovingRouteWindow(
                                        route,
                                        fusedGps,
                                        routeVisualModeChanged
                                            ? "GPS/FUSION/VISUAL-MODE"
                                            : "GPS/FUSION");
                            }

                            RouteProgressTracker.ProgressSnapshot afterGps =
                                _routeProgressTracker.Current;

                            if (activeDestinationCoordinate.HasValue &&
                                afterGps.HasProgress)
                            {
                                GeoCoordinate safeZoneEvaluationCoordinate =
                                    activeDestinationCoordinate.Value;

                                double safeZoneEvaluationRadiusMeters =
                                    activeDestinationSafeZoneRadiusMeters;

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

                                    /*
                                     * Keep the Stage 5 synthetic test on the
                                     * original 30 m production baseline so
                                     * existing developer-validation behavior
                                     * remains deterministic.
                                     */
                                    safeZoneEvaluationRadiusMeters =
                                        SafeZoneConfirmationService.ArrivalRadiusMeters;

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
                                        safeZoneEvaluationRadiusMeters,
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

                        if ((publishedRealProgress ||
                             turnGuidanceUpdate.HasValue) &&
                            !safeZoneConfirmed)
                        {
                            UpdateTurnGuidance();
                        }

                        bool hazardRerouteOwnsThisCycle =
                            false;

                        if (!safeZoneConfirmed)
                        {
                            RouteProgressTracker.ProgressSnapshot hazardProgress;

                            lock (routeProgressFusionSync)
                            {
                                hazardProgress =
                                    _routeProgressTracker.Current;
                            }

                            if (hazardProgress.HasProgress)
                            {
                                hazardRerouteOwnsThisCycle =
                                    StartHazardRerouteIfNeeded(
                                        route,
                                        hazardProgress.CommittedProgressMeters,
                                        reading.Coordinate);
                            }
                        }

                        if (shouldStartDynamicReroute &&
                            !hazardRerouteOwnsThisCycle)
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

        private bool StartHazardRerouteIfNeeded(
            RouteResult route,
            double progressMeters,
            GeoCoordinate origin)
        {
#if ANDROID
            if (safeZoneConfirmed ||
                !pageIsVisible ||
                !origin.IsValid ||
                !ReferenceEquals(
                    route,
                    activeRoute) ||
                !activeDestinationCoordinate.HasValue)
            {
                return false;
            }

            HazardReroutingService.HazardRouteAssessment assessment =
                _hazardReroutingService.AssessRoute(
                    route,
                    progressMeters,
                    Connectivity.Current.NetworkAccess ==
                        NetworkAccess.Internet);

            if (!assessment.IsUnsafe ||
                assessment.PrimaryHazard is null)
            {
                return false;
            }

            RouteHazard hazard =
                assessment.PrimaryHazard;

            if (dynamicRerouteInProgress ||
                routeRequestInProgress)
            {
                Log.Debug(
                    HazardRerouteLogTag,
                    "Unsafe route is already known, but another route operation " +
                    "is active. Hazard-aware rerouting owns this GPS cycle and " +
                    "will be reconsidered on the next route-progress poll.");

                return true;
            }

            if (!_hazardReroutingService.TryReserveReroute(
                    hazard,
                    out string reservationReason))
            {
                Log.Debug(
                    HazardRerouteLogTag,
                    "Hazard reroute not repeated yet: " +
                    $"id='{hazard.Id}', reason='{reservationReason}'.");

                return true;
            }

            Log.Warn(
                HazardRerouteLogTag,
                "HAZARD-DRIVEN REROUTE TRIGGERED: " +
                $"id='{hazard.Id}', " +
                $"category='{hazard.Category}', " +
                $"severity='{hazard.Severity}', " +
                $"distanceAhead={assessment.DistanceAheadMeters:F1} m, " +
                $"activeAlgorithm='{route.Algorithm}', " +
                $"hazardsToAvoid={assessment.ActiveHazards.Count}. " +
                "Current AR guidance will remain visible until a safe " +
                "replacement route is publishable.");

            _ =
                TryDynamicRerouteAsync(
                    origin,
                    $"ROUTE_HAZARD:{hazard.Id}",
                    forceOfflineAStar: false,
                    hazardsToAvoid: assessment.ActiveHazards,
                    triggeringHazard: hazard);

            return true;
#else
            return false;
#endif
        }

        private void OnDeveloperHazardRerouteTestClicked(
            object? sender,
            EventArgs e)
        {
#if ANDROID
            if (!EnableDeveloperDynamicHazardValidation)
            {
                return;
            }

            if (developerHazardValidationArmed)
            {
                _hazardReroutingService.SetDeveloperHazard(
                    null);

                developerHazardValidationArmed =
                    false;

                developerHazardRerouteTestButton.Text =
                    "DEV: Simulate Route Hazard";

                Log.Warn(
                    HazardRerouteLogTag,
                    "[DEV HAZARD] Controlled route hazard cleared.");

                return;
            }

            RouteResult? route =
                activeRoute;

            RouteProgressTracker.ProgressSnapshot progress;

            lock (routeProgressFusionSync)
            {
                progress =
                    _routeProgressTracker.Current;
            }

            if (route is null ||
                route.Points.Count < 2 ||
                !progress.HasProgress)
            {
                Log.Warn(
                    HazardRerouteLogTag,
                    "[DEV HAZARD] Cannot arm yet. Wait for an active route and " +
                    "at least one GPS/PDR progress observation.");

                return;
            }

            double hazardProgressMeters =
                Math.Min(
                    route.TotalDistanceMeters,
                    progress.CommittedProgressMeters +
                    DeveloperHazardAheadMeters);

            if (hazardProgressMeters -
                    progress.CommittedProgressMeters <
                DeveloperHazardAheadMeters *
                    0.60)
            {
                Log.Warn(
                    HazardRerouteLogTag,
                    "[DEV HAZARD] Route is too close to the destination to " +
                    "place a useful simulated hazard ahead.");

                return;
            }

            if (!TryGetRouteCoordinateAtProgress(
                    route,
                    hazardProgressMeters,
                    out GeoCoordinate hazardCoordinate))
            {
                Log.Warn(
                    HazardRerouteLogTag,
                    "[DEV HAZARD] Could not interpolate a hazard coordinate " +
                    "from the current route geometry.");

                return;
            }

            RouteHazard hazard =
                new(
                    "dev-stage10-route-hazard",
                    hazardCoordinate,
                    DeveloperHazardRadiusMeters,
                    "Road Hazard",
                    "High",
                    "DEV simulated blocked route",
                    "Stage 10 developer validation",
                    DateTimeOffset.UtcNow);

            _hazardReroutingService.SetDeveloperHazard(
                hazard);

            developerHazardValidationArmed =
                true;

            developerHazardRerouteTestButton.Text =
                "DEV: Clear Route Hazard";

            GeoCoordinate rerouteOrigin =
                default;

            lock (routeProgressFusionSync)
            {
                if (latestGpsCoordinateForDeveloperReroute.HasValue)
                {
                    rerouteOrigin =
                        latestGpsCoordinateForDeveloperReroute.Value;
                }
            }

            if (!rerouteOrigin.IsValid &&
                !TryGetRouteCoordinateAtProgress(
                    route,
                    progress.CommittedProgressMeters,
                    out rerouteOrigin))
            {
                Log.Warn(
                    HazardRerouteLogTag,
                    "[DEV HAZARD] Hazard was armed, but a current reroute origin " +
                    "is not yet available. The normal GPS loop will trigger it.");

                return;
            }

            Log.Warn(
                HazardRerouteLogTag,
                "[DEV HAZARD] ARMED on current route: " +
                $"currentProgress={progress.CommittedProgressMeters:F1} m, " +
                $"hazardAhead={DeveloperHazardAheadMeters:F1} m, " +
                $"hazard=({hazardCoordinate.Latitude:F7},{hazardCoordinate.Longitude:F7}), " +
                $"radius={DeveloperHazardRadiusMeters:F1} m. " +
                "Route/hazard detection and provider-aware avoidance remain production logic.");

            StartHazardRerouteIfNeeded(
                route,
                progress.CommittedProgressMeters,
                rerouteOrigin);
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
            string reason,
            bool forceOfflineAStar = false,
            IReadOnlyList<RouteHazard>? hazardsToAvoid = null,
            RouteHazard? triggeringHazard = null)
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

            bool hazardAware =
                hazardsToAvoid is { Count: > 0 };

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

                if (hazardAware &&
                    triggeringHazard is not null)
                {
                    ApplyPrototypeHazardReroutingState(
                        triggeringHazard);
                }
                else
                {
                    ApplyPrototypeReroutingState();
                }

                RouteResult? replacementRoute;

                if (hazardAware)
                {
                    replacementRoute =
                        await _hybridRoutingService.FindHazardAvoidingRouteAsync(
                            origin,
                            destination,
                            hazardsToAvoid!,
                            cancellationToken);
                }
                else if (forceOfflineAStar)
                {
                    replacementRoute =
                        await _hybridRoutingService.FindOfflineRouteAsync(
                            origin,
                            destination,
                            cancellationToken);
                }
                else
                {
                    replacementRoute =
                        await _mldArIntegrationService.RequestRouteAsync(
                            origin,
                            destination,
                            cancellationToken);
                }

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
                        "Replacement route is ready, but ARCore/ground anchor is not currently usable. " +
                        "Retaining the existing route; a later confirmed off-route sequence may retry.");

                    return false;
                }

                float arOriginOffsetX =
                    spatial.Pose.PositionX -
                    spatial.Anchor.PositionX;

                float arOriginOffsetZ =
                    spatial.Pose.PositionZ -
                    spatial.Anchor.PositionZ;

                bool published =
                    false;

                bool destinationStillCurrent;

                RouteReplacementDecision replacementDecision =
                    default;

                lock (routeProgressFusionSync)
                {
                    destinationStillCurrent =
                        activeDestinationCoordinate.HasValue &&
                        activeDestinationCoordinate.Value ==
                            destination &&
                        string.Equals(
                            activeDestinationName,
                            destinationName,
                            StringComparison.Ordinal);

                    if (destinationStillCurrent)
                    {
                        replacementDecision =
                            _routeReplacementPolicy.Evaluate(
                                activeRoute,
                                _routeProgressTracker.Current,
                                replacementRoute,
                                origin,
                                destination,
                                explicitlyJustifiedDetour:
                                    hazardAware,
                                timestampUtc:
                                    DateTimeOffset.UtcNow);
                    }

                    if (destinationStillCurrent &&
                        replacementDecision.IsAccepted)
                    {
                        published =
                            _mldArIntegrationService.PublishProgressWindow(
                                replacementRoute,
                                0.0,
                                origin,
                                activeMapToArYawDegrees,
                                arOriginOffsetX,
                                arOriginOffsetZ,
                                arWindowMeters:
                                    GetCurrentArRouteVisualWindowMeters(),
                                clearRouteOnFailure:
                                    false,
                                sourceSegmentIndex:
                                    0);
                    }

                    if (published)
                    {
                        activeRoute =
                            replacementRoute;

                        _routeProgressTracker.SetRoute(
                            replacementRoute);

                        lastRouteMatchConfidence =
                            RouteMatchConfidence.Unavailable;

                        _routeProgressTracker.MarkWindowPublished(
                            0.0);

                        recoveryConnectorVerified =
                            false;

                        _gpsPdrFusionPolicy.Reset();

                        _pdrHeadingSmoother.Reset();

                        _headingRevalidationPolicy.Reset();

                        _offRouteReroutePolicy.MarkRerouteCompleted(
                            DateTimeOffset.UtcNow);

                        lastOffRouteCandidate =
                            false;

                        lastOffRouteConfirmationCount =
                            0;
                    }
                }

                if (!destinationStillCurrent)
                {
                    lastRerouteResult =
                        "DestinationChanged";

                    Log.Debug(
                        RerouteLogTag,
                        "Dynamic reroute discarded because the destination changed before atomic route publication.");

                    return false;
                }

                Log.Warn(
                    RerouteLogTag,
                    "Replacement route validation: " +
                    $"disposition={replacementDecision.Disposition}, " +
                    $"previousRemaining={replacementDecision.PreviousRemainingMeters:F1} m, " +
                    $"replacementDistance={replacementDecision.ReplacementDistanceMeters:F1} m, " +
                    $"increase={replacementDecision.DistanceIncreaseMeters:F1} m, " +
                    $"reason='{replacementDecision.Reason}'.");

                if (!replacementDecision.IsAccepted)
                {
                    lastRerouteResult =
                        replacementDecision.Disposition ==
                            RouteReplacementDisposition.RequiresConfirmation
                            ? "AwaitingRouteConfirmation"
                            : "ReplacementRejected";

                    Log.Warn(
                        RerouteLogTag,
                        "Replacement route was not accepted. Existing route, progress, AR geometry, and displayed distance remain unchanged.");

                    return false;
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

                UpdateTurnGuidance();

                lastRerouteResult =
                    hazardAware
                        ? "HazardComplete"
                        : forceOfflineAStar
                            ? "OfflineFailoverComplete"
                            : "Complete";

                string completionMessage =
                    (hazardAware
                        ? "HAZARD REROUTE COMPLETE: "
                        : forceOfflineAStar
                            ? "NETWORK FAILOVER MLD -> A* COMPLETE: "
                            : "DYNAMIC REROUTE COMPLETE: ") +
                    $"algorithm='{replacementRoute.Algorithm}', " +
                    $"points={replacementRoute.Points.Count}, " +
                    $"distance={replacementRoute.TotalDistanceMeters:F1} m, " +
                    $"routeVersion={ARRouteBridge.Current.Version}, " +
                    $"arOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m" +
                    (hazardAware
                        ? $", triggeringHazard='{triggeringHazard?.Id ?? "<unknown>"}', " +
                          $"avoidedHazards={hazardsToAvoid!.Count}."
                        : ".");

                Log.Warn(
                    RerouteLogTag,
                    completionMessage);

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

                if (lastRerouteResult !=
                        "Complete" &&
                    lastRerouteResult !=
                        "OfflineFailoverComplete" &&
                    lastRerouteResult !=
                        "HazardComplete" &&
                    lastTurnGuidance.IsAvailable)
                {
                    Dispatcher.Dispatch(
                        () =>
                        {
                            if (currentCameraModuleView ==
                                    CameraModuleViewMode.ArCamera &&
                                !emergencyAdvisoryVisible &&
                                !safeZoneConfirmed)
                            {
                                ApplyPrototypeTurnGuidance(
                                    lastTurnGuidance);

                                turnGuidancePanel.IsVisible =
                                    true;
                            }
                        });
                }
            }
#else
            await Task.CompletedTask;
            return false;
#endif
        }

        private void UpdateTurnGuidance()
        {
            RouteResult? acceptedRoute;

            RouteProgressTracker.ProgressSnapshot acceptedProgress;

            ARRouteBridge.RouteSnapshot visibleRoute;

            /*
             * Capture the accepted geographic route, its matched progress, and
             * the currently displayed AR route under the same outer lock used
             * by route replacement and GPS/PDR fusion. Turn geometry stays tied
             * to the visible AR window, while the displayed total remaining
             * distance comes only from accepted tracker progress.
             */
            lock (routeProgressFusionSync)
            {
                acceptedRoute =
                    activeRoute;

                acceptedProgress =
                    _routeProgressTracker.Current;

                visibleRoute =
                    ARRouteBridge.Current;
            }

            RouteNavigationState navigationState =
                visibleRoute.NavigationState;

            bool visibleRouteMatchesAcceptedRoute =
                acceptedRoute is not null &&
                string.Equals(
                    visibleRoute.Algorithm,
                    acceptedRoute.Algorithm,
                    StringComparison.Ordinal) &&
                double.IsFinite(
                    visibleRoute.TotalDistanceMeters) &&
                double.IsFinite(
                    acceptedRoute.TotalDistanceMeters) &&
                Math.Abs(
                    visibleRoute.TotalDistanceMeters -
                    acceptedRoute.TotalDistanceMeters) <=
                        0.50;

            if (acceptedRoute is null ||
                !acceptedProgress.HasRoute ||
                !visibleRoute.IsAvailable ||
                !visibleRouteMatchesAcceptedRoute ||
                !navigationState.IsAvailable ||
                navigationState.VisualKind !=
                    RouteVisualKind.RouteWindow)
            {
                lastTurnGuidance =
                    PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable;

#if ANDROID
                Log.Debug(
                    TurnLogTag,
                    "TURN GUIDANCE HELD: visible AR geometry is not a " +
                    "validated window for the accepted route/progress state. " +
                    $"routeVersion={visibleRoute.Version}, " +
                    $"visualKind={navigationState.VisualKind}, " +
                    $"routeMatches={visibleRouteMatchesAcceptedRoute}, " +
                    $"acceptedProgress={acceptedProgress.HasRoute}.");
#endif

                Dispatcher.Dispatch(
                    () =>
                    {
                        turnGuidancePanel.IsVisible =
                            false;
                    });

                return;
            }

            PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance =
                _turnGuidanceService.Evaluate(
                    acceptedRoute,
                    navigationState.WindowStartProgressMeters,
                    navigationState.SourceSegmentIndex);

            if (guidance.IsAvailable)
            {
                guidance =
                    guidance with
                    {
                        RemainingRouteMeters =
                            Math.Max(
                                0.0,
                                acceptedProgress.RemainingMeters)
                    };
            }

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
                    $"visualProgress={navigationState.WindowStartProgressMeters:F1} m, " +
                    $"acceptedProgress={acceptedProgress.CommittedProgressMeters:F1} m, " +
                    $"publicationLag=" +
                    $"{Math.Max(0.0, acceptedProgress.CommittedProgressMeters - navigationState.WindowStartProgressMeters):F1} m, " +
                    $"sourceSegment={navigationState.SourceSegmentIndex}, " +
                    $"routeVersion={visibleRoute.Version}");

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
                        currentCameraModuleView !=
                            CameraModuleViewMode.ArCamera ||
                        emergencyAdvisoryVisible ||
                        safeZoneConfirmed)
                    {
                        turnGuidancePanel.IsVisible =
                            false;

                        return;
                    }

                    ApplyPrototypeTurnGuidance(
                        guidance);

                    turnGuidancePanel.IsVisible =
                        true;
                });
        }

        private void ApplyPrototypeTurnGuidance(
            PedestrianTurnGuidanceService.TurnGuidanceSnapshot guidance)
        {
            double instructionDistance =
                double.IsFinite(guidance.DistanceToTurnMeters)
                    ? Math.Max(0.0, guidance.DistanceToTurnMeters)
                    : Math.Max(0.0, guidance.RemainingRouteMeters);

            string distanceText =
                $"{instructionDistance:F0} meters";

            bool correctiveGuidance =
                guidance.Instruction ==
                    PedestrianTurnGuidanceService.TurnInstruction.UTurn;

            /*
             * The Figma prototype uses a pale green corrective card for
             * "Go back" while ordinary route instructions use the neutral
             * translucent white card. Keep this strictly presentation-only;
             * the underlying turn classifier and navigation thresholds are
             * unchanged.
             */
            if (correctiveGuidance)
            {
                turnGuidancePanel.BackgroundColor =
                    Color.FromArgb("#DCEFE5");

                turnGuidancePanel.Stroke =
                    new SolidColorBrush(
                        Color.FromArgb("#BBDCC9"));

                turnInstructionLabel.TextColor =
                    Color.FromArgb("#08723F");

                turnDistanceLabel.TextColor =
                    Color.FromArgb("#176A43");

                turnChevronImage.Source =
                    "lucide_chevron_down_green.png";
            }
            else
            {
                turnGuidancePanel.BackgroundColor =
                    Color.FromArgb("#E8FFFFFF");

                turnGuidancePanel.Stroke =
                    new SolidColorBrush(
                        Color.FromArgb("#D7DADD"));

                turnInstructionLabel.TextColor =
                    Color.FromArgb("#151515");

                turnDistanceLabel.TextColor =
                    Color.FromArgb("#222222");

                turnChevronImage.Source =
                    "lucide_chevron_down_black.png";
            }

            switch (guidance.Instruction)
            {
                case PedestrianTurnGuidanceService.TurnInstruction.SlightLeft:
                    turnDirectionIconLabel.Source =
                        "lucide_arrow_up_left_teal.png";
                    turnInstructionLabel.Text =
                        $"Bear left for {distanceText}";
                    break;

                case PedestrianTurnGuidanceService.TurnInstruction.Left:
                case PedestrianTurnGuidanceService.TurnInstruction.SharpLeft:
                    turnDirectionIconLabel.Source =
                        "lucide_corner_up_left_teal.png";
                    turnInstructionLabel.Text =
                        $"Turn left in {distanceText}";
                    break;

                case PedestrianTurnGuidanceService.TurnInstruction.SlightRight:
                    turnDirectionIconLabel.Source =
                        "lucide_arrow_up_right_teal.png";
                    turnInstructionLabel.Text =
                        $"Bear right for {distanceText}";
                    break;

                case PedestrianTurnGuidanceService.TurnInstruction.Right:
                case PedestrianTurnGuidanceService.TurnInstruction.SharpRight:
                    turnDirectionIconLabel.Source =
                        "lucide_corner_up_right_teal.png";
                    turnInstructionLabel.Text =
                        $"Turn right in {distanceText}";
                    break;

                case PedestrianTurnGuidanceService.TurnInstruction.UTurn:
                    turnDirectionIconLabel.Source =
                        "lucide_undo_2_green.png";
                    turnInstructionLabel.Text =
                        $"Go back for {distanceText}";
                    break;

                case PedestrianTurnGuidanceService.TurnInstruction.Arrive:
                    turnDirectionIconLabel.Source =
                        "lucide_circle_check_big_teal.png";
                    turnInstructionLabel.Text =
                        "Safe zone is just ahead";
                    break;

                default:
                    turnDirectionIconLabel.Source =
                        "lucide_arrow_up_teal.png";
                    turnInstructionLabel.Text =
                        $"Proceed straight for {distanceText}";
                    break;
            }

            turnDistanceLabel.Text =
                $"{Math.Max(0.0, guidance.RemainingRouteMeters):F0} meters away from the " +
                "nearest evacuation center";
        }

        private void ApplyPrototypeHazardReroutingState(
            RouteHazard hazard)
        {
            Dispatcher.Dispatch(
                () =>
                {
                    if (currentCameraModuleView !=
                            CameraModuleViewMode.ArCamera ||
                        emergencyAdvisoryVisible ||
                        safeZoneConfirmed)
                    {
                        return;
                    }

                    turnGuidancePanel.BackgroundColor =
                        Color.FromArgb("#FFF4DE");

                    turnGuidancePanel.Stroke =
                        new SolidColorBrush(
                            Color.FromArgb("#E7B85D"));

                    turnInstructionLabel.TextColor =
                        Color.FromArgb("#8A4B00");

                    turnDistanceLabel.TextColor =
                        Color.FromArgb("#7B5700");

                    turnDirectionIconLabel.Source =
                        "lucide_ellipsis_amber.png";

                    turnChevronImage.Source =
                        "lucide_chevron_down_amber.png";

                    turnInstructionLabel.Text =
                        "Hazard ahead - finding a safer route";

                    turnDistanceLabel.Text =
                        string.IsNullOrWhiteSpace(hazard.Title)
                            ? $"{hazard.Category} reported on the current route."
                            : hazard.Title;

                    turnGuidancePanel.IsVisible =
                        true;
                });
        }

        private void ApplyPrototypeReroutingState()
        {
            Dispatcher.Dispatch(
                () =>
                {
                    if (currentCameraModuleView !=
                            CameraModuleViewMode.ArCamera ||
                        emergencyAdvisoryVisible ||
                        safeZoneConfirmed)
                    {
                        return;
                    }

                    turnGuidancePanel.BackgroundColor =
                        Color.FromArgb("#E8E3D0");

                    turnGuidancePanel.Stroke =
                        new SolidColorBrush(
                            Color.FromArgb("#D8CFA6"));

                    turnInstructionLabel.TextColor =
                        Color.FromArgb("#8A6500");

                    turnDistanceLabel.TextColor =
                        Color.FromArgb("#8A6500");

                    turnDirectionIconLabel.Source =
                        "lucide_ellipsis_amber.png";

                    turnChevronImage.Source =
                        "lucide_chevron_down_amber.png";

                    turnInstructionLabel.Text =
                        "Recalculating route...";

                    turnDistanceLabel.Text =
                        "Please wait while guidance is updated.";

                    turnGuidancePanel.IsVisible =
                        true;
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

                    turnDirectionIconLabel.Source =
                        "lucide_arrow_up_teal.png";

                    turnGuidancePanel.BackgroundColor =
                        Color.FromArgb("#E8FFFFFF");

                    turnGuidancePanel.Stroke =
                        new SolidColorBrush(
                            Color.FromArgb("#D7DADD"));

                    turnInstructionLabel.TextColor =
                        Color.FromArgb("#151515");

                    turnDistanceLabel.TextColor =
                        Color.FromArgb("#222222");

                    turnChevronImage.Source =
                        "lucide_chevron_down_black.png";

                    turnInstructionLabel.Text =
                        "Proceed straight";

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
                    $"safeZoneRadius={decision.ArrivalRadiusMeters:F1} m, " +
                    $"remaining={decision.RemainingRouteMeters:F1} m/" +
                    $"{decision.MaximumRemainingRouteMeters:F1} m, " +
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
                    $"safeZoneRadius={decision.ArrivalRadiusMeters:F1} m, " +
                    $"remaining={decision.RemainingRouteMeters:F1} m/" +
                    $"{decision.MaximumRemainingRouteMeters:F1} m, " +
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

            Dispatcher.Dispatch(
                RefreshCameraModuleDynamicUi);

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
                $"safeZoneRadius={decision.ArrivalRadiusMeters:F1} m, " +
                $"remaining={decision.RemainingRouteMeters:F1} m/" +
                $"{decision.MaximumRemainingRouteMeters:F1} m, " +
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

                    RouteProgressTracker.ProgressSnapshot safeZoneProgress =
                        _routeProgressTracker.Current;

                    double travelledMeters =
                        safeZoneProgress.HasProgress
                            ? Math.Max(0.0, safeZoneProgress.CommittedProgressMeters)
                            : 0.0;

                    safeZoneDistanceTravelledLabel.Text =
                        $"{travelledMeters:F0} meters";

                    TimeSpan elapsed =
                        navigationSessionStartedAt.HasValue
                            ? DateTimeOffset.UtcNow - navigationSessionStartedAt.Value
                            : TimeSpan.Zero;

                    int elapsedMinutes =
                        Math.Max(
                            1,
                            (int)Math.Ceiling(elapsed.TotalMinutes));

                    safeZoneTimeTakenLabel.Text =
                        $"{elapsedMinutes} minutes";

                    safeZoneDetailsLabel.Text =
                        $"Safe-zone entry confirmed within the " +
                        $"{decision.ArrivalRadiusMeters:F0} m evacuation-center vicinity " +
                        $"({decision.DistanceToDestinationMeters:F0} m from the destination point)." +
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

                    safeZoneDistanceTravelledLabel.Text =
                        "0 meters";

                    safeZoneTimeTakenLabel.Text =
                        "0 minutes";

                    if (developerSafeZoneTestButton is not null)
                    {
                        developerSafeZoneTestButton.Text =
                            "DEV: Arm Safe Zone Test";

                        developerSafeZoneTestButton.IsEnabled =
                            true;
                    }

                    RefreshCameraModuleDynamicUi();
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
                $"devArrivalRadius={SafeZoneConfirmationService.ArrivalRadiusMeters:F1} m. " +
                "The DEV target intentionally keeps the original 30 m baseline so the existing test remains deterministic. Stay near the arming point and wait for three DISTINCT qualifying GPS observations.");
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

            bool renderLocalDepthInAr =
                hasLocalArDepth &&
                currentCameraModuleView ==
                    CameraModuleViewMode.FloodDepth &&
                pageIsVisible;

            if (renderLocalDepthInAr)
            {
                ARFloodDepthBridge.PublishLocalDepth(
                    snapshot.LocalDepthMeters!.Value,
                    snapshot.SourceText);
            }
            else
            {
                ARFloodDepthBridge.Clear(
                    hasLocalArDepth
                        ? "local depth retained; Flood Depth sub-tab is not active"
                        : $"{snapshot.Mode} has no trusted local street-depth value");
            }

            Dispatcher.Dispatch(
                () =>
                {
                    floodVisualizationTitleLabel.Text =
                        snapshot.Title;

                    floodVisualizationPrimaryLabel.Text =
                        hasLocalArDepth
                            ? $"Simulating {snapshot.LocalDepthMeters!.Value:0.0#} meters of flood depth near you"
                            : snapshot.PrimaryText;

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
                        currentCameraModuleView ==
                            CameraModuleViewMode.FloodDepth &&
                        !safeZoneConfirmed;

                    floodWaitingBanner.IsVisible =
                        false;

                    floodModeDepthSummaryLabel.Text =
                        snapshot.PrimaryText;

                    RefreshCameraModuleDynamicUi();
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
                $"arSpaceWater={renderLocalDepthInAr}, " +
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

            bool floodModeActive =
                currentCameraModuleView ==
                    CameraModuleViewMode.FloodDepth;

            bool shouldShow =
                visible &&
                floodModeActive;

            if (!shouldShow)
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
            else
            {
                ARFloodDepthBridge.Clear(
                    "Flood Depth sub-tab has context but no trusted local depth");
            }

            Dispatcher.Dispatch(
                () =>
                {
                    floodVisualizationLayer.IsVisible =
                        shouldShow &&
                        currentFloodVisualization.IsAvailable &&
                        pageIsVisible &&
                        !safeZoneConfirmed;

                    floodWaitingBanner.IsVisible =
                        floodModeActive &&
                        !currentFloodVisualization.IsAvailable &&
                        pageIsVisible &&
                        !safeZoneConfirmed;
                });

#if ANDROID
            Log.Debug(
                FloodDepthLogTag,
                $"Flood visualization visibility={shouldShow}; " +
                $"arSpaceWater={(shouldShow && hasLocalArDepth)}; " +
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
                {
                    floodVisualizationLayer.IsVisible =
                        false;

                    floodWaitingBanner.IsVisible =
                        currentCameraModuleView ==
                            CameraModuleViewMode.FloodDepth &&
                        pageIsVisible &&
                        !safeZoneConfirmed;

                    floodModeDepthSummaryLabel.Text =
                        "No trusted local depth is currently available";

                    RefreshCameraModuleDynamicUi();
                });

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

            lastEmergencyAdvisoryForStatus =
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

                    RefreshEmergencyStatusBanner();
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

                ApplyCameraModuleView(
                    CameraModuleViewMode.ArCamera,
                    "emergency AR evacuation guidance proceeding");

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
                        currentCameraModuleView !=
                            CameraModuleViewMode.ArCamera ||
                        safeZoneConfirmed ||
                        !pageIsVisible ||
                        !lastTurnGuidance.IsAvailable)
                    {
                        return;
                    }

                    ApplyPrototypeTurnGuidance(
                        lastTurnGuidance);

                    turnGuidancePanel.IsVisible =
                        true;

                    RefreshEmergencyStatusBanner();
                });

            Dispatcher.Dispatch(
                RefreshEmergencyStatusBanner);

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

        private async void OnViewGuidanceSessionDetailsClicked(
            object? sender,
            EventArgs e)
        {
            try
            {
                if (Shell.Current is not null)
                {
                    await Shell.Current.GoToAsync(
                        "SummaryPage");
                }
            }
            catch (Exception exception)
            {
#if ANDROID
                Log.Warn(
                    SafeZoneLogTag,
                    $"Opening guidance session details failed: {exception.Message}");
#endif
            }
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

        /// <summary>
        /// Revalidates the active map-to-AR yaw from the same accepted route
        /// segment and GPS progress used by visible and textual guidance.
        /// </summary>
        private bool TryApplyHeadingRevalidation(
            RouteResult route,
            LocationReading reading,
            RouteProgressTracker.RouteProgressUpdate update)
        {
#if ANDROID
            if (!update.IsAccepted ||
                update.IsOffRoute ||
                !TryGetRouteSegmentBearing(
                    route,
                    update.SegmentIndex,
                    out double routeTangentBearingDegrees))
            {
                return false;
            }

            ARCameraPoseBridge.SpatialSnapshot spatial =
                ARCameraPoseBridge.CurrentFrame;

            if (!spatial.IsTracking ||
                !spatial.Pose.IsTracking ||
                !spatial.Anchor.IsAvailable)
            {
                return false;
            }

            HeadingRevalidationDecision decision =
                _headingRevalidationPolicy.Evaluate(
                    activeMapToArYawDegrees,
                    reading.Coordinate,
                    reading.AccuracyMeters,
                    reading.SpeedMetersPerSecond,
                    reading.CourseDegrees,
                    update.MatchConfidence,
                    routeTangentBearingDegrees,
                    spatial.Pose.PositionX,
                    spatial.Pose.PositionZ,
                    DateTimeOffset.UtcNow);

            if (decision.Disposition ==
                    HeadingRevalidationDisposition.AwaitingConfirmation ||
                decision.Disposition ==
                    HeadingRevalidationDisposition.SignalsDisagree ||
                decision.Disposition ==
                    HeadingRevalidationDisposition.CorrectionCooldown)
            {
                Log.Debug(
                    HeadingLogTag,
                    "HEADING REVALIDATION: " +
                    $"state={decision.Disposition}, " +
                    $"confirmation={decision.ConfirmationCount}/" +
                    $"{decision.RequiredConfirmationCount}, " +
                    $"gpsMove={decision.GpsDisplacementMeters:F1} m, " +
                    $"arMove={decision.ArDisplacementMeters:F1} m, " +
                    $"gpsBearing={decision.GpsDisplacementBearingDegrees:F1} deg, " +
                    $"gpsCourse={decision.GpsCourseDegrees:F1} deg, " +
                    $"routeBearing={decision.RouteTangentBearingDegrees:F1} deg, " +
                    $"arMovement={decision.ArMovementAzimuthDegrees:F1} deg, " +
                    $"candidateYaw={decision.CandidateYawDegrees:F1} deg, " +
                    $"error={decision.AlignmentErrorDegrees:F1} deg, " +
                    $"reason='{decision.Reason}'.");
            }

            if (!decision.ShouldApplyCorrection)
            {
                return false;
            }

            double previousYawDegrees =
                activeMapToArYawDegrees;

            activeMapToArYawDegrees =
                decision.CorrectedYawDegrees;

            bool published =
                TryPublishMovingRouteWindow(
                    route,
                    update,
                    "GPS/HEADING-REVALIDATION");

            if (!published)
            {
                activeMapToArYawDegrees =
                    previousYawDegrees;

                Log.Warn(
                    HeadingLogTag,
                    "Movement-confirmed heading correction was not applied because the corrected local route window could not be published.");

                return false;
            }

            _headingRevalidationPolicy.MarkCorrectionApplied(
                DateTimeOffset.UtcNow);

            double residualAlignmentErrorDegrees =
                NormalizeSignedDegrees(
                    decision.CandidateYawDegrees -
                    decision.CorrectedYawDegrees);

            lastHeadingAlignment =
                _headingAlignmentService.ApplyMovementValidatedYaw(
                    decision.CorrectedYawDegrees,
                    decision.ConfirmationCount,
                    residualAlignmentErrorDegrees,
                    DateTimeOffset.UtcNow);

            Log.Warn(
                HeadingLogTag,
                "HEADING REVALIDATION APPLIED: " +
                $"oldYaw={previousYawDegrees:F1} deg, " +
                $"candidateYaw={decision.CandidateYawDegrees:F1} deg, " +
                $"appliedCorrection={decision.AppliedCorrectionDegrees:F1} deg, " +
                $"newYaw={decision.CorrectedYawDegrees:F1} deg, " +
                $"observedError={decision.AlignmentErrorDegrees:F1} deg, " +
                $"confirmations={decision.ConfirmationCount}/" +
                $"{decision.RequiredConfirmationCount}, " +
                $"segment={update.SegmentIndex}.");

            return true;
#else
            return false;
#endif
        }

        private static bool TryGetRouteSegmentBearing(
            RouteResult route,
            int segmentIndex,
            out double bearingDegrees)
        {
            bearingDegrees =
                0.0;

            if (segmentIndex <
                    0 ||
                segmentIndex +
                    1 >=
                    route.Points.Count)
            {
                return false;
            }

            GeoCoordinate from =
                route.Points[segmentIndex]
                    .Coordinate;

            GeoCoordinate to =
                route.Points[segmentIndex + 1]
                    .Coordinate;

            if (from.DistanceTo(
                    to) <
                0.50)
            {
                return false;
            }

            bearingDegrees =
                CalculateInitialBearingDegrees(
                    from,
                    to);

            return double.IsFinite(
                bearingDegrees);
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

            if (!IndoorRouteTestMode &&
                lastRouteMatchConfidence <
                RouteMatchConfidence.Medium)
            {
                rejectedPdrStepCount++;

                LogDetailedDebug(
                    PdrLogTag,
                    "PDR step held because the current route-segment match " +
                    $"is not trustworthy: confidence={lastRouteMatchConfidence}.");

                return;
            }

            if (!IndoorRouteTestMode &&
                _gpsPdrFusionPolicy.IsRouteIdentitySuspended)
            {
                rejectedPdrStepCount++;

                LogDetailedDebug(
                    PdrLogTag,
                    "PDR step held while GPS route identity is unresolved. " +
                    $"step={e.StepNumber}.");

                return;
            }

            if (!TryGetPdrDirectionAgreement(
                    out double cameraAzimuthDegrees,
                    out double routeAzimuthDegrees,
                    out double headingErrorDegrees,
                    out PdrHeadingSmoother.HeadingEstimate headingEstimate,
                    out string unavailableReason))
            {
                rejectedPdrStepCount++;

                LogDetailedDebug(
                    PdrLogTag,
                    "PDR step held: route-direction validation unavailable. " +
                    $"step={e.StepNumber}, reason={unavailableReason}");

                return;
            }

            lastPdrHeadingErrorDegrees =
                headingErrorDegrees;

            lastPdrHeadingSource =
                headingEstimate.Reason;

            lastPdrMotionCoherence =
                headingEstimate.MotionCoherence;

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

                LogDetailedDebug(
                    PdrLogTag,
                    "PDR step REJECTED by confidence gate: " +
                    $"step={e.StepNumber}, " +
                    $"smoothedCameraArAzimuth={cameraAzimuthDegrees:F1} deg, " +
                    $"walkingArAzimuth=" +
                    $"{(headingEstimate.UsedWalkingVector ? headingEstimate.WalkingAzimuthDegrees.ToString("F1") : "<accumulating>")} deg, " +
                    $"routeArAzimuth={routeAzimuthDegrees:F1} deg, " +
                    $"error={headingErrorDegrees:F1} deg, " +
                    $"motionCoherence={headingEstimate.MotionCoherence:F2}, " +
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

                if (arRouteVisualMode ==
                        ArRouteVisualMode.RoadFollowingLong &&
                    update.IsAccepted &&
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

                LogDetailedDebug(
                    PdrLogTag,
                    "PDR step could not advance route progress: " +
                    $"step={e.StepNumber}, " +
                    $"reason={update.RejectionReason}");

                return;
            }

            acceptedPdrStepCount++;

            UpdateTurnGuidance();

            LogDetailedDebug(
                PdrLogTag,
                "PDR step ACCEPTED: " +
                $"step={e.StepNumber}, " +
                $"acceptedSteps={acceptedPdrStepCount}, " +
                $"confidence={pdrConfidence.Confidence}, " +
                $"strideScale={pdrConfidence.StrideScale:F2}, " +
                $"advance={fusedStepAdvanceMeters:F2} m, " +
                $"progress={update.CommittedProgressMeters:F1} m, " +
                $"headingError={headingErrorDegrees:F1} deg, " +
                $"headingSource='{headingEstimate.Reason}', " +
                $"motionCoherence={headingEstimate.MotionCoherence:F2}, " +
                $"publishWindow={published}.");
#endif
        }

        /// <summary>
        /// Compares a short rolling ARCore walking/camera direction estimate to
        /// the current cyan route tangent in the SAME ARCore coordinate frame.
        ///
        /// This remains intentionally AR-relative and does not require another
        /// compass reading at step time.
        /// </summary>
        private bool TryGetPdrDirectionAgreement(
            out double cameraAzimuthDegrees,
            out double routeAzimuthDegrees,
            out double headingErrorDegrees,
            out PdrHeadingSmoother.HeadingEstimate headingEstimate,
            out string unavailableReason)
        {
            cameraAzimuthDegrees =
                0.0;

            routeAzimuthDegrees =
                0.0;

            headingErrorDegrees =
                180.0;

            headingEstimate =
                PdrHeadingSmoother.HeadingEstimate.Unavailable(
                    "PDR heading has not been evaluated.");

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
            double instantaneousCameraAzimuthDegrees =
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

            headingEstimate =
                _pdrHeadingSmoother.Evaluate(
                    Environment.TickCount64,
                    pose.PositionX,
                    pose.PositionZ,
                    instantaneousCameraAzimuthDegrees,
                    routeAzimuthDegrees);

            if (!headingEstimate.IsAvailable)
            {
                unavailableReason =
                    headingEstimate.Reason;

                return false;
            }

            cameraAzimuthDegrees =
                headingEstimate.SmoothedCameraAzimuthDegrees;

            headingErrorDegrees =
                headingEstimate.HeadingErrorDegrees;

            return true;
        }

        private double GetCurrentArRouteVisualWindowMeters()
        {
            return arRouteVisualMode ==
                ArRouteVisualMode.RoadFollowingLong
                ? RoadFollowingRouteVisualWindowMeters
                : ApproachRouteVisualWindowMeters;
        }

        private void ResetArRouteVisualMode(
            string reason)
        {
            /*
             * A newly requested route has not yet proven that the device is
             * physically inside its route corridor. Start conservatively with the
             * short access cue; GPS verification promotes it to the long
             * road-following corridor.
             */
            arRouteVisualMode =
                ArRouteVisualMode.ApproachOrOffCourseShort;

            roadFollowingReentryConfirmationCount =
                0;

            recoveryConnectorVerified =
                false;

#if ANDROID
            Log.Debug(
                RouteLogTag,
                "AR route visual mode RESET to APPROACH/RECOVERY SHORT: " +
                $"window={ApproachRouteVisualWindowMeters:F1} m, " +
                $"reason='{reason}'.");
#endif
        }

        /// <summary>
        /// Returns to the access/recovery visual only after the existing
        /// repeated-GPS off-route policy has VERIFIED an off-course condition.
        /// Candidate 1/3 and 2/3 samples deliberately leave the established
        /// long road-following corridor untouched.
        /// </summary>
        private void EnterVerifiedOffCourseVisualMode(
            string reason)
        {
            roadFollowingReentryConfirmationCount =
                0;

            recoveryConnectorVerified =
                true;

            if (arRouteVisualMode ==
                ArRouteVisualMode.ApproachOrOffCourseShort)
            {
                return;
            }

            arRouteVisualMode =
                ArRouteVisualMode.ApproachOrOffCourseShort;

#if ANDROID
            Log.Warn(
                RouteLogTag,
                "AR ROUTE VISUAL MODE -> VERIFIED OFF-COURSE APPROACH: " +
                "the cyan arrow will point back to the nearest matched " +
                "route corridor until route re-entry is verified; " +
                $"reason='{reason}'.");
#endif
        }

        /// <summary>
        /// Promotes the short access cue to the long road-following corridor
        /// only after consecutive MEDIUM-or-better matches place the user
        /// inside the accuracy-aware route-entry radius. A GPS sample can be
        /// usable for progress while still being too uncertain to justify
        /// drawing the long corridor from the camera.
        ///
        /// Once RoadFollowingLong is established, ordinary GPS jitter does not
        /// demote it. Demotion is handled only by the verified 3/3 off-route
        /// policy above.
        /// </summary>
        private bool UpdateRoadFollowingVisualModeFromAcceptedGps(
            RouteProgressTracker.RouteProgressUpdate update)
        {
            if (arRouteVisualMode ==
                ArRouteVisualMode.RoadFollowingLong)
            {
                roadFollowingReentryConfirmationCount =
                    0;

                return false;
            }

            bool canEnterRoadFollowing =
                RouteCorridorPolicy.CanEnterRoadFollowing(
                    update.IsAccepted,
                    update.IsOffRoute,
                    update.CrossTrackErrorMeters,
                    update.CorridorRadiusMeters,
                    update.MatchConfidence);

            if (!canEnterRoadFollowing)
            {
                roadFollowingReentryConfirmationCount =
                    0;

                return false;
            }

            roadFollowingReentryConfirmationCount++;

#if ANDROID
            Log.Debug(
                RouteLogTag,
                "Route-corridor visual-entry candidate: " +
                $"confirmation={roadFollowingReentryConfirmationCount}/" +
                $"{RoadFollowingEntryRequiredSamples}, " +
                $"crossTrack={update.CrossTrackErrorMeters:F1} m, " +
                $"entryRadius=" +
                $"{RouteCorridorPolicy.GetRoadFollowingEntryRadiusMeters(update.CorridorRadiusMeters):F1} m, " +
                $"corridor={update.CorridorRadiusMeters:F1} m, " +
                $"matchConfidence={update.MatchConfidence}, " +
                $"accuracy=" +
                $"{(update.AccuracyMeters.HasValue ? update.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m.");
#endif

            if (roadFollowingReentryConfirmationCount <
                RoadFollowingEntryRequiredSamples)
            {
                return false;
            }

            arRouteVisualMode =
                ArRouteVisualMode.RoadFollowingLong;

            roadFollowingReentryConfirmationCount =
                0;

            recoveryConnectorVerified =
                false;

#if ANDROID
            Log.Warn(
                RouteLogTag,
                "AR ROUTE VISUAL MODE -> ROAD-FOLLOWING LONG: " +
                $"window={RoadFollowingRouteVisualWindowMeters:F1} m after " +
                $"{RoadFollowingEntryRequiredSamples} confidence-gated " +
                "route-corridor matches.");
#endif

            return true;
        }

        /// <summary>
        /// Keeps a short route-following cue while the device is still proving
        /// route-corridor membership. A direct connector is published only
        /// after repeated off-route confirmation and only when GPS accuracy and
        /// segment identity make that recovery direction trustworthy.
        /// </summary>
        private bool TryPublishApproachToRouteVisual(
            RouteResult route,
            RouteProgressTracker.RouteProgressUpdate update,
            string progressSource = "GPS")
        {
#if ANDROID
            if (!update.GpsCoordinate.IsValid ||
                !update.SnappedCoordinate.IsValid)
            {
                return false;
            }

            ARCameraPoseBridge.SpatialSnapshot spatial =
                ARCameraPoseBridge.CurrentFrame;

            if (!spatial.IsTracking ||
                !spatial.Pose.IsTracking ||
                !spatial.Anchor.IsAvailable)
            {
                return false;
            }

            double connectorDistanceMeters =
                update.GpsCoordinate.DistanceTo(
                    update.SnappedCoordinate);

            bool directConnectorAllowed =
                recoveryConnectorVerified &&
                RouteCorridorPolicy.CanPublishRecoveryConnector(
                    update.AccuracyMeters,
                    update.CrossTrackErrorMeters,
                    update.CorridorRadiusMeters,
                    update.MatchConfidence);

            if (!directConnectorAllowed)
            {
                if (update.IsAccepted)
                {
                    return TryPublishMovingRouteWindow(
                        route,
                        update,
                        $"{progressSource}/CORRIDOR-PENDING");
                }

                Log.Debug(
                    ProgressLogTag,
                    $"{progressSource} RECOVERY CONNECTOR HELD: " +
                    $"verified={recoveryConnectorVerified}, " +
                    $"crossTrack={update.CrossTrackErrorMeters:F1} m, " +
                    $"corridor={update.CorridorRadiusMeters:F1} m, " +
                    $"matchConfidence={update.MatchConfidence}, " +
                    $"accuracy=" +
                    $"{(update.AccuracyMeters.HasValue ? update.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m.");

                return false;
            }

            float arOriginOffsetX =
                spatial.Pose.PositionX -
                spatial.Anchor.PositionX;

            float arOriginOffsetZ =
                spatial.Pose.PositionZ -
                spatial.Anchor.PositionZ;

            bool published =
                _mldArIntegrationService.PublishApproachToRoute(
                    route,
                    update.GpsCoordinate,
                    update.SnappedCoordinate,
                    activeMapToArYawDegrees,
                    arOriginOffsetX,
                    arOriginOffsetZ,
                    clearRouteOnFailure:
                        false);

            if (published)
            {
                Log.Debug(
                    ProgressLogTag,
                    $"{progressSource} APPROACH-TO-ROUTE: " +
                    $"crossTrack={update.CrossTrackErrorMeters:F1} m, " +
                    $"corridor={update.CorridorRadiusMeters:F1} m, " +
                    $"matchConfidence={update.MatchConfidence}, " +
                    $"connector={connectorDistanceMeters:F1} m, " +
                    $"accuracy=" +
                    $"{(update.AccuracyMeters.HasValue ? update.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                    $"arOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m");
            }

            return published;
#else
            return false;
#endif
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
             * Rebase the active local route window horizontally near the
             * current AR camera position so the guidance moves with the user.
             *
             * This publisher is used for the road-following corridor and the
             * short confidence-building cue before route membership is
             * confirmed. Neither GPS nor PDR drives the Evergine camera; they
             * only advance route progress and republish route geometry.
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
                    arOriginOffsetZ,
                arWindowMeters:
                    GetCurrentArRouteVisualWindowMeters(),
                sourceSegmentIndex:
                    update.SegmentIndex);

            if (!published)
            {
                return false;
            }

            _routeProgressTracker.MarkWindowPublished(
                update.CommittedProgressMeters);

            Log.Debug(
                ProgressLogTag,
                $"{progressSource} MOVING WINDOW: " +
                $"segment={update.SegmentIndex}, " +
                $"progress={update.CommittedProgressMeters:F1} m, " +
                $"remaining={update.RemainingMeters:F1} m, " +
                $"crossTrack={update.CrossTrackErrorMeters:F1} m, " +
                $"visualMode={arRouteVisualMode}, " +
                $"visualWindow={GetCurrentArRouteVisualWindowMeters():F1} m, " +
                $"gpsAccuracy=" +
                $"{(update.AccuracyMeters.HasValue ? update.AccuracyMeters.Value.ToString("F1") : "<unknown>")} m, " +
                $"arOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m");

            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Re-publishes the CURRENT short route window after ARCore performs a
        /// sustained, multi-meter world/Anchor correction while the existing
        /// V5 route root is still locked to the previous X/Z frame.
        ///
        /// Unlike replacement-anchor recovery, this intentionally does NOT
        /// preserve the old route-world start. The old world coordinates are
        /// exactly what ARCore has just corrected. Instead, the retained route
        /// progress is projected again and shifted by the CURRENT
        /// camera-to-anchor offset so the cyan guidance is immediately placed
        /// back near the user in the corrected AR frame.
        /// </summary>
        private bool TryRebaseRouteAfterWorldCorrection(
            ARCameraPoseBridge.SpatialSnapshot spatial,
            ARCameraSpatialController.RouteWorldCorrectionRequest request)
        {
#if ANDROID
            RouteResult? route =
                activeRoute;

            if (route is null ||
                route.Points.Count <
                    2)
            {
                Log.Warn(
                    "RescuAR-AnchorRecovery",
                    "Large ARCore world correction was detected, but no active " +
                    "navigation route exists to rebase.");

                return false;
            }

            if (!spatial.IsTracking ||
                !spatial.Pose.IsTracking ||
                !spatial.Anchor.IsAvailable)
            {
                return false;
            }

            ARRouteBridge.RouteSnapshot currentRoute =
                ARRouteBridge.Current;

            if (!currentRoute.IsAvailable ||
                currentRoute.Version !=
                    request.RouteVersion)
            {
                /*
                 * Another GPS/PDR/reroute publication already produced a newer
                 * route version from a newer spatial snapshot. The caller will
                 * acknowledge the now-stale correction request.
                 */
                return false;
            }

            RouteProgressTracker.ProgressSnapshot progress =
                _routeProgressTracker.Current;

            double startDistanceMeters;
            GeoCoordinate referenceCoordinate;
            int sourceSegmentIndex;

            if (progress.HasProgress &&
                progress.SnappedCoordinate.IsValid)
            {
                startDistanceMeters =
                    progress.CommittedProgressMeters;

                referenceCoordinate =
                    progress.SnappedCoordinate;

                sourceSegmentIndex =
                    progress.SegmentIndex;
            }
            else
            {
                startDistanceMeters =
                    route.Points[0]
                        .DistanceFromStartMeters;

                referenceCoordinate =
                    route.Points[0]
                        .Coordinate;

                sourceSegmentIndex =
                    0;
            }

            /*
             * This is the same placement rule used by the normal GPS/PDR
             * moving-window publisher:
             *
             * local route origin = current camera - current ground anchor.
             *
             * Therefore, after the new route version locks its root to the
             * corrected Anchor, the first projected route point is again near
             * the current camera instead of remaining in ARCore's old world
             * coordinates.
             */
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
                        route,
                        startDistanceMeters,
                        referenceCoordinate,
                        activeMapToArYawDegrees,
                        arOriginOffsetX,
                        arOriginOffsetZ,
                        arWindowMeters:
                            GetCurrentArRouteVisualWindowMeters(),
                        clearRouteOnFailure:
                            false,
                        sourceSegmentIndex:
                            sourceSegmentIndex);

                if (published)
                {
                    _routeProgressTracker.MarkWindowPublished(
                        startDistanceMeters);
                }
            }

            if (!published)
            {
                Log.Warn(
                    "RescuAR-AnchorRecovery",
                    "Large-world-correction rebase could not publish a usable " +
                    "replacement route window. The existing route remains visible " +
                    "and the request will retry while spatial tracking is healthy.");

                return false;
            }

            ARRouteBridge.RouteSnapshot rebasedRoute =
                ARRouteBridge.Current;

            UpdateTurnGuidance();

            Log.Warn(
                "RescuAR-AnchorRecovery",
                "ARCORE WORLD CORRECTION ROUTE REBASE COMPLETE: " +
                $"requestGeneration={request.Generation}, " +
                $"oldRouteVersion={request.RouteVersion}, " +
                $"newRouteVersion={rebasedRoute.Version}, " +
                $"detectedSpatialVersion={request.SpatialVersion}, " +
                $"currentSpatialVersion={spatial.Version}, " +
                $"anchorDrift={request.AnchorDriftMeters:F2} m, " +
                $"oldLockedRoot=({request.LockedRootX:F2},{request.LockedRootZ:F2}), " +
                $"correctedAnchor=({spatial.Anchor.PositionX:F2},{spatial.Anchor.PositionZ:F2}), " +
                $"camera=({spatial.Pose.PositionX:F2},{spatial.Pose.PositionZ:F2}), " +
                $"arOffset=({arOriginOffsetX:F2},{arOriginOffsetZ:F2}) m, " +
                $"progress={startDistanceMeters:F1} m, " +
                $"algorithm='{route.Algorithm}'. " +
                "Route progress was preserved; ARCore was not restarted.");

            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// Re-publishes the currently active local route window against the
        /// newly recovered nearby ground anchor.
        ///
        /// The full MLD RouteResult and route-progress state are retained.
        /// Route progress is retained, but the old AR-world route position is
        /// deliberately discarded. The replacement window starts from the
        /// current camera-to-anchor offset so city-scale offsets cannot carry
        /// across local-frame handoffs.
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
                    "Ground anchor recovered, but no active navigation route exists " +
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
            int sourceSegmentIndex;

            if (progress.HasProgress &&
                progress.SnappedCoordinate.IsValid)
            {
                startDistanceMeters =
                    progress.CommittedProgressMeters;

                referenceCoordinate =
                    progress.SnappedCoordinate;

                sourceSegmentIndex =
                    progress.SegmentIndex;
            }
            else
            {
                startDistanceMeters =
                    route.Points[0]
                        .DistanceFromStartMeters;

                referenceCoordinate =
                    route.Points[0]
                        .Coordinate;

                sourceSegmentIndex =
                    0;
            }

            float arOriginOffsetX =
                spatial.Pose.PositionX -
                spatial.Anchor.PositionX;

            float arOriginOffsetZ =
                spatial.Pose.PositionZ -
                spatial.Anchor.PositionZ;

            bool published =
                _mldArIntegrationService.PublishProgressWindow(
                    route,
                    startDistanceMeters,
                    referenceCoordinate,
                    activeMapToArYawDegrees,
                    arOriginOffsetX,
                    arOriginOffsetZ,
                arWindowMeters:
                    GetCurrentArRouteVisualWindowMeters(),
                sourceSegmentIndex:
                    sourceSegmentIndex);

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

            UpdateTurnGuidance();

            Log.Warn(
                "RescuAR-AnchorRecovery",
                "MOVING LOCAL AR FRAME REBASE COMPLETE: " +
                $"progress={startDistanceMeters:F1} m, " +
                $"newAnchor=(" +
                $"{spatial.Anchor.PositionX:F2}," +
                $"{spatial.Anchor.PositionY:F2}," +
                $"{spatial.Anchor.PositionZ:F2}), " +
                $"camera=(" +
                $"{spatial.Pose.PositionX:F2}," +
                $"{spatial.Pose.PositionZ:F2}), " +
                $"localOffset=(" +
                $"{arOriginOffsetX:F2}," +
                $"{arOriginOffsetZ:F2}) m, " +
                $"window={GetCurrentArRouteVisualWindowMeters():F1} m, " +
                $"routeVersion={recoveredRoute.Version}. " +
                "Old AR-world continuity was intentionally discarded; " +
                "geographic route progress was preserved.");

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
            /*
             * ConnectivityChanged is the primary trigger. This lightweight
             * one-second poll is only a safety net for Android network handoffs
             * that do not surface a timely event.
             */
            ScheduleNetworkLossFailoverIfNeeded(
                "diagnostic connectivity poll");

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
             * period after camera tracking returns. Recovery V3 begins a
             * parallel validated-floor search after 750 ms and retains the old
             * Anchor for at most the 2500 ms final recovery grace.
             *
             * CameraPage therefore reacts only when the Android recovery
             * service increments GroundAnchorReplacementGeneration, which
             * happens after the stale Anchor is released or a validated
             * replacement is handed off proactively.
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

                    _pdrHeadingSmoother.Reset();

                    activeGroundAnchorReplacementGeneration =
                        serviceReplacementGeneration;

                    Log.Warn(
                        "RescuAR-AnchorRecovery",
                        "Ground-anchor replacement detected: " +
                        $"replacementGeneration=" +
                        $"{serviceReplacementGeneration}. " +
                        "A stale/distant Anchor was released or proactively " +
                        "replaced. The current " +
                        "geographic route progress will be projected into the " +
                        "replacement local frame once its floor Anchor is TRACKING.");
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
                        "Replacement ground anchor is TRACKING. Rebuilding the " +
                        "current route window in the new local frame: " +
                        $"replacementGeneration=" +
                        $"{activeGroundAnchorReplacementGeneration}.");

                    bool recoveryRouteReady =
                        activeRoute is null ||
                        TryRebaseRouteAfterAnchorRecovery(
                            spatial);

                    if (recoveryRouteReady)
                    {
                        ARCameraSpatialController
                            .SetRouteRecoveryRebasePending(
                                false,
                                activeRoute is null
                                    ? "no active route requires recovery rebasing"
                                    : "current route window republished in the replacement local frame");

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

                        Log.Debug(
                            "RescuAR-AnchorRecovery",
                            "Moving local-frame anchor replacement COMPLETE. " +
                            $"handledReplacementGeneration=" +
                            $"{handledGroundAnchorReplacementGeneration}.");
                    }
                    else
                    {
                        Log.Warn(
                            "RescuAR-AnchorRecovery",
                            "Replacement anchor is TRACKING, but local-window " +
                            "rebasing has not completed. AR route placement " +
                            "remains hidden and rebasing will retry " +
                            "on the next diagnostic tick.");
                    }
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
                 *     proactive/final recovery window; or
                 *
                 *  B) an actual replacement floor search after the service has
                 *     released the stale Anchor.
                 *
                 * The service owns that distinction. CameraPage does NOT clear
                 * or republish ARRouteBridge here.
                 *
                 * The renderer owns the short visual-continuity behavior while
                 * retaining the same route-root X/Z lock.
                 */
                _arCoreService.TryRecoverGroundAnchorIfNeeded();
            }

            /*
             * LARGE IN-SESSION ARCORE WORLD CORRECTION
             * ----------------------------------------
             * This is distinct from stale-Anchor replacement above. ARCore can
             * keep both camera and Anchor TRACKING while materially refining
             * their world coordinates. The Evergine V5 route root intentionally
             * ignores small Anchor jitter, but a sustained multi-meter change
             * makes that old lock stale.
             *
             * ARCameraSpatialController detects the correction on the draw
             * thread. Consume its request here so all navigation state and
             * RouteResult access remains on CameraPage's existing path.
             */
            ARCameraSpatialController.RouteWorldCorrectionRequest
                worldCorrectionRequest =
                    ARCameraSpatialController
                        .CurrentRouteWorldCorrectionRequest;

            if (worldCorrectionRequest.IsPending)
            {
                ARRouteBridge.RouteSnapshot correctionRoute =
                    ARRouteBridge.Current;

                if (!correctionRoute.IsAvailable ||
                    activeRoute is null)
                {
                    ARCameraSpatialController
                        .AcknowledgeRouteWorldCorrection(
                            worldCorrectionRequest.Generation,
                            "no active route remains");
                }
                else if (correctionRoute.Version !=
                    worldCorrectionRequest.RouteVersion)
                {
                    /*
                     * A normal GPS/PDR/reroute/anchor-recovery publication
                     * already created newer geometry using a newer spatial
                     * snapshot, so this old correction request is satisfied.
                     */
                    ARCameraSpatialController
                        .AcknowledgeRouteWorldCorrection(
                            worldCorrectionRequest.Generation,
                            $"stale request; current route version is {correctionRoute.Version}");
                }
                else if (!anchorRecoveryInProgress &&
                         spatial.IsTracking &&
                         spatial.Pose.IsTracking &&
                         spatial.Anchor.IsAvailable)
                {
                    bool correctionRebased =
                        TryRebaseRouteAfterWorldCorrection(
                            spatial,
                            worldCorrectionRequest);

                    if (correctionRebased)
                    {
                        _pdrHeadingSmoother.Reset();

                        ARCameraSpatialController
                            .AcknowledgeRouteWorldCorrection(
                                worldCorrectionRequest.Generation,
                                "current route window republished in corrected ARCore world frame");
                    }
                }
            }

            /*
             * The bridge can change during an actual replacement rebase or an
             * in-session ARCore world-correction rebase. Refresh the diagnostic
             * snapshots so STATUS describes the state after recovery work, not
             * the snapshot captured at timer entry.
             */
            route =
                ARRouteBridge.Current;

            activeSegments =
                ARRouteRenderer.ActiveSegmentCount;

            bool routeShouldBeVisible =
                pageIsVisible &&
                !_arCoreService.IsSessionPaused &&
                currentCameraModuleView ==
                    CameraModuleViewMode.ArCamera &&
                lastArGuidanceConfidence.AllowsRouteGeometry &&
                tracking &&
                spatial.Anchor.IsAvailable &&
                route.IsAvailable &&
                activeSegments >
                    0;

            RouteProgressTracker.ProgressSnapshot progress =
                _routeProgressTracker.Current;

            ARCameraSpatialController.RouteWorldCorrectionRequest
                worldCorrectionStatus =
                    ARCameraSpatialController
                        .CurrentRouteWorldCorrectionRequest;

            ARCameraSpatialController.SpatialContinuitySnapshot
                continuityStatus =
                    ARCameraSpatialController
                        .CurrentSpatialContinuity;

            float cameraToAnchorHorizontalMeters =
                spatial.Anchor.IsAvailable &&
                spatial.Pose.IsTracking
                    ? LocalArNavigationPolicy.GetHorizontalDistanceMeters(
                        spatial.Pose.PositionX -
                            spatial.Anchor.PositionX,
                        spatial.Pose.PositionZ -
                            spatial.Anchor.PositionZ)
                    : float.NaN;

            long diagnosticTimestamp =
                Environment.TickCount64;

            if (lastDynamicUiRefreshTimestamp ==
                    long.MinValue ||
                diagnosticTimestamp -
                    lastDynamicUiRefreshTimestamp >=
                        DynamicUiRefreshIntervalMilliseconds)
            {
                lastDynamicUiRefreshTimestamp =
                    diagnosticTimestamp;

                Dispatcher.Dispatch(
                    RefreshCameraModuleDynamicUi);
            }

            long statusLogTimestamp =
                diagnosticTimestamp;

            if (lastDetailedStatusLogTimestamp !=
                    long.MinValue &&
                statusLogTimestamp -
                    lastDetailedStatusLogTimestamp <
                        DetailedStatusLogIntervalMilliseconds)
            {
                return;
            }

            lastDetailedStatusLogTimestamp =
                statusLogTimestamp;

            Log.Debug(
                RouteLogTag,
                "STATUS: " +
                $"cameraPageActive={pageIsVisible}, " +
                $"cameraModuleView={currentCameraModuleView}, " +
                $"sessionPaused={_arCoreService.IsSessionPaused}, " +
                $"frameLoop={_arCoreService.IsFrameLoopRunning}, " +
                $"tracking={tracking}, " +
                $"anchor={spatial.Anchor.IsAvailable}, " +
                $"cameraToAnchor=" +
                $"{(float.IsFinite(cameraToAnchorHorizontalMeters) ? cameraToAnchorHorizontalMeters.ToString("F2") : "<none>")}m, " +
                $"anchorRetirement=" +
                $"{LocalArNavigationPolicy.GroundAnchorRetirementDistanceMeters:F1}m, " +
                $"anchorRecovery={anchorRecoveryInProgress}, " +
                $"replacementGeneration=" +
                $"{_arCoreService.GroundAnchorReplacementGeneration}, " +
                $"handledReplacementGeneration=" +
                $"{handledGroundAnchorReplacementGeneration}, " +
                $"worldCorrectionPending={worldCorrectionStatus.IsPending}, " +
                $"worldCorrectionGeneration={worldCorrectionStatus.Generation}, " +
                $"worldCorrectionDrift=" +
                $"{(worldCorrectionStatus.IsPending ? worldCorrectionStatus.AnchorDriftMeters.ToString("F2") : "<none>")}m, " +
                $"spatialContinuity={continuityStatus.State}, " +
                $"spatialLossDuration={continuityStatus.DurationMilliseconds}ms, " +
                $"spatialTrustScore={continuityStatus.TrustScore}/100, " +
                $"guidanceConfidence={lastArGuidanceConfidence.State}, " +
                $"guidanceConfidenceScore={lastArGuidanceConfidence.Score}/100, " +
                $"guidanceRouteAllowed={lastArGuidanceConfidence.AllowsRouteGeometry}, " +
                $"consultationRouteOverride=" +
                $"{consultationRouteVisibilityOverrideActive}, " +
                $"destination={NavigationDestinationBridge.Current.IsAvailable}, " +
                $"routeAlgorithm='{activeRoute?.Algorithm ?? "<none>"}', " +
                $"headingAligned={lastHeadingAlignment.HasValue}, " +
                $"headingStable={lastHeadingAlignment?.IsStable ?? false}, " +
                $"mapToArYaw=" +
                $"{(lastHeadingAlignment?.MapToArYawDegrees ?? 0.0):F1}, " +
                $"routePublished={route.IsAvailable}, " +
                $"routeVersion={route.Version}, " +
                $"routePoints={route.Points.Count}, " +
                $"routeVisualKind={route.NavigationState.VisualKind}, " +
                $"routeWindowProgress=" +
                $"{(route.NavigationState.IsAvailable ? route.NavigationState.WindowStartProgressMeters.ToString("F1") : "<none>")}m, " +
                $"routeSourceSegment={route.NavigationState.SourceSegmentIndex}, " +
                $"localRouteWindow={GetCurrentArRouteVisualWindowMeters():F1}m, " +
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
                $"pdrHeadingSource='{lastPdrHeadingSource}', " +
                $"pdrMotionCoherence={lastPdrMotionCoherence:F2}, " +
                $"pdrConfidence={lastPdrConfidence}, " +
                $"pdrStrideScale={lastPdrStrideScale:F2}, " +
                $"gpsConfidence={lastGpsConfidence}, " +
                $"routeMatchConfidence={lastRouteMatchConfidence}, " +
                $"gpsFusionAction={lastGpsFusionAction}, " +
                $"gpsReliability={lastGpsReliabilityWeight:F2}, " +
                $"gpsMinusPdr=" +
                $"{(lastGpsPdrDivergenceMeters.HasValue ? lastGpsPdrDivergenceMeters.Value.ToString("F1") : "<none>")}m, " +
                $"gpsBackConfirmations={lastGpsBackwardConfirmationCount}, " +
                $"gpsIdentityConfirmations={lastGpsRouteIdentityConfirmationCount}, " +
                $"gpsRouteIdentitySuspended={_gpsPdrFusionPolicy.IsRouteIdentitySuspended}, " +
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
                $"turnPanelVisible={turnGuidancePanel.IsVisible}, " +
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
