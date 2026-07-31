using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace RescuAR.App.ViewModels.Profile
{
    /// <summary>
    /// ViewModel for the System Information page.
    /// Displays app version, last updated, and device permission statuses.
    /// </summary>
    public partial class SystemInformationViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _appVersion = "v0.0.1a";

        [ObservableProperty]
        private string _lastUpdated = "03/24/2026 10:01 AM";

        [ObservableProperty]
        private ObservableCollection<PermissionStatusItem> _permissions = new();

        public SystemInformationViewModel()
        {
            LoadPermissions();
        }

        [RelayCommand]
        private async Task GoBack()
        {
            await Shell.Current.GoToAsync("..");
        }

        [RelayCommand]
        private async Task ManagePermissions()
        {
            try
            {
                AppInfo.Current.ShowSettingsUI();
            }
            catch (Exception)
            {
                // Settings UI not available on this platform
            }
        }

        /// <summary>
        /// Loads and checks the current device permission statuses.
        /// </summary>
        private void LoadPermissions()
        {
            // Default statuses — in a real app these would be checked at runtime
            Permissions = new ObservableCollection<PermissionStatusItem>
            {
                new() { Name = "Camera", Status = "Allowed", IsAllowed = true },
                new() { Name = "Location", Status = "Allowed", IsAllowed = true },
                new() { Name = "Motion", Status = "Allowed", IsAllowed = true },
                new() { Name = "Notifications", Status = "Denied", IsAllowed = false }
            };
        }

        /// <summary>
        /// Refreshes permission statuses (can be called when page reappears).
        /// </summary>
        [RelayCommand]
        private void RefreshPermissions()
        {
            LoadPermissions();
        }
    }

    /// <summary>
    /// Represents a single permission row in the System Information page.
    /// </summary>
    public partial class PermissionStatusItem : ObservableObject
    {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _status = string.Empty;

        [ObservableProperty]
        private bool _isAllowed;
    }
}
