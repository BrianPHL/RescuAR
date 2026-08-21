using Supabase;

namespace RescuAR.Services;

public class SupabaseService
{
    private static SupabaseService? _instance;
    public static SupabaseService Instance => _instance ??= new SupabaseService();

    public const string PlaceholderUrl = "https://itxjqcnvxlgzeqkivhhc.supabase.co";
    public const string PlaceholderKey = "sb_publishable_vD7hDjeuohyysFweHnJPPQ_xId2pJ4F";

    public string SupabaseUrl { get; private set; } = PlaceholderUrl;
    public string SupabaseKey { get; private set; } = PlaceholderKey;
    public string GoogleWebClientId { get; private set; } = "110430882823-ck8pi6d9ngiedo78mmg3gpsf6f2p9ove.apps.googleusercontent.com";

    public Client? Client { get; private set; }

    public bool IsMockMode => string.IsNullOrWhiteSpace(SupabaseUrl) || 
                              SupabaseUrl == "https://your-supabase-project.supabase.co" || 
                              string.IsNullOrWhiteSpace(SupabaseKey) || 
                              SupabaseKey == "your-supabase-anon-key";

    private SupabaseService()
    {
        LoadKeys();
        InitializeClient();
    }

    public void LoadKeys()
    {
        SupabaseUrl = Preferences.Default.Get("SupabaseUrl", PlaceholderUrl);
        SupabaseKey = Preferences.Default.Get("SupabaseKey", PlaceholderKey);
        
        // Reset preferences if they contain old placeholders
        if (SupabaseUrl == "https://your-supabase-project.supabase.co")
        {
            SupabaseUrl = PlaceholderUrl;
            Preferences.Default.Set("SupabaseUrl", PlaceholderUrl);
        }
        if (SupabaseKey == "your-supabase-anon-key")
        {
            SupabaseKey = PlaceholderKey;
            Preferences.Default.Set("SupabaseKey", PlaceholderKey);
        }

        GoogleWebClientId = Preferences.Default.Get("GoogleWebClientId", "110430882823-ck8pi6d9ngiedo78mmg3gpsf6f2p9ove.apps.googleusercontent.com");
    }

    public void SaveKeys(string url, string key, string googleWebClientId)
    {
        SupabaseUrl = url.Trim();
        SupabaseKey = key.Trim();
        GoogleWebClientId = googleWebClientId.Trim();
        Preferences.Default.Set("SupabaseUrl", SupabaseUrl);
        Preferences.Default.Set("SupabaseKey", SupabaseKey);
        Preferences.Default.Set("GoogleWebClientId", GoogleWebClientId);
        InitializeClient();
    }

    public void InitializeClient()
    {
        if (IsMockMode)
        {
            Client = null;
            return;
        }

        try
        {
            var options = new SupabaseOptions
            {
                AutoRefreshToken = true,
                AutoConnectRealtime = true
            };
            Client = new Client(SupabaseUrl, SupabaseKey, options);
        }
        catch
        {
            Client = null;
        }
    }
}
