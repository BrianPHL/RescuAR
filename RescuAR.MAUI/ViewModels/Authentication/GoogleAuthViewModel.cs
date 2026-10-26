using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RescuAR.App.Services.Authentication;
using RescuAR.App.Views.Authentication;

namespace RescuAR.App.ViewModels.Authentication;

public partial class GoogleAuthViewModel(IServiceProvider serviceProvider, AuthenticationService authService) : ObservableObject
{
    [ObservableProperty] private string appName = "RescuAR";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private string errorMessage = string.Empty;
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);
    public bool IsNotLoading => !IsLoading;
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsNotLoading));
    partial void OnErrorMessageChanged(string value) => OnPropertyChanged(nameof(HasError));

    [RelayCommand]
    private Task SelectAccount(string email) => AuthenticateGoogleAsync();

    [RelayCommand]
    private Task UseAnotherAccount() => AuthenticateGoogleAsync();

    private async Task AuthenticateGoogleAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            // A display label is never an authenticated identity; Google chooses the actual account.
            var session = await authService.SignInWithGoogleAsync();
            await AuthenticationNavigation.CompleteSignInAsync(serviceProvider,
                session.User?.Id ?? throw new InvalidOperationException("Google did not return a signed-in account."));
        }
        catch (OperationCanceledException) { ErrorMessage = "Google sign-in was cancelled or timed out. Please retry."; }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (IsLoading) return;
        MainThread.BeginInvokeOnMainThread(() => AuthenticationNavigation.TrySetRootPage(
            new NavigationPage(serviceProvider.GetRequiredService<LoginPage>())));
    }
}
