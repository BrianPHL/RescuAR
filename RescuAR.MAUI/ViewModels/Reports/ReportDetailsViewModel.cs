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
            LoadReport(value);
        }

        public void LoadReport(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            foreach (var r in _reportService.Reports)
            {
                if (r.Id == id)
                {
                    Report = r;
                    UpdateNoCommentsState();
                    break;
                }
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

            var text = NewCommentText.Trim();
            NewCommentText = string.Empty;

            await _reportService.AddCommentAsync(Report.Id, text, "Aubrey T.");
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
