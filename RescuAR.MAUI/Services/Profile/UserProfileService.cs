using System.Linq.Expressions;
using Newtonsoft.Json;
using RescuAR.App.Models;
using RescuAR.Services;
using ProfileUser = RescuAR.App.Models.User;

namespace RescuAR.App.Services.Profile;

public sealed class UserProfileService
{
    public static UserProfileService Instance { get; set; } = new();
    private readonly SemaphoreSlim _writes = new(1, 1);

    public async Task<Supabase.Client> RequireClientAsync()
    {
        var client = await SupabaseService.Instance.GetClientAsync()
            ?? throw new InvalidOperationException("Configure Supabase before signing in.");
        if (string.IsNullOrWhiteSpace(client.Auth.CurrentSession?.AccessToken) ||
            string.IsNullOrWhiteSpace(client.Auth.CurrentUser?.Id))
            throw new InvalidOperationException("Please sign in again.");
        return client;
    }

    public string UserId => SupabaseService.Instance.Client?.Auth.CurrentUser?.Id
        ?? throw new InvalidOperationException("Please sign in again.");
    public string CacheKey(string name) => $"Profile:{UserId}:{name}";
    public string GetLocal(string name) => Preferences.Default.Get(CacheKey(name), string.Empty);
    public void SetLocal(string name, string value) => Preferences.Default.Set(CacheKey(name), value);

    private static readonly string[] IdentityKeys = {
        "CurrentUserId", "UserEmail", "UserFirstName", "UserMiddleName", "UserLastName",
        "UserPhoneNumber", "UserContactNumber", "UserAddress", "UserHouseNumber", "UserStreetName",
        "UserBarangay", "UserBirthday", "UserAvatarUrl", "UserProfilePicPath",
        "EmergencyContact1Name", "EmergencyContact1Phone", "EmergencyContact1Relationship",
        "EmergencyContact2Name", "EmergencyContact2Phone", "EmergencyContact2Relationship",
        "CustomQuickActionsList_v18"
    };

    public static void ClearIdentity()
    {
        foreach (var key in IdentityKeys) Preferences.Default.Remove(key);
        Preferences.Default.Set("IsLoggedIn", false);
    }

    public void ActivateIdentity()
    {
        var auth = SupabaseService.Instance.Client?.Auth.CurrentUser;
        if (auth == null || string.IsNullOrWhiteSpace(auth.Id)) throw new InvalidOperationException("Please sign in again.");
        if (Preferences.Default.Get("CurrentUserId", string.Empty) != auth.Id) ClearIdentity();
        Preferences.Default.Set("CurrentUserId", auth.Id);
        var legacyActions = Preferences.Default.Get("CustomQuickActionsList_v18", string.Empty);
        if (string.IsNullOrEmpty(GetLocal("QuickActions")) && !string.IsNullOrEmpty(legacyActions))
            SetLocal("QuickActions", legacyActions);
        Preferences.Default.Remove("CustomQuickActionsList_v18");
        Preferences.Default.Set("UserEmail", auth.Email ?? string.Empty);
        if (auth.UserMetadata?.TryGetValue("birthday", out var birthday) == true)
            SetLocal("Birthday", birthday?.ToString() ?? string.Empty);
        Preferences.Default.Set("IsLoggedIn", true);
    }

