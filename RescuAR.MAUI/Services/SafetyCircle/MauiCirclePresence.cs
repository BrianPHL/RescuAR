using RescuAR.App.Models;
using RescuAR.Services;

namespace RescuAR.App.Services.SafetyCircle;

internal sealed class MauiCircleDeviceReader : ICircleDeviceReader
{
    public async Task<CircleDeviceReading?> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>() != PermissionStatus.Granted)
            throw new UnauthorizedAccessException("Location permission is required for sharing.");
        token.ThrowIfCancellationRequested();
        var location = await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(4)), token);
        token.ThrowIfCancellationRequested();
        if (location is null) return null;
        int? percent = null; bool charging = false;
        try
        {
            double level = Battery.Default.ChargeLevel;
            if (double.IsFinite(level) && level is >= 0 and <= 1) percent = (int)Math.Round(level * 100);
            charging = Battery.Default.State is BatteryState.Charging or BatteryState.Full;
        }
        catch (FeatureNotSupportedException) { }
        return new(location.Latitude, location.Longitude, location.Timestamp, location.Accuracy, location.IsFromMockProvider, percent, charging);
    }
}

internal sealed class SupabaseCirclePresenceRemote(MauiCircleMembershipContext context) : ICirclePresenceRemote
{
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    private void Require(CircleMembershipScope scope, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        context.Require(scope);
        if (!context.IsOnline) throw new CircleMembershipOfflineException("Connect to check member positions.");
    }
    private async Task<Supabase.Client> Client(CircleMembershipScope scope, CancellationToken token)
    {
        Require(scope, token);
        var client = await SupabaseService.Instance.GetClientAsync().WaitAsync(token) ?? throw new InvalidOperationException("Please sign in again.");
        Require(scope, token);
        return client;
    }
    public async Task<CirclePresenceData> ReadAsync(CircleMembershipScope scope, string circleId, CancellationToken token)
    {
        var client = await Client(scope, token);
        var membership = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == circleId).Get(token);
        Require(scope, token);
        var ids = membership.Models.Select(member => member.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
        if (!ids.Contains(scope.UserId)) throw new UnauthorizedAccessException("Circle membership is no longer confirmed. Refresh your Circles.");
        var people = new Dictionary<string, CirclePerson>();
        string notice = "";
        try
        {
            var profiles = await client.From<User>().Select("id,username,first_name,last_name,phone_number,avatar_url")
                .Filter("id", Supabase.Postgrest.Constants.Operator.In, ids).Get(token);
            Require(scope, token);
            foreach (var profile in profiles.Models.Where(profile => ids.Contains(profile.Id)))
            {
                string name = $"{profile.FirstName} {profile.LastName}".Trim();
                if (name.Length == 0) name = profile.Username?.Trim() ?? "";
                people[profile.Id] = new(profile.Id, name.Length > 0 ? name : "Member name unavailable", profile.PhoneNumber ?? "", CirclePresencePolicy.SafeAvatar(profile.AvatarUrl));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { Require(scope, token); notice = "Some member profile details could not be loaded."; }
        var missing = ids.Where(id => !people.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            try
            {
                var profiles = await client.From<SupabaseProfile>().Select("id,first_name,last_name")
                    .Filter("id", Supabase.Postgrest.Constants.Operator.In, missing).Get(token);
                Require(scope, token);
                foreach (var profile in profiles.Models.Where(profile => missing.Contains(profile.Id)))
                {
                    string name = $"{profile.FirstName} {profile.LastName}".Trim();
                    people[profile.Id] = new(profile.Id, name.Length > 0 ? name : "Member name unavailable", "", "");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { Require(scope, token); notice = "Some member profile details could not be loaded."; }
        }
        var locations = await client.From<SupabaseUserLocation>().Filter("user_id", Supabase.Postgrest.Constants.Operator.In, ids).Get(token);
        Require(scope, token);
        var positions = locations.Models.Where(location => ids.Contains(location.UserId)).Select(location => new CirclePosition(location.UserId,
            location.Latitude, location.Longitude, new DateTimeOffset(Utc(location.LastUpdated)), location.StatusText ?? "")).ToList();
        return new(ids.Select(id => people.GetValueOrDefault(id) ?? new CirclePerson(id, "Member name unavailable", "", "")).ToList(), positions, notice);
    }
    public async Task UploadAsync(CircleMembershipScope scope, string circleId, CircleDeviceReading reading, CancellationToken token)
    {
        var client = await Client(scope, token);
        var membership = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == circleId).Where(member => member.UserId == scope.UserId).Get(token);
        Require(scope, token);
        if (!membership.Models.Any()) throw new UnauthorizedAccessException("Circle membership is no longer confirmed.");
        if (!CirclePresencePolicy.CanShare(reading, DateTimeOffset.UtcNow)) throw new InvalidOperationException("A recent, accurate position is required.");
        string battery = reading.BatteryPercent is >= 0 and <= 100 ? reading.BatteryPercent + "%" + (reading.IsCharging ? "⚡" : "") : "Unknown";
        var location = new SupabaseUserLocation { UserId = scope.UserId, Latitude = reading.Latitude, Longitude = reading.Longitude,
            StatusText = "Shared|" + battery, LastUpdated = reading.RecordedAt.UtcDateTime };
        Require(scope, token);
        var response = await client.From<SupabaseUserLocation>().Upsert(location, null, token);
        Require(scope, token);
        if (!response.Models.Any(saved => saved.UserId == scope.UserId && Math.Abs(saved.Latitude - reading.Latitude) < 0.0000001
            && Math.Abs(saved.Longitude - reading.Longitude) < 0.0000001 && Math.Abs((Utc(saved.LastUpdated) - reading.RecordedAt.UtcDateTime).TotalSeconds) < 1))
            throw new InvalidOperationException("The location upload was not confirmed.");
    }
}
