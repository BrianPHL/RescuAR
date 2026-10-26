using RescuAR.App.Models;
using RescuAR.App.Services.Profile;
using RescuAR.Services;

namespace RescuAR.App.Services.SafetyCircle;

internal sealed class MauiCircleMembershipContext : ICircleMembershipContext
{
    public bool IsOnline => Microsoft.Maui.Networking.Connectivity.Current.NetworkAccess == Microsoft.Maui.Networking.NetworkAccess.Internet;
    private static string Server => new Uri(SupabaseService.Instance.SupabaseUrl).AbsoluteUri.TrimEnd('/');
    public CircleMembershipScope Capture()
    {
        var owner = UserProfileService.Instance.CaptureOwner();
        var scope = new CircleMembershipScope(Server, owner.UserId, owner.Revision);
        Require(scope);
        return scope;
    }
    public void Require(CircleMembershipScope scope)
    {
        if (string.IsNullOrWhiteSpace(scope.UserId) || scope.Server != Server || scope.Revision != SupabaseService.Instance.SessionRevision ||
            SupabaseService.Instance.Client?.Auth.CurrentUser?.Id != scope.UserId)
            throw new OperationCanceledException("Your account or server changed. Reopen Circle management.");
    }
}

internal static class CircleMembershipRuntime
{
    internal static MauiCircleMembershipContext Context { get; } = new();
    public static CircleMembershipService Current { get; } = new(Context, new SupabaseCircleMembershipRemote(Context),
        new FileCircleMembershipStore(Path.Combine(FileSystem.AppDataDirectory, "circle-memberships-v1")));
}

internal sealed class SupabaseCircleMembershipRemote(MauiCircleMembershipContext context) : ICircleMembershipRemote
{
    private async Task<Supabase.Client> Client(CircleMembershipScope scope)
    {
        context.Require(scope);
        if (!context.IsOnline) throw new CircleMembershipOfflineException("Connect to confirm Circle membership changes.");
        var client = await SupabaseService.Instance.GetClientAsync() ?? throw new InvalidOperationException("Please sign in again.");
        context.Require(scope);
        return client;
    }
    private static CircleMembership Convert(SupabaseSafetyCircle circle) => new(circle.Id, circle.Name ?? "", circle.InviteCode ?? "", circle.CreatedBy ?? "", circle.CreatedAt);

