using System.Security.Cryptography;
using System.Text;
using RescuAR.App.Models;
using RescuAR.App.Services.SafetyCircle;
using RescuAR.Services;

namespace RescuAR.App.Services.Cloud;

public class SafetyCircleService
{
    // Share the write gate across dashboard and page service instances.
    private static readonly Dictionary<string, SafetyCircleSyncService> Shared = new();
    public SafetyCircleSyncService Sync
    {
        get
        {
            string project = SupabaseService.Instance.SupabaseUrl;
            lock (Shared)
            {
                if (!Shared.TryGetValue(project, out var sync))
                {
                    sync = new SafetyCircleSyncService(new SupabaseSafetyCircleRemote(),
                        new FileCircleStateStore(Path.Combine(FileSystem.AppDataDirectory, "safety-circles-v2",
                            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project))))),
                        () => SupabaseService.Instance.SupabaseUrl == project
                            ? SupabaseService.Instance.Client?.Auth.CurrentUser?.Id ?? string.Empty : string.Empty);
                    Shared[project] = sync;
                }
                return sync;
            }
        }
    }
    public string GetCurrentUserId() => Sync.GetCurrentUserId();
    public Task<SupabaseSafetyCircle> CreateCircleAsync(string name) => Sync.CreateAsync(name);
    public Task<SupabaseSafetyCircle> JoinCircleWithCodeAsync(string code) => Sync.JoinAsync(code);
    public async Task<List<SupabaseSafetyCircle>> GetMyCirclesAsync() => (await Sync.GetCirclesAsync()).Items;
    public async Task<List<User>> GetCircleMembersAsync(string id) => (await Sync.GetMembersAsync(id)).Items;
    public async Task<List<SupabaseUserLocation>> GetCircleLocationsAsync(string id) => (await Sync.GetLocationsAsync(id)).Items;
    public Task PushLocationAsync(double lat, double lon, string status = "Location shared") => Sync.PushLocationAsync(lat, lon, status);

    public Task<CircleMessageState> SendMessageAsync(string circleId, string text, string? mediaUrl = null, string type = "Text")
    {
        var metadata = SupabaseService.Instance.Client?.Auth.CurrentUser?.UserMetadata;
        string Read(string key) => metadata?.TryGetValue(key, out var value) == true ? value?.ToString() ?? string.Empty : string.Empty;
        var name = $"{Read("first_name")} {Read("last_name")}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = Read("full_name");
        if (string.IsNullOrWhiteSpace(name)) name = "Circle member";
        return Sync.SendAsync(circleId, text, mediaUrl, type, name, Read("avatar_url"));
    }
}
