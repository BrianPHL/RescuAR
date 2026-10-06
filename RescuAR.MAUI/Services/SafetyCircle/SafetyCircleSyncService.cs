using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using RescuAR.App.Models;

namespace RescuAR.App.Services.SafetyCircle;

public interface ISafetyCircleRemote
{
    Task<List<SupabaseSafetyCircle>> GetCirclesAsync(string userId);
    Task<SupabaseSafetyCircle> CreateAsync(string userId, string name);
    Task<SupabaseSafetyCircle> JoinAsync(string userId, string code);
    Task<SupabaseSafetyCircle> RenameAsync(string userId, string circleId, string name);
    Task LeaveAsync(string userId, string circleId);
    Task DeleteAsync(string userId, string circleId);
    Task<List<User>> GetMembersAsync(string userId, string circleId);
    Task<List<SupabaseUserLocation>> GetLocationsAsync(string userId, string circleId);
    Task PushLocationAsync(string userId, SupabaseUserLocation location);
    Task<List<SupabaseCircleMessage>> GetMessagesAsync(string userId, string circleId);
    Task<SupabaseCircleMessage> SendAsync(string userId, SupabaseCircleMessage message);
}

public record CircleSnapshot<T>(List<T> Items, bool IsCached);

public class CircleMessageState
{
    public SupabaseCircleMessage Message { get; set; } = new();
    public bool IsPending { get; set; }
    public string Error { get; set; } = string.Empty;
    public string DeliveryText => IsPending ? "Pending · tap Retry" : "Sent";
}

public class CircleAccountState
{
    public List<SupabaseSafetyCircle> Circles { get; set; } = new();
    public string SelectedCircleId { get; set; } = string.Empty;
    public bool TutorialSeen { get; set; }
    public Dictionary<string, string> LocalPhotos { get; set; } = new();
    public Dictionary<string, List<User>> Members { get; set; } = new();
    public Dictionary<string, List<SupabaseUserLocation>> Locations { get; set; } = new();
    public Dictionary<string, List<CircleMessageState>> Messages { get; set; } = new();

    public void RemoveCircle(string id)
    {
        Circles.RemoveAll(c => c.Id == id);
        Members.Remove(id);
        Locations.Remove(id);
        Messages.Remove(id);
        LocalPhotos.Remove(id);
        if (SelectedCircleId == id) SelectedCircleId = Circles.FirstOrDefault()?.Id ?? string.Empty;
    }
}

public interface ICircleStateStore
{
    CircleAccountState Read(string userId);
    void Write(string userId, CircleAccountState state);
}

