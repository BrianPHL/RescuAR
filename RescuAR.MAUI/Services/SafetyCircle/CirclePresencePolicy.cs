namespace RescuAR.App.Services.SafetyCircle;

public sealed record CirclePerson(string UserId, string Name, string Phone, string AvatarUrl);
public sealed record CirclePosition(string UserId, double Latitude, double Longitude, DateTimeOffset RecordedAt, string Status);
public sealed record CirclePresenceData(IReadOnlyList<CirclePerson> People, IReadOnlyList<CirclePosition> Positions, string Notice = "");
public sealed record CircleDeviceReading(double Latitude, double Longitude, DateTimeOffset RecordedAt, double? Accuracy, bool IsMock, int? BatteryPercent, bool IsCharging);
public sealed record CirclePresence(CirclePerson Person, CirclePosition? Position, bool IsRecent, int? BatteryPercent, bool IsCharging);

public static class CirclePresencePolicy
{
    public static bool Coordinates(double latitude, double longitude) => double.IsFinite(latitude) && double.IsFinite(longitude)
        && Math.Abs(latitude) <= 85.05112878 && Math.Abs(longitude) <= 180 && (latitude != 0 || longitude != 0);

    public static bool Dated(DateTimeOffset recordedAt, DateTimeOffset now) => recordedAt > DateTimeOffset.UnixEpoch && recordedAt <= now.AddSeconds(30);

    public static bool CanShare(CircleDeviceReading? reading, DateTimeOffset now) => reading is not null && !reading.IsMock
        && Coordinates(reading.Latitude, reading.Longitude) && Dated(reading.RecordedAt, now)
        && now - reading.RecordedAt <= TimeSpan.FromSeconds(30)
        && reading.Accuracy is > 0 and <= 100;

    // A local marker is separate from consent to upload. Reject stale/coarse fixes as current.
    public static bool CanDisplayLocal(CircleDeviceReading? reading, DateTimeOffset now) => CanShare(reading, now)
        && reading!.RecordedAt <= now.AddSeconds(5) && now - reading.RecordedAt <= TimeSpan.FromSeconds(15)
        && reading.Accuracy <= 50;

    public static CirclePresence Project(CirclePerson person, CirclePosition? position, DateTimeOffset now)
    {
        if (position is not null && (position.UserId != person.UserId || !Coordinates(position.Latitude, position.Longitude) || !Dated(position.RecordedAt, now)))
            position = null;
        bool recent = position is not null && now - position.RecordedAt <= TimeSpan.FromMinutes(2);
        int? percent = null; bool charging = false;
        if (position is not null)
        {
            var parts = (position.Status ?? "").Split('|');
            if (parts.Length == 2)
            {
                string value = parts[1].Trim();
                charging = value.Contains('⚡');
                value = value.Replace("⚡", "").Trim();
                if (value.EndsWith('%') && int.TryParse(value[..^1], out int parsed) && parsed is >= 0 and <= 100) percent = parsed;
                if (!percent.HasValue) charging = false;
            }
        }
        return new(person, position, recent, percent, charging);
    }

    public static string SafeAvatar(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : "";
}
