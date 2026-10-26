using RescuAR.App.Models;

namespace RescuAR.App.Services.Profile;

// Shared by registration and profile; feature descriptions carry no retention or delivery guarantee.
internal static class AppInformationContent
{
    public static IReadOnlyList<InformationSection> Terms { get; } = new[]
    {
        Section("1", "Using RescuAR", "RescuAR supports disaster preparedness and evacuation planning. Follow official instructions and assess conditions around you before moving.",
            "A displayed route or evacuation center does not establish that access is currently safe or open.",
            "Keep your account, address and emergency-contact information accurate."),
        Section("2", "Maps, routes and advisories", "Maps and routing use available mapped roads, access rules and location readings. Packaged or cached data may be incomplete or out of date.",
            "Cloud advisories, new reports and remote updates require connectivity.",
            "A report submission does not establish that responders or other residents have received it."),
        Section("3", "AR guidance limits", "AR guidance depends on camera tracking, device sensors, suitable location readings and alignment with the mapped route.",
            "Guidance may pause while tracking, route identity, road alignment or location confidence is checked.",
            "Poor lighting, obstructions, unsupported device capabilities and changing conditions can limit guidance.",
            "Stay aware of your surroundings when using the camera view."),
        Section("4", "Offline use", "Available packaged route data and saved local information support some offline use within their coverage. Map detail depends on the data available on the device.",
            "New sign-ins, changed account contacts/profile fields, new advisories and cloud synchronization require connectivity.",
            "Remembered offline account access requires an earlier successful online sign-in with Remember Me enabled.",
            "An offline or locally cached display is not evidence of a current remote update."),
        Section("5", "Saving and sharing", "Check the result of each save. A locally saved photo or message is not confirmation that it reached your account or another person.",
            "Importing a phone contact fills the form. Save confirms account contact changes and updates matching local call shortcuts.",
            "Circle locations and messages depend on the configured cloud service and connectivity; a last-known location may be stale.",
            "Choose contacts, photos, report details and messages with their recipients in mind."),
        Section("6", "Device controls", "Camera, location, contact and notification access are managed through your device permissions. Available capabilities and permissions can affect app features.",
            "System Information shows installed-app details and current permission or sensor availability.",
            "Signing out does not delete the cloud account or erase every local file and cache.")
    };

    public static IReadOnlyList<InformationSection> Privacy { get; } = new[]
    {
        Section("1", "Account and resident information", "Account and profile features use your sign-in identity and the information you submit, including names, email, phone, address, birthday, optional health details, emergency contacts and selected photos.",
            "Profile and contact changes are sent to the configured account service after you save them.",
            "Contact relationship labels are stored on this device for the signed-in account.",
            "Profile details, contact shortcuts and newly selected profile photos also have local account-specific storage."),
        Section("2", "Saved sign-in sessions", "When Remember Me is enabled, the app stores a sign-in session using device secure storage so the same account can be restored.",
            "With Remember Me unchecked, the app requires sign-in after restart.",
            "Session tokens use secure storage. Other profile, message and media caches are separate local storage.",
            "A rejected session requires sign-in; temporary connection problems can leave saved local features available."),
        Section("3", "Location and Safety Circles", "Navigation uses device location and motion readings. Circle features use the configured cloud service for membership, shared positions, status and messages.",
            "Shared coordinates and update times can remain in the cloud until changed or removed through available service controls.",
            "A last-known or cached position is not necessarily the person's current location.",
            "This information does not promise that shared location data is never stored or automatically removed when you leave a screen."),
        Section("4", "Camera, photos and contact access", "Camera access supports AR guidance and taking a profile photo. Local road observations are processed on the device. Photos you select for upload or reports can be sent to the configured cloud service.",
            "The gallery action uses the device's system photo picker.",
            "Contact permission is requested when you choose to import a phone contact. The selected details fill the form; they are saved to your account after Save.",
            "A photo marked as device-only has not completed its account update."),
        Section("5", "Reports, messages and local copies", "Report content, selected media and circle messages use the configured services when submitted or synchronized. Local message and media copies may remain on the device.",
            "A local copy or a successful local save alone does not prove delivery to another person.",
            "Signing out clears the active sign-in identity but can retain account caches, media and archived preferences.",
            "Older local archives are kept aside and are not automatically restored into a new sign-in."),
        Section("6", "Permissions and availability", "You can review camera, location, contact and notification permissions in device settings. Service availability and connectivity affect remote account, advisory, circle and report features.",
            "These descriptions explain the app's implemented data handling; they do not establish a fixed retention period or an account-deletion service.",
            "Do not assume that leaving a screen or changing a permission deletes information already stored in a cloud service or local cache.")
    };

    private static InformationSection Section(string number, string title, string description, params string[] bullets) =>
        new(number, title, description, Array.AsReadOnly(bullets));
}
