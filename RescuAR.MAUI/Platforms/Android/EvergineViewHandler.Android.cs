using System.Threading;
using Android.Util;
using Evergine.Android;
using Evergine.Common.Graphics;
using Evergine.Common.Helpers;
using Evergine.Framework.Services;
using Evergine.Vulkan;
using Microsoft.Maui.Handlers;
using RescuAR;
using RescuAR.AR;
using RescuAR.MAUI.Evergine;
using RescuAR.MAUI.Platforms.Android.Services;
using RescuAR.MAUI.Services;

namespace RescuAR.MAUI.Evergine
{
    public partial class EvergineViewHandler
        : ViewHandler<EvergineView, AndroidSurfaceView>
    {
        private const string VulkanTag =
            "RescuAR-Vulkan";
        private const string ArCoreTag =
            "RescuAR-ARCore";
        private const string EvergineTag =
            "RescuAR-Evergine";

        private AndroidSurface? androidSurface;
        private AndroidWindowsSystem? windowsSystem;
        private SwapChain? swapChain;
        private FrameBoundaryDisplay? frameBoundaryDisplay;
        private VKGraphicsContext? graphicsContext;
        private MyApplication? hostedApplication;
        private bool graphicsInitialized;
        private bool graphicsInitializationFailed;
        private bool applicationOwnsGraphicsContext;
        private Task? graphicsRetirement;
        private readonly object graphicsDrawSync = new();
        private long graphicsGeneration;
        private int graphicsOwnerThreadId;
        private int surfaceRecreationPending;
        private int surfaceResizePending;
        private int forceSurfaceRefresh;
        private volatile bool surfaceResizeFailed;
        private volatile bool handlerConnected;
        private volatile bool renderingEnabled =
            true;

        public bool IsRenderingEnabled =>
            renderingEnabled;

        public bool IsSurfaceResizeFailed =>
            surfaceResizeFailed;

        /*
         * Retain the concrete ARCore service once the graphics context is
         * configured. Surface-size callbacks can then update ARCore's display
         * geometry without resolving the service every time.
         */
        private ArCoreService? arCoreService;

        /*
         * Avoid repeatedly sending identical geometry values to
         * ArCoreService. ArCoreService performs its own duplicate check as
         * well, but keeping this state here avoids unnecessary cross-layer
         * calls from frequent Android surface callbacks.
         */
        private int lastArCoreRotation =
            int.MinValue;

        private int lastArCoreWidth =
            -1;

        private int lastArCoreHeight =
            -1;

        public EvergineViewHandler(
            IPropertyMapper mapper,
            CommandMapper? commandMapper = null)
            : base(
                mapper,
                commandMapper)
        {
        }

        public static void MapApplication(
            EvergineViewHandler handler,
            EvergineView evergineView)
        {
            handler.UpdateApplication(
                evergineView,
                evergineView.DisplayName);
        }

        internal void UpdateApplication(
            EvergineView view,
            string displayName)
        {
            _ = displayName;

            var application =
                view.Application;

            if (application is null)
            {
                return;
            }

            if (application is not MyApplication rescuarApplication)
            {
                throw new InvalidOperationException(
                    $"{nameof(EvergineView.Application)} must be a " +
                    $"{typeof(MyApplication).FullName} instance on Android.");
            }

            if (hostedApplication is not null) return;
            hostedApplication = rescuarApplication = view.ClaimApplicationForAndroidHandler(rescuarApplication);

            AndroidWindowsSystem currentWindowsSystem =
                windowsSystem
                ?? throw new InvalidOperationException(
                    "AndroidWindowsSystem has not been created yet.");

            AndroidSurface currentSurface =
                androidSurface
                ?? throw new InvalidOperationException(
                    "AndroidSurface has not been created yet.");

            /*
             * Register the Evergine Android window system.
             */
            rescuarApplication.Container.RegisterInstance(
                currentWindowsSystem);

            System.Diagnostics.Stopwatch clockTimer =
                System.Diagnostics.Stopwatch.StartNew();

            currentWindowsSystem.Run(
                () =>
                {
                    lock (graphicsDrawSync)
                    {
                        if (!handlerConnected || !ReferenceEquals(androidSurface, currentSurface)) return;
                        graphicsOwnerThreadId = Environment.CurrentManagedThreadId;
                        TryInitializeGraphics(rescuarApplication, currentSurface);
                    }
                },
                () => DrawOnSurfaceOwner(rescuarApplication, currentSurface, clockTimer));
        }

        private bool TryInitializeGraphics(MyApplication application, AndroidSurface surface)
        {
            if (graphicsInitialized) return true;
            if (graphicsInitializationFailed || Volatile.Read(ref surfaceRecreationPending) != 0 ||
                surface.Width <= 0 || surface.Height <= 0) return false;

            try
            {
                var service = (ArCoreService)MauiProgram.Services.GetRequiredService<IArCoreService>();
                // A replacement surface can arrive while the old device is
                // still draining. Try again from the draw owner without waiting.
                if (!service.CanInitializeGraphicsContext) return false;
                application.Container.RegisterInstance(new global::Evergine.OpenAL.ALAudioDevice());
                ConfigureGraphicsContext(application, surface, service);
                application.Initialize();
                graphicsInitialized = true;
                service.NotifyGraphicsSurfaceReady();
                return true;
            }
            catch (Exception exception)
            {
                graphicsInitializationFailed = true;
                surfaceResizeFailed = true;
                renderingEnabled = false;
                if (graphicsGeneration > 0) arCoreService?.NotifyGraphicsSurfaceLost();
                Log.Error(VulkanTag, $"AR graphics initialization failed: {exception}");
                return false;
            }
        }

        private void DrawOnSurfaceOwner(
            MyApplication application,
            AndroidSurface surface,
            System.Diagnostics.Stopwatch clockTimer)
        {
            bool idle = false;
            lock (graphicsDrawSync)
            {
                // Evergine creates a new worker per SurfaceCreated. Reject
                // callbacks from the old loop even if it overlaps recreation.
                if (!handlerConnected || !ReferenceEquals(androidSurface, surface) ||
                    Environment.CurrentManagedThreadId != graphicsOwnerThreadId ||
                    !TryInitializeGraphics(application, surface))
                {
                    idle = true;
                }
                else
                {
                    ProcessPendingSurfaceResize();
                    if (!renderingEnabled ||
                        Volatile.Read(ref surfaceRecreationPending) != 0)
                    {
                        idle = true;
                    }
                    else
                    {
                        TimeSpan gameTime = TimeSpan.FromMilliseconds(
                            Math.Min(clockTimer.Elapsed.TotalMilliseconds, 100.0));
                        clockTimer.Restart();
#if RESCUAR_DIAGNOSTICS
                        long profileRun = ARPerformanceProfile.CurrentRun;
                        long profileStart = System.Diagnostics.Stopwatch.GetTimestamp();
#endif
                        try
                        {
                            application.UpdateFrame(gameTime);
                            application.DrawFrame(gameTime);
                            idle = application.WasArDrawSkipped;
                        }
                        catch (Exception exception)
                        {
                            renderingEnabled = false;
                            surfaceResizeFailed = true;
                            Interlocked.Exchange(ref surfaceRecreationPending, 1);
                            Interlocked.Exchange(ref forceSurfaceRefresh, 1);
                            arCoreService?.NotifyGraphicsSurfaceLost();
                            ARCameraSpatialController.SetRouteRenderingEnabled(
                                false, "AR draw failed");
                            Log.Error(VulkanTag,
                                $"AR draw failed; waiting for surface recreation: {exception}");
                        }
#if RESCUAR_DIAGNOSTICS
                        finally
                        {
                            ARPerformanceProfile.RecordElapsed(profileRun, ARPerformanceProfile.Metric.FrameWork, profileStart);
                        }
#endif
                    }
                }
            }
            // A paused engine loop otherwise spins continuously and prevents
            // the ARCore/lifecycle workers from getting timely CPU time.
            if (idle) Thread.Sleep(16);
        }
        protected override AndroidSurfaceView CreatePlatformView()
        {
            windowsSystem =
                new AndroidWindowsSystem(
                    Context);

            androidSurface =
                windowsSystem.CreateSurface(
                    runInUIThread: false)
                as AndroidSurface
                ?? throw new InvalidOperationException(
                    "AndroidWindowsSystem.CreateSurface(false) did not return " +
                    "an AndroidSurface.");

            Log.Debug(
                EvergineTag,
                "Evergine Android graphics backend configured for " +
                $"independent thread. RunInUIThread=" +
                $"{androidSurface.NativeSurface.RunInUIThread}");

            return androidSurface.NativeSurface;
        }

        protected override void ConnectHandler(
            AndroidSurfaceView platformView)
        {
            base.ConnectHandler(
                platformView);

            graphicsRetirement = null;
            surfaceResizeFailed = false;
            renderingEnabled = true;
            handlerConnected =
                true;

            AndroidSurface? surface =
                androidSurface;

            if (surface is null)
            {
                return;
            }

            surface.OnSurfaceInfoChanged +=
                AndroidSurface_OnSurfaceInfoChanged;

            surface.Closing +=
                AndroidSurface_OnClosing;

            surface.OnScreenSizeChanged +=
                AndroidSurface_OnScreenSizeChanged;

            /*
             * The surface may already have valid dimensions by the time
             * ConnectHandler is reached.
             */
            UpdateArCoreDisplayGeometry(
                surface);
        }

        protected override void DisconnectHandler(AndroidSurfaceView platformView)
        {
            try { _ = RetireGraphicsHost(); }
            finally { base.DisconnectHandler(platformView); }
        }

        private Task RetireGraphicsHost()
        {
            renderingEnabled = false;
            handlerConnected = false;
            // Drain any host draw callback before detaching Android's surface.
            // Vulkan queue completion happens on a worker outside this lock.
            lock (graphicsDrawSync)
            {
                if (graphicsRetirement is not null) return graphicsRetirement;

                AndroidSurface? surface = androidSurface;
                if (surface is not null)
                {
                    surface.OnScreenSizeChanged -= AndroidSurface_OnScreenSizeChanged;
                    surface.OnSurfaceInfoChanged -= AndroidSurface_OnSurfaceInfoChanged;
                    surface.Closing -= AndroidSurface_OnClosing;
                }

                var retiringApplication = hostedApplication;
                var retiringChain = swapChain;
                var retiringDisplay = frameBoundaryDisplay;
                var retiringContext = graphicsContext;
                bool contextOwnedByApplication = applicationOwnsGraphicsContext;
                long retiringGeneration = graphicsGeneration;
                var retiringService = arCoreService;
                arCoreService = null;
                hostedApplication = null;
                swapChain = null;
                graphicsContext = null;
                frameBoundaryDisplay = null;
                graphicsInitialized = false;
                graphicsInitializationFailed = false;
                applicationOwnsGraphicsContext = false;
                graphicsGeneration = 0;
                graphicsOwnerThreadId = 0;
                Interlocked.Exchange(ref forceSurfaceRefresh, 0);
                Interlocked.Exchange(ref surfaceRecreationPending, 0);
                Interlocked.Exchange(ref surfaceResizePending, 0);
                lastArCoreRotation = int.MinValue;
                lastArCoreWidth = -1;
                lastArCoreHeight = -1;

                void ReleaseHost()
                {
                    // Display owns the swap chain. The application owns the
                    // registered context, which must outlive the swap chain.
                    if (retiringDisplay is not null) retiringDisplay.Dispose();
                    else retiringChain?.Dispose();
                    retiringApplication?.Dispose();
                    if (!contextOwnedByApplication) retiringContext?.Dispose();
                }

                graphicsRetirement = retiringService is not null && retiringContext is not null && retiringGeneration > 0
                    ? retiringService.RetireGraphicsContextAtSurfaceBoundaryAsync(retiringContext, retiringGeneration, ReleaseHost)
                    : Task.Run(ReleaseHost);
                _ = ObserveGraphicsRetirementAsync(graphicsRetirement, retiringGeneration);
                return graphicsRetirement;
            }
        }

        private void AndroidSurface_OnClosing(object? sender, EventArgs e)
        {
            renderingEnabled = false;
            lock (graphicsDrawSync)
            {
                if (!handlerConnected ||
                    Interlocked.Exchange(ref surfaceRecreationPending, 1) != 0)
                    return;

                // Surface destruction is temporary. Retain the Session,
                // importer, and device, and drain the last draw before return.
                Interlocked.Exchange(ref forceSurfaceRefresh, 1);
                Interlocked.Exchange(ref surfaceResizePending, 0);
                ARCameraSpatialController.SetRouteRenderingEnabled(
                    false, "Android AR surface lost");
                arCoreService?.NotifyGraphicsSurfaceLost();
                Log.Info(ArCoreTag, "ARCORE_SURFACE_PAUSED_RETAINING_CONTEXT");
            }
        }

        private static async Task ObserveGraphicsRetirementAsync(Task retirement, long retiredGeneration)
        {
            try
            {
                try { await retirement.WaitAsync(TimeSpan.FromSeconds(6)).ConfigureAwait(false); }
                catch (TimeoutException) when (!retirement.IsCompleted)
                {
                    Log.Warn(ArCoreTag, $"ARCORE_VULKAN_TEARDOWN_PENDING graphicsGeneration={retiredGeneration}.");
                    await retirement.ConfigureAwait(false);
                }
                if (retiredGeneration > 0)
                    Log.Info(ArCoreTag, $"ARCORE_SURFACE_DETACH_AFTER_DRAW_ACK graphicsGeneration={retiredGeneration}.");
            }
            catch (Exception exception)
            {
                Log.Error(ArCoreTag, $"AR graphics retirement failed for generation {retiredGeneration}: {exception}");
            }
        }

        private void AndroidSurface_OnSurfaceInfoChanged(
            object? sender, SurfaceInfo surfaceInfo)
        {
            // The pinned Evergine backend raises this on the newly created
            // graphics worker before that worker starts its RenderLoop.
            lock (graphicsDrawSync)
            {
                if (!handlerConnected || androidSurface is not { } surface)
                    return;
                try
                {
                    arCoreService?.RebindGraphicsDrawThread();
                    graphicsOwnerThreadId = Environment.CurrentManagedThreadId;
                    Interlocked.Exchange(ref forceSurfaceRefresh, 1);
                    Interlocked.Exchange(ref surfaceResizePending, 1);
                    ScheduleResizeForRecreatedSurface();
                    UpdateArCoreDisplayGeometry(surface);
                }
                catch (Exception exception)
                {
                    renderingEnabled = false;
                    surfaceResizeFailed = true;
                    arCoreService?.NotifyGraphicsSurfaceLost();
                    Log.Error(ArCoreTag, $"Graphics worker transfer failed: {exception}");
                }
            }
        }

        private void ScheduleResizeForRecreatedSurface()
        {
            // Only the new native surface notification authorizes recreation.
            // A layout callback can arrive while the old native handle is gone.
            // The draw callback separately waits for nonzero dimensions.
            if (Volatile.Read(ref surfaceRecreationPending) == 0)
            {
                return;
            }

            Interlocked.Exchange(ref surfaceRecreationPending, 0);
            Interlocked.Exchange(ref surfaceResizePending, 1);
            Log.Info(ArCoreTag, "ARCORE_SURFACE_RECREATED_RESIZE_PENDING");
        }

        private void AndroidSurface_OnScreenSizeChanged(
            object? sender,
            SizeEventArgs e)
        {
            if (!handlerConnected || androidSurface is not { } surface)
            {
                return;
            }

            Interlocked.Exchange(
                ref surfaceResizePending,
                1);

            /*
             * This is the key connection between Evergine's real render
             * surface and ARCore display geometry.
             */
            UpdateArCoreDisplayGeometry(
                surface);
        }

        private void ProcessPendingSurfaceResize()
        {
            if (Volatile.Read(ref surfaceResizePending) == 0 ||
                !handlerConnected ||
                Volatile.Read(ref surfaceRecreationPending) != 0 ||
                Environment.CurrentManagedThreadId != graphicsOwnerThreadId ||
                androidSurface is not { } surface ||
                frameBoundaryDisplay is not { } currentDisplay ||
                surface.Width <= 0 ||
                surface.Height <= 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref surfaceResizePending, 0) == 0)
            {
                return;
            }

            try
            {
                // Let the engine replace its swapchain AND publish the matching
                // Display framebuffer once, before UpdateFrame prepares cameras.
                // Native callbacks only update the next frame's snapshot.
                bool changed = currentDisplay.ApplySurfaceChangesAtFrameStart(
                    Volatile.Read(ref forceSurfaceRefresh) != 0);
                if (!changed)
                {
                    Log.Info(VulkanTag, "ARCORE_DUPLICATE_SURFACE_RESIZE_SKIPPED");
                    return;
                }
                Interlocked.Exchange(ref forceSurfaceRefresh, 0);
                Log.Info(VulkanTag,
                    $"ARCORE_SURFACE_RESIZED width={currentDisplay.Width}, " +
                    $"height={currentDisplay.Height}; owner=FRAME_START_DISPLAY.");
                surfaceResizeFailed = false;
                renderingEnabled = true;
                arCoreService?.NotifyGraphicsSurfaceReady();
            }
            catch (Exception exception)
            {
                // A failed Vulkan format negotiation must not crash the UI
                // thread or keep submitting frames to an invalid swap chain.
                SuspendRendering();
                if (!surfaceResizeFailed)
                {
                    arCoreService?.NotifyGraphicsSurfaceLost();
                }
                surfaceResizeFailed = true;
                Interlocked.Exchange(ref surfaceRecreationPending, 1);
                Interlocked.Exchange(ref forceSurfaceRefresh, 1);
                ARCameraSpatialController.SetRouteRenderingEnabled(
                    false,
                    "Android surface resize failed");
                Log.Error(
                    VulkanTag,
                    "AR surface resize failed; waiting for the next valid " +
                    $"surface geometry: {exception}");
            }
        }

