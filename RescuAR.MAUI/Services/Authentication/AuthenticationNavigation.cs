using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection;
using RescuAR.App.Views.Authentication;

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

    public static async Task CompleteSignInAsync(IServiceProvider services, string expectedUserId)
    {
        if (string.IsNullOrWhiteSpace(expectedUserId)) throw new InvalidOperationException("Please sign in again.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await RescuAR.Services.SupabaseService.Instance.SetSessionPersistenceAsync(Preferences.Default.Get("RememberMe", true), expectedUserId);
        var flow = services.GetRequiredService<AuthenticationFlowCoordinator>();
        var decision = await flow.CompleteSignInAsync(expectedUserId, timeout.Token);
        await ApplyDecisionAsync(services, decision);
    }

    public static async Task CompletePermissionsAsync(IServiceProvider services)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var decision = await services.GetRequiredService<AuthenticationFlowCoordinator>().CompletePermissionsAsync(timeout.Token);
        await ApplyDecisionAsync(services, decision);
    }

    internal static async Task ApplyDecisionAsync(IServiceProvider services, AuthenticationDecision decision)
    {
        if (decision.Route == AuthenticationRoute.Retry) throw new InvalidOperationException(decision.Message);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!services.GetRequiredService<AuthenticationFlowCoordinator>().IsCurrent(decision)) return;
            Page page = decision.Route switch
            {
                AuthenticationRoute.Application => new RescuAR.MAUI.AppShell(),
                AuthenticationRoute.Address => new NavigationPage(services.GetRequiredService<AddressInputPage>()),
                AuthenticationRoute.Permissions => services.GetRequiredService<PermissionsPage>(),
                AuthenticationRoute.Login => new NavigationPage(services.GetRequiredService<LoginPage>()),
                _ => new NavigationPage(services.GetRequiredService<OnboardingPage>())
            };
            if (!TrySetRootPage(page)) throw new InvalidOperationException("The app window is not ready. Please retry.");
            if (decision.IsOffline)
                await page.DisplayAlert("Offline access", "You are using your saved account. Offline maps remain available; account changes and cloud updates need a connection.", "Continue");
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
