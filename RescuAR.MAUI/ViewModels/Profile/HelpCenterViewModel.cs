using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RescuAR.App.ViewModels.Profile;

public partial class HelpFaqItem : ObservableObject
{
    public string Question { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ArrowIcon))] private bool isExpanded;
    public string ArrowIcon => IsExpanded ? "▲" : "▼";
    [RelayCommand] private void ToggleExpand() => IsExpanded = !IsExpanded;
}

public partial class HelpCenterViewModel : ObservableObject
{
    private readonly HelpFaqItem[] allFaqs =
    [
        Faq("How does AR evacuation guidance work?", "The camera view combines location, motion readings, mapped routes and camera tracking. Guidance can pause while route identity or road alignment is checked. Unsupported capabilities, poor lighting and uncertain tracking can prevent a path from being displayed. Follow official instructions and assess conditions around you.", "Navigation"),
        Faq("What can I use without internet?", "Available packaged route data and saved local information can support offline use within their coverage. Offline map detail depends on the data on the device. New sign-ins, changed account fields, new advisories and cloud circle/message updates need connectivity. Remembered offline account access requires an earlier successful online sign-in with Remember Me enabled.", "Offline Use"),
        Faq("What does a Safety Circle location or message mean?", "Circle features use the cloud service to exchange membership, shared positions and messages. A displayed position can be last known, and a message stored on this device is not proof another member received it. Check update times and do not rely on a cached display as a live status.", "Safety Circle"),
        Faq("What should I do if a mapped route is flooded or blocked?", "Do not enter an unsafe or impassable section because the map shows a route. Follow official directions and reassess available destinations or routes. A displayed route does not prove that present conditions are safe or that a shelter is open.", "Navigation"),
        Faq("How do I submit a community report?", "Open Reports and use its report form to provide the category, location and details, with a photo when appropriate. Sending a report requires the configured service and connectivity. A submission does not establish immediate review or responder notification.", "Community Reports"),
        Faq("How do imported emergency contacts become shortcuts?", "Open Profile > Emergency Contacts. Import fills the form; Save confirms changed account contact fields and synchronizes matching call shortcuts. Both a name and a phone number are needed for a shortcut. Relationship labels stay on this device. A failed account save does not publish the draft contacts.", "Emergency Contacts"),
        Faq("How do I find emergency phone numbers?", "Open the Hotlines directory using the button below or the existing Prepare/Hotlines entry. Choose the appropriate listed contact. This Help page opens the directory; it does not place a call automatically.", "Emergency"),
        Faq("How do Remember Me and profile-photo saves work?", "Checked Remember Me stores a session for restart; unchecked requires sign-in after restart. A profile photo can be saved on this device before its cloud update completes. Check its status and use Retry when needed. Signing out clears the active identity but does not erase every local cache or cloud record.", "Account")
    ];
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private string statusMessage = string.Empty;
    public ObservableCollection<HelpFaqItem> FilteredFaqs { get; } = new();
    public HelpCenterViewModel() => FilterFaqs();
    partial void OnSearchTextChanged(string value) => FilterFaqs();
    private void FilterFaqs()
    {
        string query = SearchText?.Trim() ?? string.Empty;
        FilteredFaqs.Clear();
        foreach (var item in allFaqs.Where(item => query.Length == 0 || item.Question.Contains(query, StringComparison.OrdinalIgnoreCase)
            || item.Answer.Contains(query, StringComparison.OrdinalIgnoreCase) || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)))
            FilteredFaqs.Add(item);
        StatusMessage = FilteredFaqs.Count == 0 ? "No matching topics. Try another search." : string.Empty;
    }
    private static HelpFaqItem Faq(string question, string answer, string category) => new() { Question = question, Answer = answer, Category = category };
    [RelayCommand]
    private async Task OpenHotlines()
    {
        if (Shell.Current is not { } shell) return;
        try { await shell.GoToAsync("Prepare/HotlineDirectory"); }
        catch { StatusMessage = "The Hotlines directory could not be opened. Use its existing entry from Prepare or the dashboard."; }
    }
    [RelayCommand] private async Task Back() { if (Shell.Current != null) await Shell.Current.GoToAsync(".."); }
}