        private void ConfigureGraphicsContext(
            MyApplication application,
            Surface surface,
            ArCoreService service)
        {
            graphicsOwnerThreadId =
                Environment.CurrentManagedThreadId;

            string[] deviceExtensions =
            [
                "VK_ANDROID_external_memory_android_hardware_buffer",
                "VK_KHR_external_memory",
                "VK_KHR_sampler_ycbcr_conversion",
            ];

            string[] instanceExtensions =
                [];

            var createdGraphicsContext =
                new VKGraphicsContext(
                    deviceExtensions,
                    instanceExtensions);

            arCoreService = service;

            createdGraphicsContext.CreateDevice();
            graphicsContext = createdGraphicsContext;

            Log.Debug(
                VulkanTag,
                "===== Requested Device Extensions =====");

            foreach (string extension in deviceExtensions)
            {
                Log.Debug(
                    VulkanTag,
                    extension);
            }

            Log.Debug(
                VulkanTag,
                $"Factory = " +
                $"{createdGraphicsContext.Factory.GetType().FullName}");

            Log.Debug(
                ArCoreTag,
                "===== AFTER CreateDevice() =====");

            Log.Debug(
                ArCoreTag,
                $"VkInstance = " +
                $"0x{createdGraphicsContext.VkInstance.Handle:X}");

            Log.Debug(
                ArCoreTag,
                $"VkPhysicalDevice = " +
                $"0x{createdGraphicsContext.VkPhysicalDevice.Handle:X}");

            Log.Debug(
                ArCoreTag,
                $"VkDevice = " +
                $"0x{createdGraphicsContext.VkDevice.Handle:X}");

            Log.Debug(
                ArCoreTag,
                createdGraphicsContext.GetType().FullName
                ?? createdGraphicsContext.GetType().Name);

            long createdGraphicsGeneration =
                arCoreService.SetGraphicsContext(
                    createdGraphicsContext,
                    RetireGraphicsHost);

            graphicsGeneration =
                createdGraphicsGeneration;

            application.BindArGraphicsGeneration(
                createdGraphicsGeneration);

            /*
             * At this point Evergine has supplied its actual Android surface.
             * Use that surface rather than the Activity DecorView for ARCore's
             * display geometry.
             */
            if (androidSurface is not null)
            {
                UpdateArCoreDisplayGeometry(
                    androidSurface);
            }

            SwapChainDescription swapChainDescription =
                new()
                {
                    SurfaceInfo =
                        surface.SurfaceInfo,

                    Width =
                        surface.Width,

                    Height =
                        surface.Height,

                    ColorTargetFormat =
                        PixelFormat.R8G8B8A8_UNorm,

                    ColorTargetFlags =
                        TextureFlags.RenderTarget |
                        TextureFlags.ShaderResource,

                    DepthStencilTargetFormat =
                        PixelFormat.D24_UNorm_S8_UInt,

                    DepthStencilTargetFlags =
                        TextureFlags.DepthStencil,

                    SampleCount =
                        TextureSampleCount.None,

                    IsWindowed =
                        true,

                    RefreshRate =
                        GetCurrentDisplayRefreshRate(),
                };

            swapChain =
                createdGraphicsContext.CreateSwapChain(
                    swapChainDescription);

            swapChain.VerticalSync =
                true;

            var graphicsPresenter =
                application.Container
                    .Resolve<GraphicsPresenter>();

            frameBoundaryDisplay =
                new FrameBoundaryDisplay(
                    surface,
                    swapChain,
                    () => EvergineArCameraFrameImporter.WaitForGraphicsDeviceIdle(
                        createdGraphicsContext));
            var firstDisplay = frameBoundaryDisplay;

            graphicsPresenter.AddDisplay(
                "DefaultDisplay",
                firstDisplay);

            application.Container.RegisterInstance(
                createdGraphicsContext);
            applicationOwnsGraphicsContext = true;
        }

