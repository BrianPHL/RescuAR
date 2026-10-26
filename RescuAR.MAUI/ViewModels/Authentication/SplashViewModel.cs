using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RescuAR.App.Services.Authentication;

namespace RescuAR.App.ViewModels.Authentication;

public partial class SplashViewModel(IServiceProvider serviceProvider) : ObservableObject
{
    [ObservableProperty] private string statusText = "Restoring your account...";
    [ObservableProperty] private string versionText = "v0.0.1a";
    [ObservableProperty] private bool isStarting;
    [ObservableProperty] private bool hasStartupError;

    public async Task InitializeAsync()
    {
        if (IsStarting) return;
        IsStarting = true;
        HasStartupError = false;
        StatusText = "Restoring your account...";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            // Keep dev's minimum splash interval while account restoration runs.
            var flow = serviceProvider.GetRequiredService<AuthenticationFlowCoordinator>();
            var startup = flow.StartAsync(timeout.Token);
            await Task.Delay(2000, timeout.Token);
            var decision = await startup;
            if (decision.Route == AuthenticationRoute.Retry)
            {
                StatusText = decision.Message;
                HasStartupError = true;
                return;
            }
            await AuthenticationNavigation.ApplyDecisionAsync(serviceProvider, decision);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Sign-in could not finish. Check your connection and retry.";
            HasStartupError = true;
        }
        catch (Exception)
        {
            StatusText = "Your account could not be restored. Please retry.";
            HasStartupError = true;
        }
        finally { IsStarting = false; }
    }

    [RelayCommand]
    private Task RetryStartupAsync() => InitializeAsync();
}
