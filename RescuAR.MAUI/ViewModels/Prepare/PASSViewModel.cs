using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private bool _isModalVisible = false;

    [ObservableProperty]
    private int _scorePercentage = 0;

    [ObservableProperty]
    private double _progressValue = 0.0;

    [ObservableProperty]
    private string _scoreStatus = "Not Assessed";

    [ObservableProperty]
    private string _statusColor = "#64748B";

    [ObservableProperty]
    private string _statusBadgeBg = "#F1F5F9";

    [ObservableProperty]
    private string _lastAssessedText = "Not assessed yet";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoHistory))]
    private bool _hasHistory = false;

    public bool HasNoHistory => !HasHistory;

    [ObservableProperty]
    private bool _isEmergencySuppliesExpanded = true;

    [ObservableProperty]
    private bool _isEvacuationReadinessExpanded = true;

    [ObservableProperty]
    private bool _isEmergencyCommunicationExpanded = false;

    [ObservableProperty]
    private bool _isHouseholdPreparednessExpanded = false;

    public ObservableCollection<AssessmentHistoryItem> History { get; } = new();

    public PASSViewModel()
    {
        RefreshScore();
        LoadHistory();
    }

    public void RefreshScore()
    {
        string userId = RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id 
                        ?? Preferences.Get("CurrentUserId", "");

        bool hasUserScore = !string.IsNullOrWhiteSpace(userId) && Preferences.ContainsKey($"PASS_Score_{userId}");
        bool hasGlobalScore = Preferences.ContainsKey("PASS_Score");

        if (hasUserScore)
        {
            ScorePercentage = Preferences.Get($"PASS_Score_{userId}", 0);
            ScoreStatus = Preferences.Get($"PASS_Status_{userId}", "Prepared");
            string date = Preferences.Get($"PASS_LastDate_{userId}", DateTime.Now.ToString("MMMM dd, yyyy"));
            LastAssessedText = $"Last assessed {date}";
        }
        else if (hasGlobalScore)
        {
            ScorePercentage = Preferences.Get("PASS_Score", 0);
            ScoreStatus = Preferences.Get("PASS_Status", "Prepared");
            string date = Preferences.Get("PASS_LastDate", DateTime.Now.ToString("MMMM dd, yyyy"));
            LastAssessedText = $"Last assessed {date}";
        }
        else
        {
            ScorePercentage = 0;
            ScoreStatus = "Not Assessed";
            LastAssessedText = "Not assessed yet";
        }

        ProgressValue = ScorePercentage / 100.0;

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
        else if (ScorePercentage > 0)
        {
            StatusColor = "#B45309";
            StatusBadgeBg = "#FEF3C7";
        }
        else
        {
            StatusColor = "#64748B";
            StatusBadgeBg = "#F1F5F9";
        }
    }

    public void LoadHistory()
    {
        History.Clear();

        try
        {
            string userId = RescuAR.Services.SupabaseService.Instance.Client?.Auth.CurrentUser?.Id 
                            ?? Preferences.Get("CurrentUserId", "");

            string historyJson = "";
            if (!string.IsNullOrWhiteSpace(userId))
            {
                historyJson = Preferences.Get($"PASS_AssessmentHistoryJson_{userId}", "");
            }

            if (string.IsNullOrWhiteSpace(historyJson))
            {
                historyJson = Preferences.Get("PASS_AssessmentHistoryJson", "");
            }

            if (!string.IsNullOrWhiteSpace(historyJson))
            {
                var items = System.Text.Json.JsonSerializer.Deserialize<List<AssessmentHistoryItem>>(historyJson);
                if (items != null && items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        History.Add(item);
                    }
                    HasHistory = History.Count > 0;
                    return;
                }
            }

            // If user has a current score recorded but no history array yet, display their initial recorded score
            bool hasScore = (!string.IsNullOrWhiteSpace(userId) && Preferences.ContainsKey($"PASS_Score_{userId}"))
                            || Preferences.ContainsKey("PASS_Score");
            if (hasScore)
            {
                int currentScore = !string.IsNullOrWhiteSpace(userId) && Preferences.ContainsKey($"PASS_Score_{userId}")
                    ? Preferences.Get($"PASS_Score_{userId}", 0)
                    : Preferences.Get("PASS_Score", 0);
                string currentStatus = !string.IsNullOrWhiteSpace(userId) && Preferences.ContainsKey($"PASS_Status_{userId}")
                    ? Preferences.Get($"PASS_Status_{userId}", "Prepared")
                    : Preferences.Get("PASS_Status", "Prepared");
                string currentDate = !string.IsNullOrWhiteSpace(userId) && Preferences.ContainsKey($"PASS_LastDate_{userId}")
                    ? Preferences.Get($"PASS_LastDate_{userId}", DateTime.Now.ToString("MMMM dd, yyyy"))
                    : Preferences.Get("PASS_LastDate", DateTime.Now.ToString("MMMM dd, yyyy"));
                string color = currentScore >= 80 ? "#15803D" : currentScore >= 60 ? "#0A8491" : "#D97706";

                History.Add(new AssessmentHistoryItem
                {
                    Date = currentDate,
                    ScoreText = $"{currentScore}%",
                    Status = currentStatus,
                    StatusColor = color
                });
                HasHistory = true;
            }
            else
            {
                HasHistory = false;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading assessment history: {ex.Message}");
            HasHistory = History.Count > 0;
        }
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

    [RelayCommand]
    private async Task BackAsync()
    {
        if (Shell.Current != null)
        {
            try
            {
                await Shell.Current.GoToAsync("..");
            }
            catch
            {
                await Shell.Current.GoToAsync("//Home");
            }
        }
    }
}
