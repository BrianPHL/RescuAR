using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace RescuAR.App.ViewModels.Prepare;

public class AssessmentHistoryItem
{
    public string Date { get; set; } = string.Empty;
    public string ScoreText { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StatusColor { get; set; } = "#22C55E";
}

public partial class PASSViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsModalVisible { get; set; } = false;

    [ObservableProperty]
    public partial int ScorePercentage { get; set; } = 100;

    [ObservableProperty]
    public partial double ProgressValue { get; set; } = 1.0;

    [ObservableProperty]
    public partial string ScoreStatus { get; set; } = "Highly Prepared";

    [ObservableProperty]
    public partial string StatusColor { get; set; } = "#15803D";

    [ObservableProperty]
    public partial string StatusBadgeBg { get; set; } = "#DCFCE7";

    [ObservableProperty]
    public partial string LastAssessedText { get; set; } = "Last assessed August 03, 2026";

    [ObservableProperty]
    public partial bool IsEmergencySuppliesExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsEvacuationReadinessExpanded { get; set; } = true;

    [ObservableProperty]
    public partial bool IsEmergencyCommunicationExpanded { get; set; } = false;

    [ObservableProperty]
    public partial bool IsHouseholdPreparednessExpanded { get; set; } = false;

    public List<AssessmentHistoryItem> History { get; } = new();

    public PASSViewModel()
    {
        RefreshScore();
        LoadHistory();
    }

    public void RefreshScore()
    {
        ScorePercentage = Preferences.Get("PASS_Score", 100);
        ProgressValue = ScorePercentage / 100.0;
        ScoreStatus = Preferences.Get("PASS_Status", "Highly Prepared");
        string date = Preferences.Get("PASS_LastDate", "June 15, 2026");
        LastAssessedText = $"Last assessed {date}";

        if (ScorePercentage >= 80)
        {
            StatusColor = "#15803D";
            StatusBadgeBg = "#DCFCE7";
        }
        else if (ScorePercentage >= 60)
        {
            StatusColor = "#0A8491";
            StatusBadgeBg = "#E0F2FE";
        }
        else
        {
            StatusColor = "#B45309";
            StatusBadgeBg = "#FEF3C7";
        }
    }

    private void LoadHistory()
    {
        History.Clear();
        History.Add(new AssessmentHistoryItem { Date = "June 15, 2026", ScoreText = "72%", Status = "Prepared", StatusColor = "#0A8491" });
        History.Add(new AssessmentHistoryItem { Date = "May 20, 2026", ScoreText = "65%", Status = "Partially Prepared", StatusColor = "#D97706" });
        History.Add(new AssessmentHistoryItem { Date = "April 10, 2026", ScoreText = "58%", Status = "Needs Work", StatusColor = "#DC2626" });
    }

    [RelayCommand]
    private void ToggleModal()
    {
        IsModalVisible = !IsModalVisible;
    }

    [RelayCommand]
    private async Task StartAssessmentAsync()
    {
        IsModalVisible = false;
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/Assessment");
        }
    }

    [RelayCommand]
    private void ToggleEmergencySupplies()
    {
        IsEmergencySuppliesExpanded = !IsEmergencySuppliesExpanded;
    }

    [RelayCommand]
    private void ToggleEvacuationReadiness()
    {
        IsEvacuationReadinessExpanded = !IsEvacuationReadinessExpanded;
    }

    [RelayCommand]
    private void ToggleEmergencyCommunication()
    {
        IsEmergencyCommunicationExpanded = !IsEmergencyCommunicationExpanded;
    }

    [RelayCommand]
    private void ToggleHouseholdPreparedness()
    {
        IsHouseholdPreparednessExpanded = !IsHouseholdPreparednessExpanded;
    }

    [RelayCommand]
    private async Task NavigateToChecklistAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/Checklist");
        }
    }

    [RelayCommand]
    private async Task NavigateToEvacuationAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
        }
    }
}
