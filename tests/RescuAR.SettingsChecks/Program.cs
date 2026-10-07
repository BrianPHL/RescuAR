using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Settings;
using RescuAR.Navigation.Guidance;

int passed = 0;
void Check(bool condition, string behavior)
{
    if (!condition) throw new InvalidOperationException(behavior);
    passed++;
    Console.WriteLine($"PASS: {behavior}");
}
var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var preferences = new TestPreferences();
var settings = new AppSettingsStore(preferences);
List<AppSetting> changes = [];
settings.Changed += changes.Add;
Check(!settings.Get(AppSetting.NavigationVoice), "voice starts muted, matching existing camera behavior");
Check(settings.Get(AppSetting.EmergencySiren) && settings.Get(AppSetting.ForegroundAdvisories), "existing advisory sounds and popups remain enabled by default");
settings.Set(AppSetting.NavigationVoice, true);
Check(preferences.Values["NavigationVoiceEnabled"] && changes.SequenceEqual([AppSetting.NavigationVoice]), "settings and camera share the established voice preference and publish a change");
settings.Set(AppSetting.NavigationVoice, true);
Check(changes.Count == 1, "unchanged preferences do not cancel or restart guidance");
settings.Set(AppSetting.ForegroundAdvisories, false);
Check(!settings.Get(AppSetting.ForegroundAdvisories) && settings.Get(AppSetting.EmergencySiren), "popup and sound preferences are independent");
settings.Set(AppSetting.DetailedOfflineMap, false);
Check(!settings.Get(AppSetting.DetailedOfflineMap) && preferences.Values.Count == 3, "optional map preference does not change routing or account preferences");
Check(new AppSettingsStore(preferences).Get(AppSetting.NavigationVoice), "preferences survive a new settings-store instance");
preferences.FailWrites = true;
int previous = changes.Count;
try { settings.Set(AppSetting.NavigationVoice, false); throw new Exception("Expected write failure"); }
catch (IOException) { }
Check(changes.Count == previous && settings.Get(AppSetting.NavigationVoice), "a failed preference write does not announce a confirmed change");
preferences.FailWrites = false;

PedestrianTurnGuidanceService.TurnGuidanceSnapshot Turn(double distance) =>
    new(true, PedestrianTurnGuidanceService.TurnInstruction.Right, distance, 90, 100, "Turn right");
var haptic = new NavigationHapticGuidanceService();
var route = new object();
var now = DateTimeOffset.UtcNow;
Check(!haptic.ShouldPulse(route, false, Turn(10), 0, now), "disabled haptics do not emit turn feedback");
Check(!haptic.ShouldPulse(route, true, PedestrianTurnGuidanceService.TurnGuidanceSnapshot.Unavailable, 0, now), "unconfirmed guidance cannot emit a turn pulse");
Check(!haptic.ShouldPulse(route, true, Turn(21), 0, now), "distant turns do not emit an early pulse");
Check(haptic.ShouldPulse(route, true, Turn(20), 0, now), "a confirmed turn within twenty meters pulses once");
Check(!haptic.ShouldPulse(route, true, Turn(15), 5, now.AddSeconds(10)), "changing displayed distance does not repeat a pulse for the same turn");
Check(!haptic.ShouldPulse(route, true, Turn(double.NaN), 0, now), "invalid turn distances do not reach hardware feedback");
Check(!haptic.ShouldPulse(route, true, Turn(-1), 0, now), "a passed maneuver does not pulse");
Check(haptic.ShouldPulse(route, true, Turn(15), 40, now.AddSeconds(11)), "the next confirmed turn can pulse after the cooldown");
Check(!haptic.ShouldPulse(route, true, Turn(5), 80, now.AddSeconds(12)), "rapidly changing turn targets respect the pulse cooldown");
Check(haptic.ShouldPulse(new object(), true, Turn(10), 0, now.AddSeconds(12)), "a replacement route has an independent turn identity");

