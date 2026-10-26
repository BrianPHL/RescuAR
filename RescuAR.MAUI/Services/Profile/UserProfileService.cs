using System.Linq.Expressions;
using Newtonsoft.Json;
using RescuAR.App.Services.Authentication;
using RescuAR.Services;
using ProfileUser = RescuAR.App.Models.User;
using AuthUser = Supabase.Gotrue.User;

namespace RescuAR.App.Services.Profile;

public sealed class UserProfileService
{
    public static UserProfileService Instance { get; } = new();
    private readonly SemaphoreSlim writes = new(1, 1);
    private string Project => new Uri(SupabaseService.Instance.SupabaseUrl).Host;
    public string UserId => SupabaseService.Instance.Client?.Auth.CurrentUser?.Id
        ?? throw new InvalidOperationException("Please sign in again.");
    private string Root(string id) => $"Profile:{Project}:{id}:";
    public string GetLocal(string name) => Preferences.Default.Get(Root(UserId) + name, string.Empty);
    public void SetLocal(string name, string value) => Preferences.Default.Set(Root(UserId) + name, value);

    public ProfileAccountScope CaptureOwner()
    {
        var owner = new ProfileAccountScope(Project, UserId, SupabaseService.Instance.SessionRevision);
        RequireOwner(owner);
        return owner;
    }

    public void RequireOwner(ProfileAccountScope owner, CancellationToken token = default) =>
        RequireAccount(owner.UserId, token, owner.Project, owner.Revision);

