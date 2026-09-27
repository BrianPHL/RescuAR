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
        private readonly object graphicsTeardownSync = new();
        private Task? graphicsTeardownTask;
        private long graphicsGeneration;
        private int graphicsOwnerThreadId;
        private int surfaceDetachAcknowledgementLogged;
        private int surfaceRecreationPending;
        private int surfaceRecoveryInProgress;
        private int surfaceResizePending;
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
        private MyApplication? rescuarApplication;

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

            this.rescuarApplication =
                rescuarApplication;

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
                    ConfigureGraphicsContext(
                        rescuarApplication,
                        currentSurface);

                    application.Initialize();
                },
                () =>
                {
                    ProcessPendingSurfaceResize();

                    if (!renderingEnabled)
                    {
                        return;
                    }

                    TimeSpan gameTime =
                        clockTimer.Elapsed;

                    clockTimer.Restart();

                    application.UpdateFrame(
                        gameTime);

                    application.DrawFrame(
                        gameTime);
                });
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

        protected override void DisconnectHandler(
            AndroidSurfaceView platformView)
        {
            handlerConnected =
                false;

            Interlocked.Exchange(
                ref surfaceRecreationPending,
                0);

            Interlocked.Exchange(
                ref surfaceResizePending,
                0);

            AndroidSurface? surface =
                androidSurface;

            if (surface is not null)
            {
                surface.OnScreenSizeChanged -=
                    AndroidSurface_OnScreenSizeChanged;

                surface.OnSurfaceInfoChanged -=
                    AndroidSurface_OnSurfaceInfoChanged;

                surface.Closing -=
                    AndroidSurface_OnClosing;
            }

            Task teardown =
                StartGraphicsTeardown(
                    "MAUI handler disconnect");

            try
            {
                /*
                 * Do not let base.DisconnectHandler detach the Android
                 * surface until the AR producer, draw callback, and Vulkan
                 * device have all acknowledged the old generation.
                 */
                teardown.WaitAsync(
                        TimeSpan.FromSeconds(18))
                    .GetAwaiter()
                    .GetResult();

                LogSurfaceDetachAcknowledgement();
            }
            catch (Exception exception)
            {
                Log.Error(
                    ArCoreTag,
                    "Refusing to detach the Android rendering surface before " +
                    $"AR/Vulkan quiescence completed: {exception}");

                return;
            }

            arCoreService =
                null;

            rescuarApplication =
                null;

            graphicsContext =
                null;

            graphicsGeneration =
                0;

            graphicsOwnerThreadId =
                0;

            lock (graphicsTeardownSync)
            {
                graphicsTeardownTask = null;
            }

            lastArCoreRotation =
                int.MinValue;

            lastArCoreWidth =
                -1;

            lastArCoreHeight =
                -1;

            base.DisconnectHandler(
                platformView);
        }

        private void AndroidSurface_OnClosing(
            object? sender,
            EventArgs e)
        {
            if (androidSurface is not { } surface)
            {
                return;
            }

            Interlocked.Exchange(
                ref surfaceRecreationPending,
                1);

            /*
             * Android destroys and recreates the SurfaceView when the app is
             * backgrounded. Keep the callbacks subscribed so the recreated
             * surface can re-register the retained Vulkan context. A permanent
             * MAUI handler disconnect removes the callbacks in DisconnectHandler.
             * Start rejecting frames immediately; DisconnectHandler consumes the
             * same task and performs the mandatory bounded wait before detach.
             */
            if (Environment.CurrentManagedThreadId == graphicsOwnerThreadId &&
                arCoreService is not null &&
                graphicsContext is not null &&
                graphicsGeneration > 0)
            {
                try
                {
                    arCoreService.QuiesceGraphicsContextAtSurfaceBoundary(
                        graphicsContext,
                        graphicsGeneration,
                        "Android surface closing on graphics-owner thread");

                    lock (graphicsTeardownSync)
                    {
                        graphicsTeardownTask = Task.CompletedTask;
                    }

                    LogSurfaceDetachAcknowledgement();
                }
                catch (Exception exception)
                {
                    lock (graphicsTeardownSync)
                    {
                        graphicsTeardownTask = Task.FromException(exception);
                    }

                    Log.Error(
                        ArCoreTag,
                        "Owner-thread surface teardown failed: " +
                        exception);
                }

                return;
            }

            _ = StartGraphicsTeardown(
                "Android surface closing");
        }

        private Task StartGraphicsTeardown(
            string reason)
        {
            lock (graphicsTeardownSync)
            {
                if (graphicsTeardownTask is not null)
                {
                    return graphicsTeardownTask;
                }

                ArCoreService? currentArCoreService =
                    arCoreService;

                VKGraphicsContext? currentGraphicsContext =
                    graphicsContext;

                long currentGraphicsGeneration =
                    graphicsGeneration;

                if (currentArCoreService is null ||
                    currentGraphicsContext is null ||
                    currentGraphicsGeneration <= 0)
                {
                    return Task.CompletedTask;
                }

                graphicsTeardownTask =
                    currentArCoreService.QuiesceGraphicsContextAsync(
                        currentGraphicsContext,
                        currentGraphicsGeneration,
                        reason);

                return graphicsTeardownTask;
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
            object? sender,
            SurfaceInfo surfaceInfo)
        {
            if (androidSurface is not { } surface)
            {
                return;
            }

            // The callback may run on Android's UI thread while Evergine
            // owns the swap chain on its independent graphics thread.
            // Apply the latest surface info on that owner thread instead.
            Interlocked.Exchange(
                ref surfaceResizePending,
                1);

            if (Volatile.Read(
                    ref surfaceRecreationPending) !=
                0)
            {
                _ = RecoverGraphicsContextAfterSurfaceRecreationAsync(
                    surface);
            }

            /*
             * SurfaceInfo can change because of Android display changes or
             * because the backing surface was recreated. Re-submit the
             * actual Evergine viewport geometry to ARCore.
             */
            UpdateArCoreDisplayGeometry(
                surface);

            /*
             * Preserve the previous handler behavior.
             */
            surface.OnScreenSizeChanged -=
                AndroidSurface_OnScreenSizeChanged;

            surface.OnScreenSizeChanged +=
                AndroidSurface_OnScreenSizeChanged;
        }

        private async Task RecoverGraphicsContextAfterSurfaceRecreationAsync(
            AndroidSurface surface)
        {
            if (Interlocked.CompareExchange(
                    ref surfaceRecoveryInProgress,
                    1,
                    0) !=
                0)
            {
                return;
            }

            try
            {
                Task? teardown;

                lock (graphicsTeardownSync)
                {
                    teardown =
                        graphicsTeardownTask;
                }

                if (teardown is not null)
                {
                    await teardown.WaitAsync(
                            TimeSpan.FromSeconds(18))
                        .ConfigureAwait(false);
                }

                if (!handlerConnected ||
                    Volatile.Read(
                        ref surfaceRecreationPending) ==
                    0)
                {
                    return;
                }

                ArCoreService? currentArCoreService =
                    arCoreService;

                VKGraphicsContext? retainedGraphicsContext =
                    graphicsContext;

                MyApplication? application =
                    rescuarApplication;

                if (currentArCoreService is null ||
                    retainedGraphicsContext is null ||
                    application is null)
                {
                    Log.Warn(
                        ArCoreTag,
                        "The recreated Android surface could not restore AR " +
                        "because its retained graphics owner is unavailable.");

                    return;
                }

                long restoredGraphicsGeneration =
                    currentArCoreService.SetGraphicsContext(
                        retainedGraphicsContext);

                graphicsGeneration =
                    restoredGraphicsGeneration;

                application.BindArGraphicsGeneration(
                    restoredGraphicsGeneration);

                lock (graphicsTeardownSync)
                {
                    graphicsTeardownTask =
                        null;
                }

                Interlocked.Exchange(
                    ref surfaceRecreationPending,
                    0);

                Interlocked.Exchange(
                    ref surfaceResizePending,
                    1);

                Volatile.Write(
                    ref surfaceDetachAcknowledgementLogged,
                    0);

                UpdateArCoreDisplayGeometry(
                    surface);

                Log.Info(
                    ArCoreTag,
                    "ARCORE_SURFACE_RECREATED " +
                    $"graphicsGeneration={restoredGraphicsGeneration}.");
            }
            catch (Exception exception)
            {
                Log.Error(
                    ArCoreTag,
                    "Failed to restore AR rendering after Android surface " +
                    $"recreation: {exception}");
            }
            finally
            {
                Interlocked.Exchange(
                    ref surfaceRecoveryInProgress,
                    0);
            }
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
                !renderingEnabled ||
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

            try
            {
                currentSwapChain.RefreshSurfaceInfo(surface.SurfaceInfo);
                currentSwapChain.ResizeSwapChain(surface.Width, surface.Height);
            }
            catch (Exception exception)
            {
                // A failed Vulkan format negotiation must not crash the UI
                // thread or keep submitting frames to an invalid swap chain.
                SuspendRendering();
                surfaceResizeFailed = true;
                ARCameraSpatialController.SetRouteRenderingEnabled(
                    false,
                    "Android surface resize failed");
                Log.Error(
                    VulkanTag,
                    "AR surface resize failed; rendering stopped until the " +
                    $"Camera view is reopened: {exception}");
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

            lock (graphicsTeardownSync)
            {
                graphicsTeardownTask = null;
            }

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