string packPath = Path.Combine(root, "RescuAR.MAUI/Resources/Raw", OfflineMapInstaller.PackageName);
using (var tiles = new OfflineTileArchive(File.OpenRead(packPath)))
using (var index = ZipFile.OpenRead(packPath))
{
    Check(tiles.Manifest is { TileCount: 3555, MinZoom: 13, MaxZoom: 18, Scheme: "xyz" }, "the real custom raster package advertises its measured coverage");
    int read = 0;
    foreach (var entry in index.Entries.Where(entry => entry.FullName.StartsWith("tiles/")))
    {
        var parts = entry.FullName.Split('/');
        var png = tiles.ReadTile(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(Path.GetFileNameWithoutExtension(parts[3])));
        if (png is null || png.Length != entry.Length) throw new Exception("Missing real offline PNG tile");
        read++;
    }
    Check(read == 3555, "every real PNG tile is readable by the production loader without a network provider");
    Check(tiles.ReadTile(12, 0, 0) is null && tiles.ReadTile(19, 0, 0) is null, "unsupported zooms return unavailable instead of remote fallback");
    Check(tiles.ReadTile(13, -1, 0) is null && tiles.ReadTile(13, 0, 8192) is null, "invalid tile indices are rejected");
    Check(tiles.ReadTile(13, 0, 0) is null, "locations outside the export do not get a fabricated tile");
}
MemoryStream BadPack(string metadata, byte[]? tile = null)
{
    var stream = new MemoryStream();
    using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
    {
        using (var writer = new StreamWriter(zip.CreateEntry("manifest.json").Open())) writer.Write(metadata);
        if (tile is not null) { using var output = zip.CreateEntry("tiles/13/1/1.png").Open(); output.Write(tile); }
    }
    stream.Position = 0; return stream;
}
string validMetadata = "{\"Version\":1,\"Format\":\"png\",\"Scheme\":\"xyz\",\"MinZoom\":13,\"MaxZoom\":18,\"TileCount\":1,\"Bounds\":[121,14,122,15],\"Source\":\"test\"}";
try { using var _ = new OfflineTileArchive(BadPack(validMetadata.Replace("png", "pbf"))); throw new Exception("Expected rejected format"); }
catch (InvalidDataException) { Check(true, "vector PBF packages cannot masquerade as PNG offline maps"); }
using (var badTile = new OfflineTileArchive(BadPack(validMetadata, Encoding.UTF8.GetBytes("not a png image"))))
{
    try { badTile.ReadTile(13, 1, 1); throw new Exception("Expected rejected tile"); }
    catch (InvalidDataException) { Check(true, "corrupt raster tiles fail explicitly"); }
}
string temporary = Path.Combine(root, "artifacts", $"settings-checks-{Guid.NewGuid():N}");
Directory.CreateDirectory(temporary);
try
{
    byte[] contents = Encoding.UTF8.GetBytes("controlled package contents");
    string hash = Convert.ToHexString(SHA256.HashData(contents));
    int opens = 0;
    Task<Stream> Open() { opens++; return Task.FromResult<Stream>(new MemoryStream(contents)); }
    string installed = await OfflineMapInstaller.InstallAsync(temporary, Open, expectedHash: hash, expectedSize: contents.Length);
    Check(File.ReadAllBytes(installed).SequenceEqual(contents), "first install copies and verifies package bytes before publishing the map");
    await OfflineMapInstaller.InstallAsync(temporary, Open, expectedHash: hash, expectedSize: contents.Length);
    Check(opens == 1, "a verified installed map is reused without copying the package again");
    File.WriteAllText(Path.Combine(temporary, "pending-circle-message.json"), "keep me");
    await File.WriteAllBytesAsync(installed, Encoding.UTF8.GetBytes("corrupt"));
    await OfflineMapInstaller.InstallAsync(temporary, Open, expectedHash: hash, expectedSize: contents.Length);
    Check(File.ReadAllBytes(installed).SequenceEqual(contents) && opens == 2, "a corrupt local map is repaired from packaged bytes");
    try { await OfflineMapInstaller.InstallAsync(temporary, Open, expectedHash: new string('0', 64), expectedSize: contents.Length); throw new Exception("Expected failed integrity"); }
    catch (InvalidDataException) { }
    Check(File.ReadAllBytes(installed).SequenceEqual(contents), "failed replacement integrity does not overwrite the previous map");
    Check(Directory.GetFiles(temporary, "*.tmp").Length == 0, "failed installations leave no partial copy");
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    try { await OfflineMapInstaller.InstallAsync(temporary, Open, canceled.Token, hash, contents.Length); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
    Check(File.ReadAllBytes(installed).SequenceEqual(contents), "canceled preparation leaves the installed map intact");
    Check(File.ReadAllText(Path.Combine(temporary, "pending-circle-message.json")) == "keep me", "map installation and repair preserve unrelated user data");
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packPath))).Equals(OfflineMapInstaller.PackageHash, StringComparison.OrdinalIgnoreCase) &&
        new FileInfo(packPath).Length == OfflineMapInstaller.PackageSize, "the real packaged map matches the installer's pinned integrity evidence");
}
finally
{
    // The generated folder is constrained to this repository's artifacts directory.
    foreach (string file in Directory.GetFiles(temporary)) File.Delete(file);
    Directory.Delete(temporary);
}
Console.WriteLine($"Settings checks passed: {passed}");

sealed class TestPreferences : IBooleanPreferences
{
    public Dictionary<string, bool> Values { get; } = [];
    public bool FailWrites { get; set; }
    public bool Get(string key, bool defaultValue) => Values.GetValueOrDefault(key, defaultValue);
    public void Set(string key, bool value)
    {
        if (FailWrites) throw new IOException("Storage unavailable");
        Values[key] = value;
    }
}
