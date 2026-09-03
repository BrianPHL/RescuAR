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

        private double activeMapToArYawDegrees;

        private static readonly TimeSpan RouteProgressPollInterval =
            TimeSpan.FromSeconds(
                2);

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

            _mldArIntegrationService =
                new MLDARIntegrationService();

            _headingAlignmentService =
                new ArHeadingAlignmentService();

            _locationService =
                new MauiLocationService();

            _routeProgressTracker =
                new RouteProgressTracker();

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
             * Do not auto-create a brand-new ARCore Session here. The first
             * initialization remains user-controlled by the existing button.
             *
             * If a retained Session already exists, however, returning to the
             * Camera tab must resume it automatically.
             */
            if (_arCoreService.IsInitialized)
            {
                bool resumed =
                    _arCoreService.ResumeCameraSession();

#if ANDROID
                Log.Debug(
                    ArCoreLogTag,
                    $"Camera tab ARCore resume result = {resumed}; " +
                    $"paused={_arCoreService.IsSessionPaused}, " +
                    $"frameLoop={_arCoreService.IsFrameLoopRunning}");
#endif

                if (resumed)
                {
                    StartRouteRequestIfPossible();
                }
            }
        }

        protected override void OnDisappearing()
        {
            pageIsVisible =
                false;

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

        private async void InitializeArCoreClicked(
            object sender,
            EventArgs e)
        {
#if ANDROID
            Log.Debug(
                ArCoreLogTag,
                "Initialize ARCore button clicked.");

            PermissionStatus permissionStatus =
                await Permissions.RequestAsync<
                    Permissions.Camera>();

            Log.Debug(
                ArCoreLogTag,
                $"Camera permission status: {permissionStatus}");

            if (permissionStatus !=
                PermissionStatus.Granted)
            {
                Log.Error(
                    ArCoreLogTag,
                    "Camera permission was not granted.");

                await DisplayAlert(
                    "ARCore",
                    "Camera permission was not granted.",
                    "OK");

                return;
            }

            Log.Debug(
                ArCoreLogTag,
                "Camera permission granted.");

            bool initialized;

            bool creatingNewSession =
                !_arCoreService.IsInitialized;

            if (!creatingNewSession)
            {
                /*
                 * A retained ARCore Session keeps the same AR world frame, so
                 * its map-to-AR heading calibration must also be retained.
                 */
                initialized =
                    _arCoreService.ResumeCameraSession();
            }
            else
            {
                /*
                 * A newly-created ARCore Session defines a new arbitrary world
                 * yaw. Clear the old calibration exactly here -- not when the
                 * Camera tab pauses and not when the destination changes.
                 */
                _headingAlignmentService.ResetSessionCalibration(
                    "creating a new ARCore Session");

                lastHeadingAlignment =
                    null;

                initialized =
                    _arCoreService.Initialize();
            }

            Log.Debug(
                ArCoreLogTag,
                $"ARCore start/resume returned: {initialized}; " +
                $"paused={_arCoreService.IsSessionPaused}, " +
                $"frameLoop={_arCoreService.IsFrameLoopRunning}");

            if (!initialized)
            {
                await DisplayAlert(
                    "ARCore",
                    "ARCore Session was not initialized/resumed. Check Logcat.",
                    "OK");

                return;
            }

            Log.Debug(
                MldLogTag,
                "ARCore active. Checking navigation destination for MLD routing.");

            StartRouteRequestIfPossible();

            await DisplayAlert(
                "ARCore",
                "ARCore Session initialized successfully.",
                "OK");
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

                activeMapToArYawDegrees =
                    mapToArYawDegrees;

                _routeProgressTracker.SetRoute(
                    route);

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

        private async void UpdateArCoreClicked(
            object sender,
            EventArgs e)
        {
            if (!_arCoreService.IsInitialized)
            {
                await DisplayAlert(
                    "ARCore",
                    "Initialize ARCore first.",
                    "OK");

                return;
            }

#if ANDROID
            if (_arCoreService.IsSessionPaused)
            {
                Log.Warn(
                    ArCoreLogTag,
                    "Manual Update ignored because ARCore Session is paused.");

                return;
            }

            try
            {
                var frame =
                    _arCoreService.Update();

                if (frame is null)
                {
                    Log.Debug(
                        ArCoreLogTag,
                        "Manual ARCore Update(): frame is null. " +
                        "This is expected while the automatic frame loop is running.");

                    return;
                }

                var camera =
                    frame.Camera;

                Log.Debug(
                    ArCoreLogTag,
                    "Manual ARCore frame: " +
                    $"timestamp={frame.Timestamp}, " +
                    $"tracking={camera.TrackingState}, " +
                    $"failure={camera.TrackingFailureReason}, " +
                    $"textureName={frame.CameraTextureName}, " +
                    $"hardwareBufferNull={frame.HardwareBuffer is null}");
            }
            catch (Exception ex)
            {
                Log.Error(
                    ArCoreLogTag,
                    $"Manual ARCore frame update exception: {ex}");
            }
#endif
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

                    if (reading is not null)
                    {
                        RouteProgressTracker.RouteProgressUpdate update =
                            _routeProgressTracker.Update(
                                reading.Coordinate,
                                reading.AccuracyMeters);

                        if (update.IsAccepted &&
                            update.ShouldPublishWindow)
                        {
                            TryPublishMovingRouteWindow(
                                route,
                                update);
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
