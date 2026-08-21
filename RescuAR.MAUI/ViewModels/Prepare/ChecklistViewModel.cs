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

public partial class ChecklistItem : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public string IconEmoji { get; set; } = "💧";
    public string IconBg { get; set; } = "#E0F2FE";

    [ObservableProperty]
    public partial bool IsCompleted { get; set; }
}

public partial class ChecklistViewModel : ObservableObject
{
    private List<ChecklistItem> _allMasterItems = new();

    public ObservableCollection<ChecklistItem> FilteredItems { get; } = new();

    [ObservableProperty]
    public partial double ProgressValue { get; set; } = 0.6;

    [ObservableProperty]
    public partial int PercentReady { get; set; } = 60;

    [ObservableProperty]
    public partial string PreparedCountText { get; set; } = "6 of 10 items prepared";

    [ObservableProperty]
    public partial string SelectedCategory { get; set; } = "All";

    [ObservableProperty]
    public partial bool IsAddModalVisible { get; set; } = false;

    [ObservableProperty]
    public partial string NewItemTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewItemDescription { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewItemCategory { get; set; } = "Food & Water";

    public ChecklistViewModel()
    {
        InitializeItems();
        ApplyFilter();
        UpdateProgress();
    }

    private void InitializeItems()
    {
        _allMasterItems = new List<ChecklistItem>
        {
            new ChecklistItem 
            { 
                Title = "Drinking Water (3-Day Supply)", 
                Description = "At least 1 gallon per person per day.", 
                Category = "Food & Water", 
                IconEmoji = "💧",
                IconBg = "#E0F2FE",
                IsCompleted = Preferences.Get("item_1", true) 
            },
            new ChecklistItem 
            { 
                Title = "Non-perishable Food", 
                Description = "Canned goods, protein bars, dry snacks.", 
                Category = "Food & Water", 
                IconEmoji = "🥫",
                IconBg = "#FEF3C7",
                IsCompleted = Preferences.Get("item_2", true) 
            },
            new ChecklistItem 
            { 
                Title = "First Aid Kit", 
                Description = "Bandages, antiseptics, gauze, tape, tweezers.", 
                Category = "Medical & Safety", 
                IconEmoji = "🩹",
                IconBg = "#FEE2E2",
                IsCompleted = Preferences.Get("item_3", true) 
            },
            new ChecklistItem 
            { 
                Title = "Prescription Medications", 
                Description = "7-day essential personal medication supply.", 
                Category = "Medical & Safety", 
                IconEmoji = "💊",
                IconBg = "#F3E8FF",
                IsCompleted = Preferences.Get("item_4", false) 
            },
            new ChecklistItem 
            { 
                Title = "LED Flashlight & Batteries", 
                Description = "Bright flashlight with spare batteries.", 
                Category = "Tools & Power", 
                IconEmoji = "🔦",
                IconBg = "#FEF9C3",
                IsCompleted = Preferences.Get("item_5", true) 
            },
            new ChecklistItem 
            { 
                Title = "Power Bank (20,000 mAh)", 
                Description = "Fully charged power bank for phone charging.", 
                Category = "Tools & Power", 
                IconEmoji = "🔋",
                IconBg = "#EFF6FF",
                IsCompleted = Preferences.Get("item_6", true) 
            },
            new ChecklistItem 
            { 
                Title = "Emergency Signal Whistle", 
                Description = "Loud whistle for rescue signaling.", 
                Category = "Tools & Power", 
                IconEmoji = "📣",
                IconBg = "#FFEDD5",
                IsCompleted = Preferences.Get("item_7", true) 
            },
            new ChecklistItem 
            { 
                Title = "Waterproof Document Folder", 
                Description = "IDs, insurance policies, medical certificates.", 
                Category = "Documents", 
                IconEmoji = "📁",
                IconBg = "#CCFBF1",
                IsCompleted = Preferences.Get("item_8", false) 
            },
            new ChecklistItem 
            { 
                Title = "Emergency Cash & Coins", 
                Description = "Small denominations for power outages.", 
                Category = "Documents", 
                IconEmoji = "💵",
                IconBg = "#DCFCE7",
                IsCompleted = Preferences.Get("item_9", false) 
            },
            new ChecklistItem 
            { 
                Title = "Portable AM/FM Radio", 
                Description = "Solar or battery operated radio for updates.", 
                Category = "Tools & Power", 
                IconEmoji = "📻",
                IconBg = "#F1F5F9",
                IsCompleted = Preferences.Get("item_10", false) 
            }
        };
    }

    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        var items = SelectedCategory == "All" 
            ? _allMasterItems 
            : _allMasterItems.Where(x => x.Category.Equals(SelectedCategory, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredItems.Add(item);
        }
    }

    [RelayCommand]
    private void ToggleItem(ChecklistItem item)
    {
        if (item == null) return;
        item.IsCompleted = !item.IsCompleted;
        
        int index = _allMasterItems.IndexOf(item);
        if (index >= 0)
        {
            Preferences.Set($"item_{index + 1}", item.IsCompleted);
        }

        UpdateProgress();
    }

    private void UpdateProgress()
    {
        int completedCount = _allMasterItems.Count(x => x.IsCompleted);
        int totalCount = _allMasterItems.Count;
        if (totalCount > 0)
        {
            ProgressValue = (double)completedCount / totalCount;
            PercentReady = (int)(ProgressValue * 100);
            PreparedCountText = $"{completedCount} of {totalCount} emergency kit items ready";

            Preferences.Set("PASS_ChecklistScore", PercentReady);
        }
    }

    [RelayCommand]
    private void OpenAddModal()
    {
        NewItemTitle = string.Empty;
        NewItemDescription = string.Empty;
        IsAddModalVisible = true;
    }

    [RelayCommand]
    private void CloseAddModal()
    {
        IsAddModalVisible = false;
    }

    [RelayCommand]
    private void SaveNewItem()
    {
        if (string.IsNullOrWhiteSpace(NewItemTitle)) return;

        var newItem = new ChecklistItem
        {
            Title = NewItemTitle.Trim(),
            Description = string.IsNullOrWhiteSpace(NewItemDescription) ? "Custom emergency item." : NewItemDescription.Trim(),
            Category = NewItemCategory,
            IsCompleted = true
        };

        _allMasterItems.Add(newItem);
        ApplyFilter();
        UpdateProgress();

        IsAddModalVisible = false;
    }

    [RelayCommand]
    private async Task NavigateToEvacuationCentersAsync()
    {
        if (Shell.Current != null)
        {
            await Shell.Current.GoToAsync("Prepare/EvacuationCenterInfo");
        }
    }
}