        /// <summary>
        /// Sends the actual Evergine Android rendering viewport to ARCore.
        ///
        /// Width and height come directly from AndroidSurface, which is also
        /// the surface used by the Evergine swap chain.
        ///
        /// Rotation comes from the Android Display associated with the native
        /// Evergine SurfaceView.
        /// </summary>
        private void UpdateArCoreDisplayGeometry(
            AndroidSurface surface)
        {
            ArCoreService? currentArCoreService =
                arCoreService;

            if (currentArCoreService is null)
            {
                return;
            }

            int width =
                checked((int)surface.Width);

            int height =
                checked((int)surface.Height);

            /*
             * During creation/resizing Android can temporarily report a
             * zero-size surface. Never send invalid geometry to ARCore.
             */
            if (width <= 0 ||
                height <= 0)
            {
                return;
            }

            int rotation =
                GetCurrentDisplayRotation();

            if (rotation ==
                    lastArCoreRotation &&
                width ==
                    lastArCoreWidth &&
                height ==
                    lastArCoreHeight)
            {
                return;
            }

            currentArCoreService.SetDisplayGeometry(
                rotation,
                width,
                height);

            lastArCoreRotation =
                rotation;

            lastArCoreWidth =
                width;

            lastArCoreHeight =
                height;

            Log.Debug(
                ArCoreTag,
                "Evergine viewport geometry -> ARCore: " +
                $"rotation={rotation}, " +
                $"width={width}, " +
                $"height={height}");
        }

