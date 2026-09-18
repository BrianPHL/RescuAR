using Android.App;
using Android.Content.PM;
using Android.OS;
using Microsoft.Extensions.DependencyInjection;
using RescuAR.MAUI.Services;

namespace RescuAR.MAUI
{
    [Activity(
        Theme = "@style/Maui.SplashTheme",
        MainLauncher = true,
        LaunchMode = LaunchMode.SingleTop,
        ConfigurationChanges =
            ConfigChanges.ScreenSize |
            ConfigChanges.Orientation |
            ConfigChanges.UiMode |
            ConfigChanges.ScreenLayout |
            ConfigChanges.SmallestScreenSize |
            ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnResume()
        {
            base.OnResume();

            ResolveArCoreService()
                ?.NotifyActivityResumed();
        }

        protected override void OnPause()
        {
            ResolveArCoreService()
                ?.NotifyActivityPaused();

            base.OnPause();
        }

        protected override void OnDestroy()
        {
            if (IsFinishing)
            {
                ResolveArCoreService()
                    ?.RequestShutdown(
                        "Main Activity is finishing");
            }

            base.OnDestroy();
        }

        private static IArCoreService? ResolveArCoreService()
        {
            try
            {
                return MauiProgram.Services?
                    .GetService<IArCoreService>();
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }
    }
}