public sealed class FileCircleStateStore(string directory) : ICircleStateStore
{
    private string GetPath(string userId) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))) + ".json");

    public CircleAccountState Read(string userId)
    {
        var path = GetPath(userId);
        if (!File.Exists(path)) return new();
        // A damaged cache must be reported, especially if it contains unsent messages.
        return JsonConvert.DeserializeObject<CircleAccountState>(File.ReadAllText(path))
            ?? throw new IOException("Safety Circle cache could not be read.");
    }

    public void Write(string userId, CircleAccountState state)
    {
        Directory.CreateDirectory(directory);
        var path = GetPath(userId);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonConvert.SerializeObject(state));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class SafetyCircleSyncService(
    ISafetyCircleRemote remote, ICircleStateStore store, Func<string> currentUser)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    private static bool CanUseCache(Exception ex) => ex is not UnauthorizedAccessException &&
        !(ex is Supabase.Postgrest.Exceptions.PostgrestException api && (int)api.StatusCode is 401 or 403);

    public string GetCurrentUserId()
    {
        var id = currentUser();
        if (string.IsNullOrWhiteSpace(id)) throw new UnauthorizedAccessException("Please sign in again.");
        return id;
    }

    private void CheckAccount(string id)
    {
        if (GetCurrentUserId() != id) throw new UnauthorizedAccessException("Account changed. Reopen Safety Circles.");
    }

    private async Task<T> WithStateAsync<T>(Func<string, CircleAccountState, Task<T>> action)
    {
        var userId = GetCurrentUserId();
        await _gate.WaitAsync();
        try
        {
            CheckAccount(userId);
            return await action(userId, store.Read(userId));
        }
        finally { _gate.Release(); }
    }

    private void Save(string userId, CircleAccountState state)
    {
        CheckAccount(userId);
        store.Write(userId, state);
    }

    private static void RequireCircle(CircleAccountState state, string id)
    {
        if (!state.Circles.Any(c => c.Id == id))
            throw new UnauthorizedAccessException("Refresh and select a joined circle first.");
    }

    public Task<CircleSnapshot<SupabaseSafetyCircle>> GetCirclesAsync() => WithStateAsync(async (user, state) =>
    {
        List<SupabaseSafetyCircle> circles;
        try { circles = await remote.GetCirclesAsync(user); }
        catch (Exception ex) when (CanUseCache(ex))
        {
            CheckAccount(user);
            return new CircleSnapshot<SupabaseSafetyCircle>(state.Circles, true);
        }
        CheckAccount(user);
        // Successful empty results remove revoked/deleted memberships and their private caches.
        foreach (var removed in state.Circles.Where(c => !circles.Any(r => r.Id == c.Id)).ToList())
            state.RemoveCircle(removed.Id);
        state.Circles = circles;
        if (!circles.Any(c => c.Id == state.SelectedCircleId))
            state.SelectedCircleId = circles.FirstOrDefault()?.Id ?? string.Empty;
        Save(user, state);
        return new CircleSnapshot<SupabaseSafetyCircle>(circles, false);
    });

    public Task<string> GetSelectedCircleIdAsync() => WithStateAsync((_, state) => Task.FromResult(state.SelectedCircleId));
    public Task SelectCircleAsync(string id) => WithStateAsync((user, state) =>
    {
        RequireCircle(state, id);
        state.SelectedCircleId = id;
        Save(user, state);
        return Task.FromResult(true);
    });

    public Task<bool> HasSeenTutorialAsync() => WithStateAsync((_, state) => Task.FromResult(state.TutorialSeen));
    public Task MarkTutorialSeenAsync() => WithStateAsync((user, state) =>
    {
        state.TutorialSeen = true;
        Save(user, state);
        return Task.FromResult(true);
    });

    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60) throw new ArgumentException("Use a circle name of 1–60 characters.");
        return name;
    }

    private Task<SupabaseSafetyCircle> ChangeCircleAsync(Func<string, Task<SupabaseSafetyCircle>> change)
        => WithStateAsync(async (user, state) =>
        {
            var circle = await change(user);
            CheckAccount(user);
            state.Circles.RemoveAll(c => c.Id == circle.Id);
            state.Circles.Add(circle);
            state.SelectedCircleId = circle.Id;
            Save(user, state);
            return circle;
        });

    public Task<SupabaseSafetyCircle> CreateAsync(string name) => ChangeCircleAsync(user => remote.CreateAsync(user, ValidateName(name)));
    public Task<SupabaseSafetyCircle> JoinAsync(string code)
    {
        code = code.Trim().ToUpperInvariant();
        if (code.Length != 6 || code.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Enter a 6-character invite code.");
        return ChangeCircleAsync(user => remote.JoinAsync(user, code));
    }

    public Task<SupabaseSafetyCircle> RenameAsync(string id, string name) => WithStateAsync(async (user, state) =>
    {
        RequireCircle(state, id);
        if (state.Circles.First(c => c.Id == id).CreatedBy != user)
            throw new UnauthorizedAccessException("Only the circle creator can rename it.");
        var circle = await remote.RenameAsync(user, id, ValidateName(name));
        CheckAccount(user);
        state.Circles[state.Circles.FindIndex(c => c.Id == id)] = circle;
        Save(user, state);
        return circle;
    });

    public Task RemoveAsync(string id, bool delete) => WithStateAsync(async (user, state) =>
    {
        RequireCircle(state, id);
        bool isOwner = state.Circles.First(c => c.Id == id).CreatedBy == user;
        if (delete && !isOwner) throw new UnauthorizedAccessException("Only the circle creator can delete it.");
        if (!delete && isOwner) throw new InvalidOperationException("The creator must delete the circle instead of leaving it.");
        if (delete) await remote.DeleteAsync(user, id);
        else await remote.LeaveAsync(user, id);
        SaveRemovedCircle(user, state, id);
        return true;
    });

    private void SaveRemovedCircle(string user, CircleAccountState state, string id)
    {
        CheckAccount(user);
        state.RemoveCircle(id);
        Save(user, state);
    }

    public Task<string> GetLocalPhotoAsync(string id) => WithStateAsync((_, state) =>
        Task.FromResult(state.LocalPhotos.GetValueOrDefault(id, string.Empty)));
    public Task SetLocalPhotoAsync(string id, string path) => WithStateAsync((user, state) =>
    {
        RequireCircle(state, id);
        state.LocalPhotos[id] = path;
        Save(user, state);
        return Task.FromResult(true);
    });

    public Task<CircleSnapshot<User>> GetMembersAsync(string id) => WithStateAsync(async (user, state) =>
    {
        RequireCircle(state, id);
        List<User> members;
        try { members = await remote.GetMembersAsync(user, id); }
        catch (Exception ex) when (CanUseCache(ex))
        {
            CheckAccount(user);
            return new CircleSnapshot<User>(state.Members.GetValueOrDefault(id, new()), true);
        }
        CheckAccount(user);
        state.Members[id] = members;
        Save(user, state);
        return new CircleSnapshot<User>(members, false);
    });

    public Task<CircleSnapshot<SupabaseUserLocation>> GetLocationsAsync(string id) => WithStateAsync(async (user, state) =>
    {
        RequireCircle(state, id);
        List<SupabaseUserLocation> locations;
        try { locations = await remote.GetLocationsAsync(user, id); }
        catch (Exception ex) when (CanUseCache(ex))
        {
            CheckAccount(user);
            return new CircleSnapshot<SupabaseUserLocation>(state.Locations.GetValueOrDefault(id, new()), true);
        }
        CheckAccount(user);
        state.Locations[id] = locations;
        Save(user, state);
        return new CircleSnapshot<SupabaseUserLocation>(locations, false);
    });

    public async Task PushLocationAsync(double lat, double lon, string status)
    {
        if (!CircleLocationStatus.ValidCoordinates(lat, lon)) throw new ArgumentException("Location is invalid.");
        var user = GetCurrentUserId();
        await remote.PushLocationAsync(user, new SupabaseUserLocation
        {
            UserId = user, Latitude = lat, Longitude = lon, StatusText = status, LastUpdated = DateTime.UtcNow
        });
        CheckAccount(user);
    }

    public Task<CircleMessageState> SendAsync(string id, string text, string? media, string type,
        string senderName, string avatar) => WithStateAsync(async (user, state) =>
    {
        RequireCircle(state, id);
        if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(media)) throw new ArgumentException("Enter a message.");
        var item = new CircleMessageState
        {
            IsPending = true,
            Message = new SupabaseCircleMessage
            {
                Id = Guid.NewGuid().ToString(), CircleId = id, UserId = user,
                MessageText = text.Trim(), MediaUrl = media ?? string.Empty,
                MediaType = string.IsNullOrWhiteSpace(media) ? "Text" : type,
                SenderName = senderName, SenderAvatarUrl = avatar, CreatedAt = DateTime.UtcNow
            }
        };
        if (!state.Messages.TryGetValue(id, out var messages)) state.Messages[id] = messages = new();
        messages.Add(item);
        Save(user, state); // Durable outbox before any network write.
        await DeliverAsync(user, item);
        Save(user, state);
        return item;
    });

    private async Task DeliverAsync(string user, CircleMessageState item)
    {
        CheckAccount(user);
        if (item.Message.UserId != user) throw new UnauthorizedAccessException("Message belongs to another account.");
        try
        {
            var confirmed = await remote.SendAsync(user, item.Message);
            CheckAccount(user);
            if (confirmed.Id != item.Message.Id || confirmed.CircleId != item.Message.CircleId || confirmed.UserId != user)
                throw new InvalidOperationException("Cloud did not confirm this message.");
            item.Message = confirmed;
            item.IsPending = false;
            item.Error = string.Empty;
        }
        catch (Exception ex) when (CanUseCache(ex))
        {
            CheckAccount(user);
            item.Error = "Message has not been confirmed by the cloud.";
        }
    }

    public Task<CircleSnapshot<CircleMessageState>> GetMessagesAsync(string id, bool retryPending = false)
        => WithStateAsync(async (user, state) =>
        {
            RequireCircle(state, id);
            if (!state.Messages.TryGetValue(id, out var messages)) state.Messages[id] = messages = new();
            List<SupabaseCircleMessage> remoteMessages;
            try { remoteMessages = await remote.GetMessagesAsync(user, id); }
            catch (Exception ex) when (CanUseCache(ex))
            {
                CheckAccount(user);
                return new CircleSnapshot<CircleMessageState>(messages.OrderBy(m => m.Message.CreatedAt).ToList(), true);
            }
            CheckAccount(user);
            // The remote history is authoritative. Preserve only our unconfirmed outbox entries.
            var merged = remoteMessages.Where(m => m.CircleId == id)
                .GroupBy(m => m.Id).Select(g => new CircleMessageState { Message = g.Last() }).ToList();
            merged.AddRange(messages.Where(m => m.IsPending && m.Message.UserId == user &&
                !merged.Any(r => r.Message.Id == m.Message.Id)));
            state.Messages[id] = merged;
            if (retryPending)
                foreach (var pending in merged.Where(m => m.IsPending).ToList())
                {
                    await DeliverAsync(user, pending);
                    Save(user, state);
                }
            Save(user, state);
            return new CircleSnapshot<CircleMessageState>(merged.OrderBy(m => m.Message.CreatedAt).ToList(), false);
        });
}