        /// <summary>
        /// Returns the Android surface rotation expected by
        /// Session.SetDisplayGeometry():
        ///
        /// 0 = ROTATION_0
        /// 1 = ROTATION_90
        /// 2 = ROTATION_180
        /// 3 = ROTATION_270
        /// </summary>
        private int GetCurrentDisplayRotation()
        {
            AndroidSurfaceView? nativeView =
                PlatformView;

            if (nativeView?.Display is not null)
            {
                return (int)nativeView.Display.Rotation;
            }

            /*
             * PlatformView may not yet be assigned during very early graphics
             * initialization. The AndroidSurface owns the same native
             * SurfaceView, so use it as the fallback.
             */
            AndroidSurfaceView? surfaceView =
                androidSurface?.NativeSurface;

            if (surfaceView?.Display is not null)
            {
                return (int)surfaceView.Display.Rotation;
            }

            /*
             * Rotation 0 is only a startup fallback. Once Android attaches
             * the view to a display, one of the paths above will provide the
             * real rotation and trigger another geometry update.
             */
            return 0;
        }
        public void SuspendRendering()
        {
            if (!renderingEnabled)
            {
                return;
            }

            renderingEnabled =
                false;

            Log.Debug(
                EvergineTag,
                "Evergine rendering suspended.");
        }

