using Microsoft.Maui.Controls;

namespace RescuAR.App.Services.Authentication;

internal static class AuthenticationNavigation
{
    public static Page? RootPage
    {
        get
        {
            Application? application =
                Application.Current;

            if (application is null ||
                application.Windows.Count == 0)
            {
                return null;
            }

            return application.Windows[0].Page;
        }
    }

    public static async Task CompleteSignInAsync(IServiceProvider services)
    {
        var profiles = RescuAR.App.Services.Profile.UserProfileService.Instance;
        profiles.ActivateIdentity();
        var user = await profiles.LoadAsync();
        Preferences.Default.Set("HasSignedUp", true);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            Page page = string.IsNullOrWhiteSpace(user.Address)
                ? new NavigationPage(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<RescuAR.App.Views.Authentication.AddressInputPage>(services))
                : Preferences.Default.Get("HasCompletedPermissions", false)
                    ? new RescuAR.MAUI.AppShell()
                    : Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<RescuAR.App.Views.Authentication.PermissionsPage>(services);
            TrySetRootPage(page);
        });
    }

    public static bool TrySetRootPage(
        Page page)
    {
        ArgumentNullException.ThrowIfNull(
            page);

        Application? application =
            Application.Current;

        if (application is null ||
            application.Windows.Count == 0)
        {
            return false;
        }

        application.Windows[0].Page =
            page;

        return true;
    }
}
