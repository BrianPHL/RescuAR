using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.Services;

namespace RescuAR.App.ViewModels.Prepare;

public partial class HotlineDirectoryItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string BadgeText { get; set; } = "EMS";
    public string Category { get; set; } = "General";
    public string Email { get; set; } = string.Empty;
    public string AlternativeNumber { get; set; } = string.Empty;
}

public partial class HotlineDirectoryViewModel : ObservableObject
{
    private List<HotlineDirectoryItem> _masterHotlines = new();

    public ObservableCollection<HotlineDirectoryItem> FilteredHotlines { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public HotlineDirectoryViewModel()
    {
        LoadDefaultHotlines();
        _ = FetchHotlinesFromSupabaseAsync();
    }

    private void LoadDefaultHotlines()
    {
        _masterHotlines = new List<HotlineDirectoryItem>
        {
            new HotlineDirectoryItem { Name = "Marikina Rescue 161", Number = "(02) 161", Type = "24/7 Emergency Medical & Rescue • Citywide", BadgeText = "EMS", Category = "Medical" },
            new HotlineDirectoryItem { Name = "Marikina PNP Central", Number = "(02) 8405-0091", Type = "Police Emergency Hotline • Citywide", BadgeText = "PNP", Category = "Police" },
            new HotlineDirectoryItem { Name = "Marikina BFP Fire Dept", Number = "(02) 8646-0427", Type = "Fire & Rescue Brigade • Citywide", BadgeText = "BFP", Category = "Fire" },
            new HotlineDirectoryItem { Name = "Red Cross Marikina", Number = "(02) 8681-3442", Type = "Disaster Relief & Blood Bank • Citywide", BadgeText = "PRC", Category = "Relief" },
            new HotlineDirectoryItem { Name = "NDRRMC Hotline", Number = "(02) 8911-1406", Type = "National Disaster Risk Reduction", BadgeText = "GOV", Category = "Relief" },
            new HotlineDirectoryItem { Name = "MMDA Command Center", Number = "136", Type = "Metro Manila Development Authority", BadgeText = "MMDA", Category = "General" }
        };

        ApplyFilter();
    }

    public async Task FetchHotlinesFromSupabaseAsync()
    {
        IsLoading = true;
        try
        {
            var client = await SupabaseService.Instance.GetClientAsync();
            if (client != null)
            {
                var response = await client.From<SupabaseEmergencyHotline>()
                    .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending)
                    .Get();

                if (response?.Models != null && response.Models.Count > 0)
                {
                    var fetchedList = new List<HotlineDirectoryItem>();
                    foreach (var h in response.Models)
                    {
                        string badge = string.IsNullOrWhiteSpace(h.Category) ? "EMS" : h.Category.ToUpper();
                        fetchedList.Add(new HotlineDirectoryItem
                        {
                            Id = h.Id,
                            Name = h.Agency,
                            Number = h.PrimaryNumber,
                            Type = string.IsNullOrWhiteSpace(h.Coverage) ? h.Availability : $"{h.Availability} • {h.Coverage}",
                            BadgeText = badge,
                            Category = h.Category,
                            Email = h.Email,
                            AlternativeNumber = h.AlternativeNumber
                        });
                    }

                    _masterHotlines = fetchedList;
                    MainThread.BeginInvokeOnMainThread(ApplyFilter);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to fetch live hotlines from Supabase: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var items = _masterHotlines.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim().ToLowerInvariant();
            items = items.Where(h => h.Name.ToLowerInvariant().Contains(q) || h.Number.Contains(q) || h.BadgeText.ToLowerInvariant().Contains(q));
        }

        FilteredHotlines.Clear();
        foreach (var item in items)
        {
            FilteredHotlines.Add(item);
        }
    }

    [RelayCommand]
    private async Task MakePhoneCall(string number)
    {
        if (string.IsNullOrWhiteSpace(number)) return;

        try
        {
            string cleanDigits = System.Text.RegularExpressions.Regex.Replace(number, @"[^\d+]", "");
            if (!string.IsNullOrWhiteSpace(cleanDigits))
            {
                var uri = new Uri($"tel:{cleanDigits}");
                await Launcher.Default.OpenAsync(uri);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Phone call error: {ex.Message}");
        }
    }
}