public static class CircleLocationStatus
{
    public static bool ValidCoordinates(double lat, double lon) => double.IsFinite(lat) && double.IsFinite(lon)
        && lat is >= -85 and <= 85 && lon is >= -180 and <= 180;

    public static bool HasLocation(SupabaseUserLocation? location) => location?.Latitude is double lat &&
        location.Longitude is double lon && ValidCoordinates(lat, lon) && location.LastUpdated is DateTime updated && updated != default;

    public static bool IsFresh(SupabaseUserLocation? location, DateTime now) => HasLocation(location)
        && now.ToUniversalTime() - location!.LastUpdated!.Value.ToUniversalTime() is var age
        && age >= TimeSpan.FromMinutes(-1) && age <= TimeSpan.FromMinutes(2);

    public static string Describe(SupabaseUserLocation? location, DateTime now, bool cached)
    {
        if (!HasLocation(location)) return "Location unavailable";
        if (!cached && IsFresh(location, now)) return "Location updated just now";
        return $"Last known location · {location!.LastUpdated!.Value.ToLocalTime():MMM d, h:mm tt}";
    }

    public static string BatteryText(SupabaseUserLocation? location, DateTime now, bool cached)
    {
        if (cached || !IsFresh(location, now)) return string.Empty;
        var value = location!.StatusText?.Split('|').ElementAtOrDefault(1)?.Replace("⚡", "").Trim().TrimEnd('%');
        return int.TryParse(value, out int percent) && percent is >= 0 and <= 100 ? $"{percent}%" : string.Empty;
    }
}
