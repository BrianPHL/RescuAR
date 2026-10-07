using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RescuAR.App.ViewModels.Profile;

public partial class HelpCenterViewModel : ObservableObject
{
    private readonly HelpFaqItem[] topics =
    [
        new("Can I use the map offline?", "Basic roads and supported offline routing are bundled. Turn on Detailed Marikina map in App Settings for raster tiles, buildings, parks and water. These are static context. New advisories and cloud updates need internet."),
        new("Why is guidance waiting for my location?", "The app checks location freshness, accuracy and route alignment before giving movement cues. Check location permission and move to an area with a clearer GPS signal. Use the map’s Retry button after restoring permission."),
        new("How do voice and vibration work?", "App Settings and the camera voice button share the same voice preference. Speech and short turn pulses require confirmed route guidance. Install an offline device voice for speech without internet. Sound volume and haptic support depend on your device."),
        new("What does pending mean in my Safety Circle?", "A pending message has been saved locally and has not been confirmed by the cloud. Reconnect and retry. Circle membership changes require cloud confirmation. Use the circle selector to switch the circle shown in chat."),
        new("Why is a circle member’s location unavailable?", "The member may not have shared a valid location or permission may be unavailable. Last known locations are labeled with their timestamp. An unavailable location is never replaced by a guessed point."),
        new("Where can I change permissions or recover my account?", "Use App Settings → Open device settings for permissions. Use Forgot Password on the login screen to request a recovery code. Account changes need a connection to the configured service.")
    ];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FilteredTopics))] [NotifyPropertyChangedFor(nameof(HasNoMatches))]
    private string searchText = string.Empty;
    public IReadOnlyList<HelpFaqItem> FilteredTopics => topics.Where(topic => string.IsNullOrWhiteSpace(SearchText) ||
        (topic.Question + " " + topic.Answer).Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
    public bool HasNoMatches => FilteredTopics.Count == 0;
    [RelayCommand] private async Task OpenHotlinesAsync()
    {
        if (Shell.Current is not null) await Shell.Current.GoToAsync("HotlineDirectoryPage");
    }
}

public sealed partial class HelpFaqItem(string question, string answer) : ObservableObject
{
    public string Question => question;
    public string Answer => answer;
    [ObservableProperty] private bool isExpanded;
    [RelayCommand] private void Toggle() => IsExpanded = !IsExpanded;
}
