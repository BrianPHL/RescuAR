#if ANDROID
using Android.Util;
#endif

using RescuAR;
using RescuAR.AR;
using RescuAR.MAUI.Services;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Progress;
using RescuAR.Navigation.Projection;
using RescuAR.Navigation.Routing;
using RescuAR.Navigation.State;

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

        private readonly MyApplication evergineApplication;
        private readonly IArCoreService _arCoreService;
        private readonly MLDARIntegrationService _mldArIntegrationService;
        private readonly ArHeadingAlignmentService _headingAlignmentService;
        private readonly ILocationService _locationService;
        private readonly RouteProgressTracker _routeProgressTracker;

        private readonly IDispatcherTimer diagnosticTimer;

        private CancellationTokenSource? routeRequestCancellation;
        private CancellationTokenSource? routeProgressCancellation;
        private Task? routeProgressTask;

        private bool routeRequestInProgress;
        private bool destinationEventSubscribed;
        private bool pageIsVisible;

        private ArHeadingAlignmentService.HeadingAlignmentResult?
            lastHeadingAlignment;

        private RouteResult? activeRoute;

        private string activeDestinationName =
            string.Empty;

        private GeoCoordinate? activeDestinationCoordinate;

        private double activeMapToArYawDegrees;

        /*
         * TEST SWITCH:
         * Keep true while testing indoors. Set false before normal outdoor /
         * production navigation testing.
         */
        private const bool IndoorRouteTestMode =
            true;

        /*
         * The moving-window milestone has already been proven. While indoor
         * testing continues, freeze route progress so poor GPS and synthetic
         * test advancement cannot move an otherwise healthy AR route.
         *
         * Set IndoorRouteTestMode=false for real outdoor GPS progress.
         */
        private const bool FreezeRouteProgressDuringIndoorTest =
            true;

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
                        ? "INDOOR ROUTE TEST MODE ENABLED. Route progress is FROZEN " +
                          "for stability; GPS/synthetic samples will not move the AR " +
                          "window. Disable IndoorRouteTestMode for outdoor progress testing."
                        : "INDOOR ROUTE TEST MODE ENABLED. GPS thresholds are relaxed " +
                          "and controlled synthetic progress may be used after several " +
                          "stationary samples. Disable this before outdoor/production testing.");
            }
#endif

            SubscribeDestinationChanged();

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

            if (diagnosticTimer.IsRunning)
            {
                diagnosticTimer.Stop();
            }

            StopRouteProgress(
                "Camera tab exited.");

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
                "restarting GPS route-progress loop");

            if (!pageIsVisible ||
                activeRoute is null)
            {
                return;
            }

            if (IndoorRouteTestMode &&
                FreezeRouteProgressDuringIndoorTest)
            {
#if ANDROID
                Log.Debug(
                    ProgressLogTag,
                    "Indoor stability mode: GPS/synthetic route progress loop " +
                    "is intentionally frozen. Existing AR route window retained.");
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
                "GPS route-progress loop started.");
#endif
        }

        private void StopRouteProgress(
            string reason)
        {
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
                        RouteProgressTracker.RouteProgressUpdate update =
                            _routeProgressTracker.Update(
                                reading.Coordinate,
                                reading.AccuracyMeters);

                        if (update.IsAccepted &&
                            update.ShouldPublishWindow)
                        {
                            publishedRealProgress =
                                TryPublishMovingRouteWindow(
                                    route,
                                    update);
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
                        RouteProgressTracker.RouteProgressUpdate synthetic =
                            _routeProgressTracker.AdvanceSynthetic(
                                IndoorSyntheticAdvanceMeters);

                        if (synthetic.IsAccepted)
                        {
                            bool publishedSynthetic =
                                TryPublishMovingRouteWindow(
                                    route,
                                    synthetic);

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

        private bool TryPublishMovingRouteWindow(
            RouteResult route,
            RouteProgressTracker.RouteProgressUpdate update)
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
             * This is the GPS-only moving-window milestone. A later milestone
             * may periodically create/handoff nearby anchors for long routes.
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
                "MOVING WINDOW: " +
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
                $"progressFrozen=" +
                $"{(IndoorRouteTestMode && FreezeRouteProgressDuringIndoorTest)}, " +
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
