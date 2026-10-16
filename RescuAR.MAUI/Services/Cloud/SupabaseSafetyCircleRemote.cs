using System.Security.Cryptography;
using RescuAR.App.Models;
using RescuAR.App.Services.SafetyCircle;
using RescuAR.Services;

namespace RescuAR.App.Services.Cloud;

public sealed class SupabaseSafetyCircleRemote : ISafetyCircleRemote
{
    private readonly string _project = SupabaseService.Instance.SupabaseUrl;

    private Supabase.Client Client(string user)
    {
        var client = SupabaseService.Instance.Client ?? throw new InvalidOperationException("Supabase is unavailable.");
        if (SupabaseService.Instance.SupabaseUrl != _project || client.Auth.CurrentUser?.Id != user || string.IsNullOrWhiteSpace(client.Auth.CurrentSession?.AccessToken))
            throw new UnauthorizedAccessException("Please sign in again.");
        return client;
    }

    private async Task JoinMemberAsync(string user, string circleId)
    {
        var existing = await Client(user).From<SupabaseCircleMember>()
            .Where(m => m.CircleId == circleId).Where(m => m.UserId == user).Get();
        if (!existing.Models.Any())
        {
            try
            {
                await Client(user).From<SupabaseCircleMember>().Insert(new SupabaseCircleMember
                { CircleId = circleId, UserId = user, JoinedAt = DateTime.UtcNow });
            }
            catch
            {
                var confirmed = await Client(user).From<SupabaseCircleMember>()
                    .Where(m => m.CircleId == circleId).Where(m => m.UserId == user).Get();
                if (!confirmed.Models.Any()) throw;
            }
        }
    }

