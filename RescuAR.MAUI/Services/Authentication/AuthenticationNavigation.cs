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