    private ProfileUser FromMetadata()
    {
        var auth = SupabaseService.Instance.Client!.Auth.CurrentUser!;
        string Get(string key) => auth.UserMetadata?.TryGetValue(key, out var value) == true ? value?.ToString() ?? "" : "";
        var first = Get("first_name");
        var last = Get("last_name");
        if (string.IsNullOrWhiteSpace(first)) first = Get("given_name");
        if (string.IsNullOrWhiteSpace(last)) last = Get("family_name");
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last))
        {
            var full = Get("full_name");
            if (string.IsNullOrWhiteSpace(full)) full = Get("name");
            var parts = full.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            first = parts.FirstOrDefault() ?? "";
            last = parts.Length > 1 ? parts[1] : "";
        }
        return new ProfileUser { Id = auth.Id!, Email = auth.Email ?? "", FirstName = first,
            MiddleName = Get("middle_name"), LastName = last,
            PhoneNumber = Get("phone"), Address = Get("address"), AvatarUrl = Get("avatar_url") };
    }

    public async Task<ProfileUser> LoadAsync()
    {
        var client = await RequireClientAsync();
        var id = UserId;
        try
        {
            // Get() returns an empty list for a missing row; permission/network errors must propagate.
            var response = await client.From<ProfileUser>().Where(x => x.Id == id).Get();
            var user = response.Models.FirstOrDefault() ?? FromMetadata();
            user.Email = client.Auth.CurrentUser?.Email ?? user.Email;
            EnsureSameAccount(id);
            Cache(user);
            return user;
        }
        catch
        {
            EnsureSameAccount(id);
            var cached = JsonConvert.DeserializeObject<ProfileUser>(GetLocal("User"));
            if (cached != null && cached.Id == id) return cached;
            throw;
        }
    }

    public async Task<ProfileUser> PatchAsync(params (Expression<Func<ProfileUser, object>> Field, object Value)[] fields)
    {
        await _writes.WaitAsync();
        try
        {
            var client = await RequireClientAsync();
            var id = UserId;
            var existing = await client.From<ProfileUser>().Where(x => x.Id == id).Get();
            EnsureSameAccount(id);
            if (existing.Models.Count == 0)
            {
                // Only insert after a successful empty read, never after a failed read.
                // Ignore a racing insert; it must not overwrite the other writer's fields.
                await client.From<ProfileUser>().Upsert(FromMetadata(), new Supabase.Postgrest.QueryOptions { DuplicateResolution = Supabase.Postgrest.QueryOptions.DuplicateResolutionType.IgnoreDuplicates });
            }
            var query = client.From<ProfileUser>().Where(x => x.Id == id);
            foreach (var field in fields) query = query.Set(field.Field, field.Value);
            var updated = await query.Update();
            EnsureSameAccount(id);
            var user = updated.Models.FirstOrDefault()
                ?? throw new InvalidOperationException("No profile was updated. Check your account permissions.");
            Cache(user);
            return user;
        }
        finally { _writes.Release(); }
    }

    private void EnsureSameAccount(string id)
    {
        if (id != UserId) throw new InvalidOperationException("Your account changed. Please reopen this screen.");
    }

    private void Cache(ProfileUser user)
    {
        SetLocal("User", JsonConvert.SerializeObject(user));
        Preferences.Default.Set("UserFirstName", user.FirstName);
        Preferences.Default.Set("UserMiddleName", user.MiddleName);
        Preferences.Default.Set("UserLastName", user.LastName);
        Preferences.Default.Set("UserAddress", user.Address);
        Preferences.Default.Set("UserPhoneNumber", user.PhoneNumber);
    }

    public async Task SaveDraftAsync(ResidentProfileDraft draft)
    {
        var client = await RequireClientAsync();
        if (!string.Equals(client.Auth.CurrentUser?.Email, draft.Email, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The registration details belong to a different account.");
        // Birthday uses auth metadata; no unverified database columns are introduced.
        await PatchAsync((x => x.FirstName, draft.FirstName), (x => x.MiddleName, draft.MiddleName),
            (x => x.LastName, draft.LastName), (x => x.PhoneNumber, draft.PhoneNumber), (x => x.Address, draft.Address));
        SetLocal("Birthday", draft.Birthday.ToString("yyyy-MM-dd"));
    }

    public static (string Street, string Barangay, string City) ParseAddress(string address)
    {
        var parts = address.Split(',').Select(p => p.Trim()).ToArray();
        if (parts.Length >= 6) return ($"{parts[0]}, {parts[1]}", parts[2], parts[3]);
        if (parts.Length == 3) return (parts[0], parts[1], parts[2]);
        // Keep an unfamiliar address intact until the resident explicitly edits it.
        return (address, string.Empty, "Marikina City");
    }

    public void SyncContacts(IList<QuickActionItem> actions, ProfileUser user)
    {
        Sync(1, user.EmergencyContact1Name, user.EmergencyContact1Phone);
        Sync(2, user.EmergencyContact2Name, user.EmergencyContact2Phone);
        void Sync(int slot, string name, string phone)
        {
            var id = $"emergency_contact_{slot}";
            var owned = actions.Where(x => x.Id == id).ToList();
            var enabled = owned.FirstOrDefault()?.IsEnabled ?? true;
            foreach (var old in owned) actions.Remove(old);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone)) return;
            // Reuse an independently added matching contact instead of displaying it twice.
            var matching = actions.FirstOrDefault(x => x.IsCustom && x.ActionType == "CustomContact" &&
                x.PhoneNumber == phone && x.Title == name);
            if (matching != null) { matching.Id = id; return; }
            actions.Add(new QuickActionItem { Id = id, Title = name, PhoneNumber = phone,
                Subtitle = $"Emergency Contact: {phone}", ActionType = "CustomContact", IsCustom = true,
                IsEnabled = enabled, IconImage = "icon_hotlines.svg", IconBg = "#EFF6FF", IconColor = "#2563EB" });
        }
    }
}
