namespace RescuAR.App.Views.Profile;

/// <summary>One set of factual app information for registration and profile screens.</summary>
internal static class LegalInformationContent
{
    public static View Create(bool privacy)
    {
        (string Title, string Text)[] sections = privacy ?
        [
            ("Account & resident information", "Registration and profile information, including resident details, health information, emergency contacts and your uploaded avatar, are saved to your account. Profile information is also cached on this device for your account."),
            ("Location & Safety Circles", "Navigation uses device location and sensors. Safety Circle members can see the location and status you share through the circle. Last shared locations may remain in the cloud and local circle cache until updated. The app distinguishes current, last known and unavailable locations."),
            ("Messages & local changes", "Circle messages and membership information use the cloud service. Cached messages and pending messages are stored on this device for your account. Pending changes are shown separately from confirmed cloud changes. A picture marked ‘device only’ has not been uploaded."),
            ("Camera & contact access", "The camera supports AR guidance and taking an avatar photo. Contact access is requested when importing an emergency contact. Imported contact details are saved when you confirm the contact form."),
            ("Device controls", "Manage camera, location and contact permissions in device settings. App settings control popup presentation, alarm sounds, haptics, voice guidance and detailed maps. Removing a local map copy does not delete account data or pending messages."),
            ("Service & data availability", "Account features use the configured cloud service. Offline maps are bundled static context; current advisories and cloud synchronization require connectivity. These descriptions explain the app’s current behavior and do not promise a data-retention period or account-deletion service.")
        ] :
        [
            ("Using RescuAR", "RescuAR supports disaster preparedness and evacuation. Follow official instructions and assess conditions around you before moving. A displayed route or shelter is not proof that access is currently safe or open."),
            ("Maps & advisories", "Bundled roads and detailed map tiles describe static map context. The detailed export covers part of Marikina at zoom levels 13–18 and has no supplied survey date. Live advisories depend on the configured remote service and connectivity."),
            ("Navigation limits", "Route guidance depends on suitable location readings, device tracking and mapped access rules. Poor lighting, signal loss, obstacles and conditions on the ground can limit guidance. The app may suspend guidance while your location or route alignment is being checked."),
            ("Offline use", "Bundled maps and supported offline routing can work without internet. Cloud account changes, circle updates, new advisories and external routing services require connectivity. Installed device voices determine whether speech is available offline."),
            ("Account & circle actions", "Keep your information accurate and protect access to your account. Confirm changes on screen. A pending or device-only change is not confirmation that it reached your account or another circle member."),
            ("Settings", "Alert and guidance preferences apply on this device. Turning off optional popups leaves camera emergency events and the advisory feed active. Sound and vibration also depend on device support and settings.")
        ];
        var stack = new VerticalStackLayout { Padding = new Thickness(20), Spacing = 18 };
        stack.Add(new Label { Text = privacy ? "Privacy information" : "Terms & app use", FontSize = 28,
            FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0F172A") });
        foreach (var section in sections)
        {
            var content = new VerticalStackLayout { Spacing = 8 };
            content.Add(new Label { Text = section.Title, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#0A8491") });
            content.Add(new Label { Text = section.Text, FontSize = 14, TextColor = Color.FromArgb("#334155"), LineHeight = 1.2 });
            stack.Add(new Border { Content = content, Padding = 16, BackgroundColor = Colors.White,
                Stroke = Color.FromArgb("#E2E8F0"), StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 } });
        }
        return stack;
    }
}
