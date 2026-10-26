using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RescuAR.App.Services.Authentication;

namespace RescuAR.App.ViewModels.Authentication;

public partial class LoginViewModel
{
    private readonly PasswordRecoveryFlow passwordRecovery = new(new MauiPasswordRecoveryFactory());
    private long resetOperation;

    [ObservableProperty] private bool isResetPasswordModalVisible;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResetEmailStep), nameof(IsResetCodeStep))]
    private int resetStep = 1;
    [ObservableProperty] private string resetEmail = string.Empty;
    [ObservableProperty] private string resetOtpCode = string.Empty;
    [ObservableProperty] private string resetNewPassword = string.Empty;
    [ObservableProperty] private string resetConfirmPassword = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResetError))]
    private string resetErrorMessage = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResetNotLoading))]
    private bool isResetLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResetEmailStep), nameof(IsResetCodeStep))]
    private bool isResetSuccess;

    public bool HasResetError => !string.IsNullOrEmpty(ResetErrorMessage);
    public bool IsResetNotLoading => !IsResetLoading;
    public bool IsResetEmailStep => ResetStep == 1 && !IsResetSuccess;
    public bool IsResetCodeStep => ResetStep == 2 && !IsResetSuccess;

    [RelayCommand]
    private void OpenResetPasswordModal()
    {
        if (IsLoading) return;
        CancelPasswordReset();
        ResetEmail = Email ?? string.Empty;
        IsResetPasswordModalVisible = true;
    }

    [RelayCommand]
    private void CloseResetPasswordModal() => CancelPasswordReset();

    public void CancelPasswordReset()
    {
        Interlocked.Increment(ref resetOperation);
        passwordRecovery.Cancel();
        IsResetPasswordModalVisible = false;
        IsResetLoading = false;
        IsResetSuccess = false;
        ResetStep = 1;
        ResetEmail = string.Empty;
        ResetOtpCode = string.Empty;
        ResetNewPassword = string.Empty;
        ResetConfirmPassword = string.Empty;
        ResetErrorMessage = string.Empty;
    }

    [RelayCommand]
    private async Task SendResetOtp()
    {
        if (IsResetLoading || !IsResetPasswordModalVisible) return;
        long operation = Interlocked.Increment(ref resetOperation);
        IsResetLoading = true;
        ResetErrorMessage = string.Empty;
        try
        {
            await passwordRecovery.RequestAsync(ResetEmail ?? string.Empty);
            if (operation == Volatile.Read(ref resetOperation)) ResetStep = 2;
        }
        catch (Exception ex)
        {
            if (operation == Volatile.Read(ref resetOperation)) ResetErrorMessage = ex is OperationCanceledException
                ? "Recovery was cancelled. Please request a new code." : ex.Message;
        }
        finally { if (operation == Volatile.Read(ref resetOperation)) IsResetLoading = false; }
    }

    [RelayCommand]
    private async Task ConfirmResetPassword()
    {
        if (IsResetLoading || !IsResetPasswordModalVisible) return;
        long operation = Interlocked.Increment(ref resetOperation);
        IsResetLoading = true;
        ResetErrorMessage = string.Empty;
        try
        {
            await passwordRecovery.ConfirmAsync(ResetOtpCode ?? string.Empty, ResetNewPassword ?? string.Empty, ResetConfirmPassword ?? string.Empty);
            if (operation == Volatile.Read(ref resetOperation))
            {
                ResetOtpCode = ResetNewPassword = ResetConfirmPassword = string.Empty;
                IsResetSuccess = true;
            }
        }
        catch (Exception ex)
        {
            if (operation == Volatile.Read(ref resetOperation)) ResetErrorMessage = ex is OperationCanceledException
                ? "Recovery was cancelled. Please request a new code." : ex.Message;
        }
        finally { if (operation == Volatile.Read(ref resetOperation)) IsResetLoading = false; }
    }
}