    public async Task<List<SupabaseSafetyCircle>> GetCirclesAsync(string user)
    {
        var memberships = await Client(user).From<SupabaseCircleMember>().Where(m => m.UserId == user).Get();
        var ids = memberships.Models.Select(m => m.CircleId).Distinct().ToList();
        var circles = ids.Count == 0 ? new List<SupabaseSafetyCircle>() :
            (await Client(user).From<SupabaseSafetyCircle>()
                .Filter("id", Supabase.Postgrest.Constants.Operator.In, ids).Get()).Models;
        // Recover a creator whose circle insert succeeded but whose membership insert was interrupted.
        var owned = await Client(user).From<SupabaseSafetyCircle>().Where(c => c.CreatedBy == user).Get();
        foreach (var circle in owned.Models.Where(c => !ids.Contains(c.Id)))
        {
            await JoinMemberAsync(user, circle.Id);
            circles.Add(circle);
        }
        return circles.GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.CreatedAt).ToList();
    }

    public async Task<SupabaseSafetyCircle> CreateAsync(string user, string name)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var code = new string(Enumerable.Range(0, 6).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        var response = await Client(user).From<SupabaseSafetyCircle>().Insert(new SupabaseSafetyCircle
        { Name = name, InviteCode = code, CreatedBy = user, CreatedAt = DateTime.UtcNow });
        var circle = response.Models.FirstOrDefault() ?? throw new InvalidOperationException("Circle creation was not confirmed. Refresh before retrying.");
        try { await JoinMemberAsync(user, circle.Id); }
        catch (Exception ex) { throw new InvalidOperationException("Circle was created, but membership is not confirmed. Refresh to recover it.", ex); }
        return circle;
    }

    public async Task<SupabaseSafetyCircle> JoinAsync(string user, string code)
    {
        var response = await Client(user).From<SupabaseSafetyCircle>().Where(c => c.InviteCode == code).Get();
        var circle = response.Models.FirstOrDefault() ?? throw new InvalidOperationException("Invite code is unavailable or invalid.");
        await JoinMemberAsync(user, circle.Id);
        return circle;
    }

    public async Task<SupabaseSafetyCircle> RenameAsync(string user, string id, string name)
    {
        await Client(user).From<SupabaseSafetyCircle>().Where(c => c.Id == id).Where(c => c.CreatedBy == user)
            .Set(c => c.Name, name).Update();
        var response = await Client(user).From<SupabaseSafetyCircle>().Where(c => c.Id == id).Where(c => c.CreatedBy == user).Get();
        return response.Models.FirstOrDefault(c => c.Name == name) ?? throw new InvalidOperationException("Rename was not confirmed.");
    }

    public async Task LeaveAsync(string user, string id)
    {
        await Client(user).From<SupabaseCircleMember>().Where(m => m.CircleId == id).Where(m => m.UserId == user).Delete();
        var remaining = await Client(user).From<SupabaseCircleMember>().Where(m => m.CircleId == id).Where(m => m.UserId == user).Get();
        if (remaining.Models.Any()) throw new InvalidOperationException("Leaving the circle was not confirmed.");
    }

    public async Task DeleteAsync(string user, string id)
    {
        // Backend RLS and foreign keys remain authoritative. Do not delete others' memberships from the client.
        await Client(user).From<SupabaseSafetyCircle>().Where(c => c.Id == id).Where(c => c.CreatedBy == user).Delete();
        var remaining = await Client(user).From<SupabaseSafetyCircle>().Where(c => c.Id == id).Get();
        if (remaining.Models.Any()) throw new InvalidOperationException("Deletion was not confirmed. Check database permissions and references.");
    }

    public async Task<List<User>> GetMembersAsync(string user, string id)
    {
        var memberships = await Client(user).From<SupabaseCircleMember>().Where(m => m.CircleId == id).Get();
        var ids = memberships.Models.Select(m => m.UserId).Where(uid => !string.IsNullOrWhiteSpace(uid)).Distinct().ToList();
        if (ids.Count == 0) return new();
        var users = new List<User>();
        try { users = (await Client(user).From<User>().Select("id,first_name,last_name,avatar_url")
            .Filter("id", Supabase.Postgrest.Constants.Operator.In, ids).Get()).Models; }
        catch (Exception ex) when (ex is not UnauthorizedAccessException) { /* Names can be private; membership still supplies the roster. */ }
        var missing = ids.Where(uid => !users.Any(u => u.Id == uid)).ToList();
        if (missing.Count > 0)
        {
            try
            {
                var profiles = await Client(user).From<SupabaseProfile>().Filter("id", Supabase.Postgrest.Constants.Operator.In, missing).Get();
                users.AddRange(profiles.Models.Select(p => new User { Id = p.Id, FirstName = p.FirstName, LastName = p.LastName }));
            }
            catch (Exception ex) when (ex is not UnauthorizedAccessException) { }
        }
        foreach (var uid in ids.Where(uid => !users.Any(u => u.Id == uid))) users.Add(new User { Id = uid, FirstName = "Circle member" });
        return users.Where(u => ids.Contains(u.Id)).ToList();
    }

    public async Task<List<SupabaseUserLocation>> GetLocationsAsync(string user, string id)
    {
        var memberships = await Client(user).From<SupabaseCircleMember>().Where(m => m.CircleId == id).Get();
        var ids = memberships.Models.Select(m => m.UserId).Distinct().ToList();
        if (ids.Count == 0) return new();
        return (await Client(user).From<SupabaseUserLocation>().Filter("user_id", Supabase.Postgrest.Constants.Operator.In, ids).Get()).Models;
    }

    public async Task PushLocationAsync(string user, SupabaseUserLocation location)
        => await Client(user).From<SupabaseUserLocation>().Upsert(location);

    public async Task<List<SupabaseCircleMessage>> GetMessagesAsync(string user, string id)
        => (await Client(user).From<SupabaseCircleMessage>().Where(m => m.CircleId == id)
            .Order("created_at", Supabase.Postgrest.Constants.Ordering.Ascending).Get()).Models;

    public async Task<SupabaseCircleMessage> SendAsync(string user, SupabaseCircleMessage message)
    {
        async Task<SupabaseCircleMessage?> ConfirmAsync() => (await Client(user).From<SupabaseCircleMessage>()
            .Where(m => m.Id == message.Id).Where(m => m.CircleId == message.CircleId).Where(m => m.UserId == user).Get()).Models.FirstOrDefault();
        // Stable client IDs make retries safe after a lost response without overwriting confirmed messages.
        var existing = await ConfirmAsync();
        if (existing != null) return existing;
        try { await Client(user).From<SupabaseCircleMessage>().Insert(message); }
        catch
        {
            var confirmed = await ConfirmAsync();
            if (confirmed != null) return confirmed;
            throw;
        }
        return await ConfirmAsync() ?? throw new InvalidOperationException("Message delivery was not confirmed.");
    }
}
