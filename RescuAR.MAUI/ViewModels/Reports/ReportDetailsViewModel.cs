using System;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;

namespace RescuAR.App.ViewModels.Reports
{
    [QueryProperty(nameof(ReportId), "ReportId")]
    public partial class ReportDetailsViewModel : ObservableObject
    {
        private readonly CommunityReportService _reportService;

        [ObservableProperty]
        private string reportId = string.Empty;

        [ObservableProperty]
        private CommunityReport? report;

        [ObservableProperty]
        private string newCommentText = string.Empty;

        [ObservableProperty]
        private bool hasNoComments;

        public ReportDetailsViewModel(CommunityReportService reportService)
        {
            _reportService = reportService;
        }

        partial void OnReportIdChanged(string value)
        {
            _ = LoadReportAsync(value);
        }

        public async Task LoadReportAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;

            var match = _reportService.Reports.FirstOrDefault(r => r.Id == id);
            if (match != null)
            {
                Report = match;
                UpdateNoCommentsState();
                _ = _reportService.EnrichCommentsUsernamesAsync(Report.Comments);
                return;
            }

            try
            {
                var liveReports = await _reportService.GetReportsAsync();
                match = liveReports.FirstOrDefault(r => r.Id == id);
                if (match != null)
                {
                    Report = match;
                    UpdateNoCommentsState();
                    _ = _reportService.EnrichCommentsUsernamesAsync(Report.Comments);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading report details: {ex.Message}");
            }
        }

        private void UpdateNoCommentsState()
        {
            HasNoComments = Report == null || Report.Comments.Count == 0;
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            await Shell.Current.GoToAsync("..");
        }

        [RelayCommand]
        private async Task AddCommentAsync()
        {
            if (Report == null || string.IsNullOrWhiteSpace(NewCommentText)) return;

            if (!Report.AllowComments)
            {
                if (Shell.Current != null)
                {
                    await Shell.Current.DisplayAlert("Comments Disabled", "Comments are disabled for this report.", "OK");
                }
                return;
            }

            var text = NewCommentText.Trim();
            NewCommentText = string.Empty;

            var (authorName, userId) = await GetCurrentCommenterInfoAsync();
            await _reportService.AddCommentAsync(Report.Id, text, authorName, userId);
            UpdateNoCommentsState();
        }

        private async Task<(string authorName, string userId)> GetCurrentCommenterInfoAsync()
        {
            string userId = string.Empty;
            string resolvedUsername = string.Empty;

            try
            {
                var client = await RescuAR.Services.SupabaseService.Instance.GetClientAsync();
                var authUser = client?.Auth?.CurrentUser ?? client?.Auth?.CurrentSession?.User;
                if (authUser != null)
                {
                    userId = authUser.Id ?? string.Empty;

                    // 1. Retrieve the registered username from the user profile table
                    if (!string.IsNullOrWhiteSpace(userId) && client != null)
                    {
                        try
                        {
                            var userResp = await client.From<User>()
                                .Filter("id", Supabase.Postgrest.Constants.Operator.Equals, userId)
                                .Get();

                            var dbUser = userResp?.Models?.FirstOrDefault();
                            if (dbUser != null)
                            {
                                if (!string.IsNullOrWhiteSpace(dbUser.Username))
                                {
                                    resolvedUsername = dbUser.Username.Trim();
                                }
                                else
                                {
                                    string full = $"{dbUser.FirstName} {dbUser.LastName}".Trim();
                                    if (!string.IsNullOrWhiteSpace(full))
                                    {
                                        resolvedUsername = full;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error retrieving commenter profile: {ex.Message}");
                        }
                    }

                    // 2. Fallback to auth metadata if user profile record was not retrieved
                    if (string.IsNullOrWhiteSpace(resolvedUsername) && authUser.UserMetadata != null)
                    {
                        if (authUser.UserMetadata.TryGetValue("username", out var u) && u != null && !string.IsNullOrWhiteSpace(u.ToString()))
                        {
                            resolvedUsername = u.ToString()!.Trim();
                        }
                        else if (authUser.UserMetadata.TryGetValue("full_name", out var fn) && fn != null && !string.IsNullOrWhiteSpace(fn.ToString()))
                        {
                            resolvedUsername = fn.ToString()!.Trim();
                        }
                        else if (authUser.UserMetadata.TryGetValue("name", out var n) && n != null && !string.IsNullOrWhiteSpace(n.ToString()))
                        {
                            resolvedUsername = n.ToString()!.Trim();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking current user session: {ex.Message}");
            }

            // 3. Fallback to locally saved preferences if available
            if (string.IsNullOrWhiteSpace(resolvedUsername))
            {
                string prefUser = Microsoft.Maui.Storage.Preferences.Default.Get("UserName", "").Trim();
                if (!string.IsNullOrWhiteSpace(prefUser) && !string.Equals(prefUser, "User", StringComparison.OrdinalIgnoreCase))
                {
                    resolvedUsername = prefUser;
                }
                else
                {
                    string fname = Microsoft.Maui.Storage.Preferences.Default.Get("UserFirstName", "").Trim();
                    string lname = Microsoft.Maui.Storage.Preferences.Default.Get("UserLastName", "").Trim();
                    string storedFullName = $"{fname} {lname}".Trim();
                    if (!string.IsNullOrWhiteSpace(storedFullName))
                    {
                        resolvedUsername = storedFullName;
                    }
                }
            }

            // 4. Default fallback name if unavailable
            if (string.IsNullOrWhiteSpace(resolvedUsername) || string.Equals(resolvedUsername, "User", StringComparison.OrdinalIgnoreCase))
            {
                resolvedUsername = "Resident";
            }

            return (resolvedUsername, userId);
        }

        [RelayCommand]
        private async Task ToggleLikeAsync()
        {
            if (Report == null) return;
            await _reportService.ToggleLikeAsync(Report.Id);
        }
    }
}
