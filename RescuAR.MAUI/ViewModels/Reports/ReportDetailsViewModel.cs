using System;
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

            var authorName = Microsoft.Maui.Storage.Preferences.Default.Get("UserName", "User");
            await _reportService.AddCommentAsync(Report.Id, text, authorName);
            UpdateNoCommentsState();
        }

        [RelayCommand]
        private async Task ToggleLikeAsync()
        {
            if (Report == null) return;
            await _reportService.ToggleLikeAsync(Report.Id);
        }
    }
}
