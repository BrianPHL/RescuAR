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
        private VKGraphicsContext? graphicsContext;
        private readonly object graphicsDrawSync = new();
        private long graphicsGeneration;
        private int graphicsOwnerThreadId;
        private int surfaceDetachAcknowledgementLogged;
        private int surfaceRecreationPending;
        private int surfaceResizePending;
        private int forceSurfaceRefresh;
        private uint lastSwapChainWidth;
        private uint lastSwapChainHeight;
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
            application.Container.RegisterInstance(
                currentWindowsSystem);

            /*
             * Create and register the audio device.
             */
            var audioDevice =
                new global::Evergine.OpenAL.ALAudioDevice();

            application.Container.RegisterInstance(
                audioDevice);

            System.Diagnostics.Stopwatch clockTimer =
                System.Diagnostics.Stopwatch.StartNew();

            currentWindowsSystem.Run(
                () =>
                {
                    lock (graphicsDrawSync)
                    {
                        if (!handlerConnected) return;
                        ConfigureGraphicsContext(
                            rescuarApplication,
                            currentSurface);

                        application.Initialize();
                        if (Volatile.Read(ref surfaceRecreationPending) == 0 &&
                            currentSurface.Width > 0 && currentSurface.Height > 0)
                        {
                            arCoreService?.NotifyGraphicsSurfaceReady();
                        }
                    }
                },
                () => DrawOnSurfaceOwner(application, clockTimer));
        }

        private void DrawOnSurfaceOwner(
            global::Evergine.Framework.Application application,
            System.Diagnostics.Stopwatch clockTimer)
        {
            bool idle = false;
            lock (graphicsDrawSync)
            {
                // Evergine creates a new worker per SurfaceCreated. Reject
                // callbacks from the old loop even if it overlaps recreation.
                if (!handlerConnected ||
                    Environment.CurrentManagedThreadId != graphicsOwnerThreadId)
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
                        try
                        {
                            application.UpdateFrame(gameTime);
                            application.DrawFrame(gameTime);
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
            renderingEnabled = false;
            lock (graphicsDrawSync)
            {
                handlerConnected = false;
                AndroidSurface? surface = androidSurface;
                if (surface is not null)
                {
                    surface.OnScreenSizeChanged -= AndroidSurface_OnScreenSizeChanged;
                    surface.OnSurfaceInfoChanged -= AndroidSurface_OnSurfaceInfoChanged;
                    surface.Closing -= AndroidSurface_OnClosing;
                }

                // Closing a handler is permanent. The native render loop may
                // already have stopped, so cleanup cannot await its next draw.
                try
                {
                    if (arCoreService is not null && graphicsContext is not null &&
                        graphicsGeneration > 0)
                    {
                        arCoreService.QuiesceGraphicsContextAtSurfaceBoundary(
                            graphicsContext, graphicsGeneration, "MAUI handler disconnect");
                        LogSurfaceDetachAcknowledgement();
                    }
                }
                catch (Exception exception)
                {
                    Log.Error(ArCoreTag,
                        $"Refusing surface detachment before graphics cleanup: {exception}");
                    return;
                }

                arCoreService = null;
                graphicsContext = null;
                graphicsGeneration = 0;
                graphicsOwnerThreadId = 0;
                lastSwapChainWidth = 0;
                lastSwapChainHeight = 0;
                Interlocked.Exchange(ref forceSurfaceRefresh, 0);
                Interlocked.Exchange(ref surfaceRecreationPending, 0);
                Interlocked.Exchange(ref surfaceResizePending, 0);
                lastArCoreRotation = int.MinValue;
                lastArCoreWidth = -1;
                lastArCoreHeight = -1;
                base.DisconnectHandler(platformView);
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

        private void LogSurfaceDetachAcknowledgement()
        {
            if (Interlocked.Exchange(
                    ref surfaceDetachAcknowledgementLogged,
                    1) != 0)
            {
                return;
            }

            Log.Info(
                ArCoreTag,
                "ARCORE_SURFACE_DETACH_AFTER_DRAW_ACK " +
                $"graphicsGeneration={graphicsGeneration}.");
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
            if (androidSurface is not { } surface)
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
                swapChain is not { } currentSwapChain ||
                surface.Width <= 0 ||
                surface.Height <= 0)
            {
                return;
            }

            if (Interlocked.Exchange(ref surfaceResizePending, 0) == 0)
            {
                return;
            }

            // SurfaceView can deliver the initial surface notification after
            // CreateSwapChain. Rebuilding that very same swap chain on its
            // first draw needlessly invalidates images already in use.
            if (Volatile.Read(ref forceSurfaceRefresh) == 0 &&
                lastSwapChainWidth == surface.Width &&
                lastSwapChainHeight == surface.Height)
            {
                Log.Info(VulkanTag, "ARCORE_DUPLICATE_SURFACE_RESIZE_SKIPPED");
                return;
            }

            try
            {
                if (Volatile.Read(ref forceSurfaceRefresh) != 0)
                    currentSwapChain.RefreshSurfaceInfo(surface.SurfaceInfo);
                if (lastSwapChainWidth != surface.Width ||
                    lastSwapChainHeight != surface.Height)
                {
                    EvergineArCameraFrameImporter.WaitForGraphicsDeviceIdle(
                        graphicsContext ?? throw new InvalidOperationException(
                            "Graphics context is unavailable."));
                    currentSwapChain.ResizeSwapChain(surface.Width, surface.Height);
                }
                lastSwapChainWidth = surface.Width;
                lastSwapChainHeight = surface.Height;
                Interlocked.Exchange(ref forceSurfaceRefresh, 0);
                Log.Info(VulkanTag,
                    $"ARCORE_SURFACE_RESIZED width={surface.Width}, " +
                    $"height={surface.Height}.");
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
            Surface surface)
        {
            graphicsOwnerThreadId =
                Environment.CurrentManagedThreadId;

            Volatile.Write(
                ref surfaceDetachAcknowledgementLogged,
                0);

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

            graphicsContext =
                createdGraphicsContext;

            arCoreService =
                MauiProgram.Services
                    .GetRequiredService<IArCoreService>()
                as ArCoreService;

            if (arCoreService is null)
            {
                throw new InvalidOperationException(
                    "The registered IArCoreService is not an " +
                    $"{nameof(ArCoreService)} instance.");
            }

            createdGraphicsContext.CreateDevice();

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
                    createdGraphicsContext);

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

            lastSwapChainWidth = surface.Width;
            lastSwapChainHeight = surface.Height;

            swapChain.VerticalSync =
                true;

            var graphicsPresenter =
                application.Container
                    .Resolve<GraphicsPresenter>();

            var firstDisplay =
                new global::Evergine.Framework.Graphics.Display(
                    surface,
                    swapChain);

            graphicsPresenter.AddDisplay(
                "DefaultDisplay",
                firstDisplay);

            application.Container.RegisterInstance(
                createdGraphicsContext);
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