        public void PauseRendering()
        {
            AndroidSurfaceView? nativeView =
                PlatformView
                ?? androidSurface?.NativeSurface;

            if (nativeView is null)
            {
                return;
            }

            nativeView.Pause();

            Log.Debug(
                EvergineTag,
                "Evergine Android surface paused.");
        }

        public void ResumeRendering()
        {
            AndroidSurfaceView? nativeView =
                PlatformView
                ?? androidSurface?.NativeSurface;

            if (nativeView is null)
            {
                return;
            }

            nativeView.Resume();

            Log.Debug(
                EvergineTag,
                "Evergine Android surface resumed.");
        }

        private uint GetCurrentDisplayRefreshRate()
        {
            AndroidSurfaceView? nativeView =
                PlatformView
                ?? androidSurface?.NativeSurface;

            Android.Views.Display? display =
                nativeView?.Display;

            if (display is null)
            {
                Log.Warn(
                    VulkanTag,
                    "Android display unavailable. Falling back to 60 Hz.");

                return 60;
            }

            float currentRefreshRate =
                display.GetMode()?.RefreshRate
                ?? display.RefreshRate;

            uint selectedRefreshRate =
                (uint)Math.Round(
                    currentRefreshRate);

            if (selectedRefreshRate == 0)
            {
                selectedRefreshRate =
                    60;
            }

            return selectedRefreshRate;
        }
    }
}
