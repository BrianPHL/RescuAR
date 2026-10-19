using Evergine.Common.IO;
using Evergine.Framework;
using Evergine.Framework.Services;
using Evergine.Framework.Threading;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Data;

namespace RescuAR
{
    public partial class MyApplication : Application
    {
        public MyApplication()
        {
            this.Container.Register<Settings>();
            this.Container.Register<Clock>();
            this.Container.Register<TimerFactory>();
            this.Container.Register<Random>();
            this.Container.Register<ErrorHandler>();
            this.Container.Register<ScreenContextManager>();
            this.Container.Register<GraphicsPresenter>();
            this.Container.Register<AssetsDirectory>();
            this.Container.Register<AssetsService>();
            this.Container.Register<ForegroundTaskSchedulerService>();
            this.Container.Register<WorkActionScheduler>();
        }

        public override void Initialize()
        {
            base.Initialize();

            // Get ScreenContextManager
            var screenContextManager = this.Container.Resolve<ScreenContextManager>();
            var assetsService = this.Container.Resolve<AssetsService>();

            // Navigate to scene
            // Reference the generated factory directly so disabling scene-code
            // generation cannot silently produce an application with no scene.
            AndroidLog.Info("RescuAR-Evergine",
                $"AR_STARTUP_SCENE_LOADING id={EvergineContent.Scenes.MyScene_wescene}; " +
                $"factory={typeof(CompiledMyScene).FullName}.");
            var scene = assetsService.Load<MyScene>(EvergineContent.Scenes.MyScene_wescene)
                ?? throw new System.InvalidOperationException(
                    "The required AR startup scene could not be loaded. " +
                    "Rebuild with GenerateEvergineScenesCode=true and include its scene assets.");
            ScreenContext screenContext = new ScreenContext(scene);
            screenContextManager.To(screenContext);
            AndroidLog.Info("RescuAR-Evergine", "AR_STARTUP_SCENE_QUEUED scene=RescuAR.MyScene.");

            /*
             * Milestone 4 diagnostic:
             * validate the embedded Marikina navigation datasets once without
             * blocking Evergine scene initialization.
             *
             * The task caches its result, so this cannot build the graph more
             * than once per process.
             */
            _ =
                NavigationDataBootstrap.ValidateOnceAsync();
        }
    }
}


