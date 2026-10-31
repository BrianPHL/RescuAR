using RescuAR.App.Services.SafetyCircle;

namespace RescuAR.App.ViewModels.Map;

public partial class SafetyCircleViewModel
{
    // The retained membership-management screen consumes the current presence service.
    // Athena's map continues to construct its own original CircleMember rows.
    internal static CircleMember Row(CirclePresence presence, string self, bool cached)
    {
        string name = presence.Person.Name + (presence.Person.UserId == self ? " (You)" : "");
        string initials = string.Concat(presence.Person.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Take(2).Select(word => char.ToUpperInvariant(word[0])));
        return new CircleMember
        {
            Id = presence.Person.UserId,
            Name = name,
            PhoneNumber = presence.Person.Phone,
            Initials = initials.Length > 0 ? initials : "?",
            LocationSubtitle = presence.Position is { } position
                ? $"Last shared: {position.Latitude:F5}, {position.Longitude:F5}" : "Location unavailable",
            SinceTimeText = presence.Position is { } at
                ? at.RecordedAt.ToLocalTime().ToString("MMM d, HH:mm") +
                    (cached ? " · cached" : presence.IsRecent ? " · recent" : " · last known")
                : "No dated position",
            BatteryText = presence.BatteryPercent is { } percent
                ? $"{percent}%{(presence.IsCharging ? " ⚡" : "")}" : "Unknown",
            BatteryColor = presence.BatteryPercent is null || !presence.IsRecent ? Colors.Gray
                : presence.IsCharging ? Colors.RoyalBlue : presence.BatteryPercent <= 20 ? Colors.Crimson : Colors.Teal,
            ColorTheme = cached || !presence.IsRecent ? Colors.Gray
                : Color.FromArgb(new[] { "#0A8491", "#931492", "#2563EB", "#16A34A" }
                    [(uint)presence.Person.UserId.GetHashCode() % 4])
        };
    }
}
