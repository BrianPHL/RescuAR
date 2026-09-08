using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.App.Services.Reports;

namespace RescuAR.App.ViewModels.Dashboard
{
    public partial class CategoryChipItem : ObservableObject
    {
        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private bool isSelected;

        public string BgColor => IsSelected ? "#0A8491" : "#FFFFFF";
        public string TextColor => IsSelected ? "#FFFFFF" : "#000000";
        public string BorderColor => IsSelected ? "#0A8491" : "#E5E5EA";

        partial void OnIsSelectedChanged(bool value)
        {
            OnPropertyChanged(nameof(BgColor));
            OnPropertyChanged(nameof(TextColor));
            OnPropertyChanged(nameof(BorderColor));
        }
    }

    public partial class AdvisoryFeedViewModel : ObservableObject
    {
        private readonly AdvisoryService _advisoryService;

        [ObservableProperty]
        private string selectedCategory = "All";

        [ObservableProperty]
        private string selectedStatus = "All";

        [ObservableProperty]
        private string lastUpdatedText = "Last updated just now";

        public ObservableCollection<CategoryChipItem> CategoryChips { get; } = new();
        public ObservableCollection<CategoryChipItem> StatusChips { get; } = new();
        public ObservableCollection<DisasterAdvisory> DisplayedAdvisories { get; } = new();

        public AdvisoryFeedViewModel() : this(new AdvisoryService())
        {
        }

        public AdvisoryFeedViewModel(AdvisoryService advisoryService)
        {
            _advisoryService = advisoryService;

            InitializeFilterChips();
            _ = LoadAdvisoriesAsync();
        }

        private void InitializeFilterChips()
        {
            CategoryChips.Clear();
            var categories = new[] { "All", "Earthquake", "Typhoon", "Flood", "Advisory" };
            foreach (var cat in categories)
            {
                CategoryChips.Add(new CategoryChipItem { Name = cat, IsSelected = cat == "All" });
            }

            StatusChips.Clear();
            var statuses = new[] { "All", "Active", "Cleared" };
            foreach (var stat in statuses)
            {
                StatusChips.Add(new CategoryChipItem { Name = stat, IsSelected = stat == "All" });
            }
        }

        [RelayCommand]
        public async Task LoadAdvisoriesAsync()
        {
            var items = await _advisoryService.GetAdvisoriesAsync();
            DisplayedAdvisories.Clear();
            foreach (var item in items)
            {
                DisplayedAdvisories.Add(item);
            }
        }

        [RelayCommand]
        private async Task SelectCategoryAsync(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName)) return;

            SelectedCategory = categoryName;
            foreach (var chip in CategoryChips)
            {
                chip.IsSelected = chip.Name.Equals(categoryName, StringComparison.OrdinalIgnoreCase);
            }
            await LoadAdvisoriesAsync();
        }

        [RelayCommand]
        private async Task SelectStatusAsync(string statusName)
        {
            if (string.IsNullOrWhiteSpace(statusName)) return;

            SelectedStatus = statusName;
            foreach (var chip in StatusChips)
            {
                chip.IsSelected = chip.Name.Equals(statusName, StringComparison.OrdinalIgnoreCase);
            }
            await LoadAdvisoriesAsync();
        }

        [RelayCommand]
        private async Task GoBackAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.GoToAsync("..");
            }
        }

        [RelayCommand]
        private async Task ViewDetailsAsync(DisasterAdvisory advisory)
        {
            if (advisory == null || Shell.Current == null) return;

            await Shell.Current.DisplayAlert(
                advisory.Title,
                $"Affected Area: {advisory.DisplayAffectedArea}\nMessage: {advisory.DisplayMessage}\nAlert Level: {advisory.DisplayAlertLevel}",
                "OK");
        }

        [RelayCommand]
        private async Task OpenNotificationsAsync()
        {
            if (Shell.Current != null)
            {
                await Shell.Current.DisplayAlert(
                    "Advisory Notifications",
                    "Push notifications for real-time disaster advisories are ACTIVE.",
                    "OK");
            }
        }
    }
}