    public bool IsCurrentOwner(ProfileAccountScope owner)
    {
        try { RequireOwner(owner); return true; }
        catch (OperationCanceledException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    public string GetLocal(ProfileAccountScope owner, string name)
    {
        RequireOwner(owner);
        return Preferences.Default.Get($"Profile:{owner.Project}:{owner.UserId}:" + name, string.Empty);
    }

    public void SetLocal(ProfileAccountScope owner, string name, string value)
    {
        RequireOwner(owner);
        Preferences.Default.Set($"Profile:{owner.Project}:{owner.UserId}:" + name, value);
    }

    internal sealed record ProfileDisplay(ProfileUser User, bool Cached);
    internal async Task<ProfileDisplay> LoadForDisplayAsync(ProfileAccountScope owner, CancellationToken token = default)
    {
        RequireOwner(owner, token);
        if (Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess != Microsoft.Maui.Networking.NetworkAccess.Internet)
            return Cached();
        try
        {
            var user = await LoadAsync(token, owner.UserId);
            RequireOwner(owner, token);
            return new(user, false);
        }
        catch (Exception error) when (error is HttpRequestException or TimeoutException || error is TaskCanceledException && !token.IsCancellationRequested)
        {
            return Cached();
        }

        ProfileDisplay Cached()
        {
            RequireOwner(owner, token);
            var user = ReadCached(owner.UserId) ?? throw new InvalidOperationException("Connect to load this account's profile for the first time.");
            user.Email = SupabaseService.Instance.Client?.Auth.CurrentUser?.Email ?? user.Email;
            return new(user, true);
        }
    }

    internal ProfileUser? ReadCached(string id)
    {
        try
        {
            var user = JsonConvert.DeserializeObject<ProfileUser>(Preferences.Default.Get(Root(id) + "User", string.Empty));
            return user?.Id == id ? user : null;
        }
        catch (JsonException) { return null; }
    }

    public async Task<ProfileUser> LoadAsync(CancellationToken token = default, string? expectedUserId = null)
    {
        var client = await SupabaseService.Instance.GetClientAsync().WaitAsync(token)
            ?? throw new InvalidOperationException("Supabase is not configured.");
        string id = UserId;
        if (expectedUserId is not null && expectedUserId != id)
            throw new OperationCanceledException("Your account changed. Please retry.");
        string project = Project;
        long revision = SupabaseService.Instance.SessionRevision;
        if (string.IsNullOrWhiteSpace(client.Auth.CurrentSession?.AccessToken)) throw new InvalidOperationException("Please sign in again.");
        var response = await client.From<ProfileUser>().Where(user => user.Id == id).Get().WaitAsync(token);
        RequireAccount(id, token, project, revision);
        if (response.Models.Count == 0)
        {
            // An empty successful read authorizes creation; a failed read never does.
            await client.From<ProfileUser>().Upsert(FromMetadata(client.Auth.CurrentUser!),
                new Supabase.Postgrest.QueryOptions { DuplicateResolution = Supabase.Postgrest.QueryOptions.DuplicateResolutionType.IgnoreDuplicates }).WaitAsync(token);
            RequireAccount(id, token, project, revision);
            response = await client.From<ProfileUser>().Where(user => user.Id == id).Get().WaitAsync(token);
        }
        RequireAccount(id, token, project, revision);
        var result = response.Models.FirstOrDefault() ?? throw new InvalidOperationException("Your profile could not be loaded. Please retry.");
        if (result.Id != id) throw new InvalidOperationException("Your account changed. Please retry.");
        result.Email = client.Auth.CurrentUser?.Email ?? result.Email;
        Preferences.Default.Set(Root(id) + "User", JsonConvert.SerializeObject(result));
        return result;
    }

    public Task<ProfileUser> PatchAsync(params (Expression<Func<ProfileUser, object>> Field, object Value)[] fields) =>
        PatchOwnedAsync(CaptureOwner(), fields);

    private Task<ProfileUser> PatchForUserAsync(string id, params (Expression<Func<ProfileUser, object>> Field, object Value)[] fields)
    {
        var owner = CaptureOwner();
        if (owner.UserId != id) throw new OperationCanceledException("Your account changed. Reopen this screen.");
        return PatchOwnedAsync(owner, fields);
    }

    public async Task<ProfileUser> PatchOwnedAsync(ProfileAccountScope owner, params (Expression<Func<ProfileUser, object>> Field, object Value)[] fields)
    {
        RequireOwner(owner);
        if (fields.Length == 0) throw new InvalidOperationException("There are no fields to save.");
        await writes.WaitAsync();
        try
        {
            RequireOwner(owner);
            if (Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess != Microsoft.Maui.Networking.NetworkAccess.Internet)
                throw new IOException("Connect before saving changes to your account.");
            await LoadAsync(CancellationToken.None, owner.UserId);
            RequireOwner(owner);
            var client = SupabaseService.Instance.Client!;
            var query = client.From<ProfileUser>().Where(user => user.Id == owner.UserId);
            foreach (var field in fields) query = query.Set(field.Field, field.Value);
            var response = await query.Update();
            RequireOwner(owner);
            var result = response.Models.FirstOrDefault() ?? throw new InvalidOperationException("No profile update was confirmed. Please retry.");
            if (result.Id != owner.UserId) throw new OperationCanceledException("The returned profile belongs to another account.");
            result.Email = client.Auth.CurrentUser?.Email ?? result.Email;
            Preferences.Default.Set(Root(owner.UserId) + "User", JsonConvert.SerializeObject(result));
            if (Preferences.Default.Get("CurrentUserId", string.Empty) == owner.UserId &&
                Preferences.Default.Get("CurrentAuthProject", string.Empty) == owner.Project)
            {
                Preferences.Default.Set("UserFirstName", result.FirstName);
                Preferences.Default.Set("UserMiddleName", result.MiddleName);
                Preferences.Default.Set("UserLastName", result.LastName);
                Preferences.Default.Set("UserPhoneNumber", result.PhoneNumber);
                Preferences.Default.Set("UserPhone", result.PhoneNumber);
                Preferences.Default.Set("UserAddress", result.Address);
                Preferences.Default.Set("UserAvatarUrl", result.AvatarUrl);
            }
            return result;
        }
        finally { writes.Release(); }
    }

    public Task<ProfileUser> SaveAddressAsync(string expectedUserId, string address)
    {
        if (UserId != expectedUserId) throw new OperationCanceledException("Your account changed. Reopen the address screen.");
        return PatchForUserAsync(expectedUserId, (user => user.Address, address));
    }

    internal void Activate(string id, string email, AuthUser? metadata, bool offline)
    {
        string previous = Preferences.Default.Get("CurrentUserId", string.Empty);
        if (previous != id || Preferences.Default.Get("CurrentAuthProject", string.Empty) != Project)
            AuthenticationIdentity.ClearActive(new MauiIdentityPreferences(), previous);
        var user = ReadCached(id) ?? (metadata?.Id == id ? FromMetadata(metadata) : new ProfileUser { Id = id, Email = email });
        Preferences.Default.Set("CurrentAuthProject", Project);
        Preferences.Default.Set("CurrentUserId", id);
        Preferences.Default.Set("current_user_id", id);
        Preferences.Default.Set("UserEmail", email);
        if (Preferences.Default.Get("RememberMe", false)) Preferences.Default.Set("SavedLoginEmail", email);
        else Preferences.Default.Remove("SavedLoginEmail");
        if (metadata?.Id == id && metadata.UserMetadata?.TryGetValue("birthday", out var birthday) == true)
            Preferences.Default.Set(Root(id) + "Birthday", birthday?.ToString() ?? string.Empty);
        Preferences.Default.Set("UserFirstName", user.FirstName);
        Preferences.Default.Set("UserLastName", user.LastName);
        Preferences.Default.Set("UserPhoneNumber", user.PhoneNumber);
        Preferences.Default.Set("UserPhone", user.PhoneNumber);
        Preferences.Default.Set("UserAddress", user.Address);
        Preferences.Default.Set("UserAvatarUrl", user.AvatarUrl);
        Preferences.Default.Set("EmergencyContact1Name", user.EmergencyContact1Name);
        Preferences.Default.Set("EmergencyContact1Phone", user.EmergencyContact1Phone);
        Preferences.Default.Set("EmergencyContact2Name", user.EmergencyContact2Name);
        Preferences.Default.Set("EmergencyContact2Phone", user.EmergencyContact2Phone);
        Preferences.Default.Set("HasSignedUp", true);
        Preferences.Default.Set("IsLoggedIn", true);
        Preferences.Default.Set("AuthenticationOffline", offline);
    }

    private void RequireAccount(string id, CancellationToken token, string project, long revision)
    {
        token.ThrowIfCancellationRequested();
        if (SupabaseService.Instance.Client?.Auth.CurrentUser?.Id != id || Project != project || SupabaseService.Instance.SessionRevision != revision)
            throw new OperationCanceledException("Your account changed. Please reopen this screen.");
    }

    private static ProfileUser FromMetadata(AuthUser user)
    {
        string Get(string key) => user.UserMetadata?.TryGetValue(key, out var value) == true ? value?.ToString() ?? string.Empty : string.Empty;
        string first = Get("first_name"), last = Get("last_name");
        if (string.IsNullOrWhiteSpace(first)) first = Get("given_name");
        if (string.IsNullOrWhiteSpace(last)) last = Get("family_name");
        if (string.IsNullOrWhiteSpace(first) && string.IsNullOrWhiteSpace(last))
        {
            string full = Get("full_name");
            if (string.IsNullOrWhiteSpace(full)) full = Get("name");
            var parts = full.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            first = parts.FirstOrDefault() ?? string.Empty;
            last = parts.Length > 1 ? parts[1] : string.Empty;
        }
        return new ProfileUser { Id = user.Id!, Email = user.Email ?? string.Empty,
            FirstName = first, MiddleName = Get("middle_name"), LastName = last,
            PhoneNumber = Get("phone"), Address = Get("address"), AvatarUrl = Get("avatar_url") };
    }
}
