using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Networking;

namespace RescuAR.App.ViewModels.Reports
{
    public partial class ConnectivityMonitoringViewModel : ObservableObject
    {
        private readonly IConnectivity _connectivity;
        private bool _isMonitoring;
        private bool _isReconnecting;

        [ObservableProperty] private bool isInternetAvailable;
        [ObservableProperty] private string title = "Checking connection...";
        [ObservableProperty] private string message = "Checking internet availability for community reports.";
        [ObservableProperty] private string backgroundColor = "#FFFBEB";
        [ObservableProperty] private string foregroundColor = "#92400E";

        public event EventHandler? ConnectionRestored;

        public ConnectivityMonitoringViewModel() : this(Microsoft.Maui.Networking.Connectivity.Current)
        {
        }

        public ConnectivityMonitoringViewModel(IConnectivity connectivity)
        {
            _connectivity = connectivity;
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;
            _isMonitoring = true;
            _connectivity.ConnectivityChanged += OnConnectivityChanged;
            Refresh();
        }

        public void StopMonitoring()
        {
            _isMonitoring = false;
            _connectivity.ConnectivityChanged -= OnConnectivityChanged;
            _isReconnecting = false;
        }

        public void CompleteRefresh()
        {
            _isReconnecting = false;
            Refresh();
        }

        public void Refresh()
        {
            var access = _connectivity.NetworkAccess;
            var profiles = _connectivity.ConnectionProfiles.ToArray();
            var hasWifi = profiles.Contains(ConnectionProfile.WiFi);
            var hasCellular = profiles.Contains(ConnectionProfile.Cellular);
            IsInternetAvailable = access == NetworkAccess.Internet;

            if (IsInternetAvailable)
            {
                Title = _isReconnecting ? "Reconnecting..." :
                    hasWifi ? "Wi-Fi connected" : hasCellular ? "Mobile data connected" : "Connected";
                Message = _isReconnecting
                    ? "Connection restored. Refreshing community reports..."
                    : "Internet is available. Pull down to refresh community reports.";
                BackgroundColor = _isReconnecting ? "#FFFBEB" : "#ECFDF5";
                ForegroundColor = _isReconnecting ? "#92400E" : "#065F46";
            }
            else
            {
                _isReconnecting = false;
                Title = access == NetworkAccess.Unknown ? "Connection unknown" :
                    access == NetworkAccess.ConstrainedInternet ? (hasWifi ? "Wi-Fi sign-in required" : "Network sign-in required") :
                    hasWifi ? "Wi-Fi has no internet" : "Offline";
                Message = access == NetworkAccess.ConstrainedInternet
                    ? "Sign in to this network to receive new community reports. Displayed reports may be out of date."
                    : access == NetworkAccess.Unknown
                        ? "Internet access could not be confirmed. New community reports may be unavailable."
                        : "New community reports cannot load. Check your connection. Displayed reports may be out of date.";
                BackgroundColor = access == NetworkAccess.None ? "#FEF2F2" : "#FFFBEB";
                ForegroundColor = access == NetworkAccess.None ? "#991B1B" : "#92400E";
            }
        }

        private void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs args)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_isMonitoring) return;
                var wasOnline = IsInternetAvailable;
                Refresh();
                if (!wasOnline && IsInternetAvailable)
                {
                    _isReconnecting = true;
                    Refresh();
                    ConnectionRestored?.Invoke(this, EventArgs.Empty);
                }
            });
        }
    }
}