    public async Task<List<CircleMembership>> GetAsync(CircleMembershipScope scope)
    {
        var client = await Client(scope);
        var joined = await client.From<SupabaseCircleMember>().Where(member => member.UserId == scope.UserId).Get();
        context.Require(scope);
        var ids = joined.Models.Select(member => member.CircleId).Distinct().ToList();
        var circles = new List<SupabaseSafetyCircle>();
        if (ids.Count > 0)
        {
            circles = (await client.From<SupabaseSafetyCircle>().Filter("id", Supabase.Postgrest.Constants.Operator.In, ids).Get()).Models;
            context.Require(scope);
        }
        var owned = await client.From<SupabaseSafetyCircle>().Where(circle => circle.CreatedBy == scope.UserId).Get();
        context.Require(scope);
        foreach (var circle in owned.Models)
        {
            if (!ids.Contains(circle.Id)) await ConfirmMembership(scope, client, circle.Id);
            context.Require(scope);
            if (!circles.Any(existing => existing.Id == circle.Id)) circles.Add(circle);
        }
        return circles.Select(Convert).ToList();
    }
    private async Task ConfirmMembership(CircleMembershipScope scope, Supabase.Client client, string circleId)
    {
        context.Require(scope);
        var before = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == circleId).Where(member => member.UserId == scope.UserId).Get();
        context.Require(scope);
        if (before.Models.Any()) return;
        try
        {
            await client.From<SupabaseCircleMember>().Insert(new SupabaseCircleMember { CircleId = circleId, UserId = scope.UserId, JoinedAt = DateTime.UtcNow });
        }
        catch (Exception) when (SupabaseService.Instance.Client?.Auth.CurrentUser?.Id == scope.UserId)
        {
            context.Require(scope);
            // A racing/response-lost insert is successful only if membership is subsequently visible.
            var raced = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == circleId).Where(member => member.UserId == scope.UserId).Get();
            context.Require(scope);
            if (raced.Models.Any()) return;
            throw;
        }
        context.Require(scope);
        var confirmed = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == circleId).Where(member => member.UserId == scope.UserId).Get();
        context.Require(scope);
        if (!confirmed.Models.Any()) throw new InvalidOperationException("Circle membership was not confirmed. Refresh before retrying.");
    }
    public async Task<CircleMembership> CreateAsync(CircleMembershipScope scope, string name)
    {
        var client = await Client(scope);
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        string code = new(Enumerable.Range(0, 6).Select(_ => alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
        var response = await client.From<SupabaseSafetyCircle>().Insert(new SupabaseSafetyCircle { Name = name, InviteCode = code, CreatedBy = scope.UserId, CreatedAt = DateTime.UtcNow });
        context.Require(scope);
        var circle = response.Models.FirstOrDefault() ?? throw new InvalidOperationException("Creation was not confirmed. Refresh before creating another Circle.");
        try { await ConfirmMembership(scope, client, circle.Id); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { throw new InvalidOperationException("The Circle was created, but membership is not confirmed. Refresh to recover it before creating another Circle.", error); }
        return Convert(circle);
    }
    public async Task<CircleMembership> JoinAsync(CircleMembershipScope scope, string code)
    {
        var client = await Client(scope);
        var response = await client.From<SupabaseSafetyCircle>().Where(circle => circle.InviteCode == code).Get();
        context.Require(scope);
        var circle = response.Models.FirstOrDefault() ?? throw new InvalidOperationException("This invite code is invalid or unavailable to your account.");
        await ConfirmMembership(scope, client, circle.Id);
        return Convert(circle);
    }
    public async Task<CircleMembership> RenameAsync(CircleMembershipScope scope, string id, string name)
    {
        var client = await Client(scope);
        await client.From<SupabaseSafetyCircle>().Where(circle => circle.Id == id).Where(circle => circle.CreatedBy == scope.UserId).Set(circle => circle.Name, name).Update();
        context.Require(scope);
        var response = await client.From<SupabaseSafetyCircle>().Where(circle => circle.Id == id).Where(circle => circle.CreatedBy == scope.UserId).Get();
        context.Require(scope);
        return Convert(response.Models.FirstOrDefault(circle => circle.Name == name) ?? throw new InvalidOperationException("Rename was not confirmed. Refresh before retrying."));
    }
    public async Task LeaveAsync(CircleMembershipScope scope, string id)
    {
        var client = await Client(scope);
        await client.From<SupabaseCircleMember>().Where(member => member.CircleId == id).Where(member => member.UserId == scope.UserId).Delete();
        context.Require(scope);
        var response = await client.From<SupabaseCircleMember>().Where(member => member.CircleId == id).Where(member => member.UserId == scope.UserId).Get();
        context.Require(scope);
        if (response.Models.Any()) throw new InvalidOperationException("Leaving was not confirmed. Refresh before retrying.");
    }
    public async Task DeleteAsync(CircleMembershipScope scope, string id)
    {
        var client = await Client(scope);
        // Delete only the creator's Circle row. Existing backend RLS/FKs own atomic cascade behavior.
        await client.From<SupabaseSafetyCircle>().Where(circle => circle.Id == id).Where(circle => circle.CreatedBy == scope.UserId).Delete();
        context.Require(scope);
        var response = await client.From<SupabaseSafetyCircle>().Where(circle => circle.Id == id).Get();
        context.Require(scope);
        if (response.Models.Any()) throw new InvalidOperationException("Deletion was not confirmed. Existing database permissions or references may prevent it.");
    }
}
