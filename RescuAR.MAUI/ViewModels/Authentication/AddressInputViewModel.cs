using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using Microsoft.Maui.ApplicationModel;
using RescuAR.App.Views.Authentication;

using RescuAR.App.Services.Authentication;

using RescuAR.MAUI;

namespace RescuAR.App.ViewModels.Authentication
{
    public partial class AddressInputViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty]
        private string _houseLotBlock = string.Empty;

        [ObservableProperty]
        private string _street = string.Empty;

        [ObservableProperty]
        private string _barangay = string.Empty;

        [ObservableProperty]
        private string _city = "Marikina City"; // Fixed

        [ObservableProperty]
        private string _province = "Metro Manila"; // Fixed

        [ObservableProperty]
        private string _zipCode = "1800";

        [ObservableProperty]
        private bool _isLoading = false;

        public bool IsNotLoading => !IsLoading;
        
        [ObservableProperty]
        private string _errorMessage = string.Empty;

        [ObservableProperty]
        private bool _isConfirmPopupVisible = false;

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public AddressInputViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(IsNotLoading));
        }

        [RelayCommand]
        private async Task GetLocationFromMapAsync()
        {
            // Try to get coordinates natively
            try
            {
                IsLoading = true;
                
                var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest
                {
                    DesiredAccuracy = GeolocationAccuracy.Medium,
                    Timeout = TimeSpan.FromSeconds(10)
                });
                
                if (location != null)
                {
                    // Reverse Geocode using Nominatim API (OpenStreetMap)
                    string url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={location.Latitude}&lon={location.Longitude}";
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("User-Agent", "RescuAR.App/1.0"); // Nominatim requires User-Agent
                    var response = await client.GetAsync(url);

                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        var addressNode = doc.RootElement.GetProperty("address");
                        
                        if (addressNode.TryGetProperty("road", out var road)) Street = road.GetString() ?? string.Empty;
                        if (addressNode.TryGetProperty("suburb", out var suburb)) Barangay = suburb.GetString() ?? string.Empty;
                        // Keep City as Marikina City to enforce residency rules
                        
                        if (Shell.Current != null)
                        {
                            await Shell.Current.DisplayAlert("Location Found", "Street and Barangay populated. Please review and ensure you are within Marikina City.", "OK");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddressInput] Location detection failed: {ex.Message}");
                ErrorMessage = "Could not detect location. Please type manually.";
                OnPropertyChanged(nameof(HasError));
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task SaveAddressAsync()
        {
            if (IsLoading) return;

            ErrorMessage = string.Empty;
            OnPropertyChanged(nameof(HasError));

            if (string.IsNullOrWhiteSpace(HouseLotBlock) || string.IsNullOrWhiteSpace(Street) || string.IsNullOrWhiteSpace(Barangay))
            {
                ErrorMessage = "House/Lot/Block, Street, and Barangay are required.";
                OnPropertyChanged(nameof(HasError));
                return;
            }

            // Show custom popup
            IsConfirmPopupVisible = true;
        }

        [RelayCommand]
        private void CancelConfirm()
        {
            IsConfirmPopupVisible = false;
        }

        [RelayCommand]
        private async Task ConfirmAndSaveAddressAsync()
        {
            IsConfirmPopupVisible = false;
            IsLoading = true;

            try
            {
                var client = RescuAR.Services.SupabaseService.Instance.Client;
                if (client != null && client.Auth.CurrentSession != null)
                {
                    // Build full address string
                    string fullAddress = $"{HouseLotBlock.Trim()}, {Street.Trim()}, {Barangay.Trim()}, {City}, {Province}, {ZipCode}";

                    // Get or create current user record.
                    var authUser = client.Auth.CurrentSession.User;
                    var authUserId = authUser?.Id;
                    if (authUser == null || string.IsNullOrWhiteSpace(authUserId))
                    {
                        throw new InvalidOperationException("The authenticated Supabase user is unavailable.");
                    }

                    Models.User? dbUser = null;

                    try
                    {
                        dbUser = await client.From<Models.User>().Where(x => x.Id == authUserId).Single();
                    }
                    catch { }

                    if (dbUser == null)
                    {
                        dbUser = new Models.User
                        {
                            Id = authUserId,
                            Email = authUser.Email ?? string.Empty
                        };

                        if (authUser.UserMetadata != null)
                        {
                            if (authUser.UserMetadata.TryGetValue("first_name", out var fn))
                                dbUser.FirstName = fn?.ToString() ?? string.Empty;
                            if (authUser.UserMetadata.TryGetValue("last_name", out var ln))
                                dbUser.LastName = ln?.ToString() ?? string.Empty;
                            if (authUser.UserMetadata.TryGetValue("phone", out var ph))
                                dbUser.PhoneNumber = ph?.ToString() ?? string.Empty;
                        }
                    }

                    dbUser.Address = fullAddress;
                    await client.From<Models.User>().Upsert(dbUser);

                    // Navigate to Permissions or Dashboard
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (Application.Current != null)
                        {
                            Preferences.Default.Set("HasSignedUp", true);
                            Preferences.Default.Set("IsLoggedIn", true);
                            
                            bool hasPermissions = Preferences.Default.Get("HasCompletedPermissions", false);
                            Page targetPage;
                            if (hasPermissions)
                            {
                                targetPage = new AppShell();
                            }
                            else
                            {
                                targetPage = _serviceProvider.GetRequiredService<PermissionsPage>();
                            }

                            // Use Windows[0].Page for .NET 8+ MAUI root navigation
                            if (Application.Current.Windows.Count > 0)
                            {
                                Application.Current.Windows[0].Page = targetPage;
                            }
                            else
                            {
#pragma warning disable CS0618
                                AuthenticationNavigation.TrySetRootPage(targetPage);
#pragma warning restore CS0618
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Could not save address: {ex.Message}";
                OnPropertyChanged(nameof(HasError));
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
