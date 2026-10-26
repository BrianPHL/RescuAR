using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.MAUI.Services.Settings;

internal static class OfflineMapChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string fixtures = Path.Combine(repository, "artifacts/offline-map-unit-fixtures", Guid.NewGuid().ToString("N"));
        if (!Directory.Exists(Path.Combine(repository, "RescuAR.MAUI")) || !Path.GetFullPath(fixtures).StartsWith(Path.Combine(repository, "artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Offline-map fixtures must stay inside this worktree's artifacts directory.");
        Directory.CreateDirectory(fixtures);

        string packaged = Path.Combine(repository, "RescuAR.MAUI/Resources/Raw", OfflineMapInstaller.PackageName);
        using (var input = File.OpenRead(packaged))
            check(input.Length == OfflineMapInstaller.PackageSize && Convert.ToHexString(SHA256.HashData(input)).Equals(OfflineMapInstaller.PackageHash, StringComparison.OrdinalIgnoreCase),
                "the actual converted archive matches its pinned Athena payload size and checksum");
        using (var archive = new OfflineTileArchive(File.OpenRead(packaged)))
        using (var index = new ZipArchive(File.OpenRead(packaged)))
        {
            check(archive.Manifest.MinZoom == 13 && archive.Manifest.MaxZoom == 18 && archive.Manifest.TileCount == 3555,
                "the actual package declares its limited zoom range and 3555 tiles");
            var entries = index.Entries.Where(entry => entry.FullName.StartsWith("tiles/") && entry.FullName.EndsWith(".png")).ToArray();
            int read = 0;
            foreach (var entry in entries)
            {
                var parts = entry.FullName.Split('/');
                int zoom = int.Parse(parts[1]), column = int.Parse(parts[2]), row = int.Parse(Path.GetFileNameWithoutExtension(parts[3]));
                var tile = archive.ReadTile(zoom, column, row);
                if (tile is null || tile.Length != entry.Length) throw new InvalidOperationException("A packaged PNG failed the actual production reader: " + entry.FullName);
                read++;
            }
            check(read == 3555, "all 3555 supplied PNG tiles pass the production reader without extracting files or using a network");
            check(archive.Covers(121.1029, 14.6507, 14), "the supplied package contains an actual tile at the usual map browsing center");
            check(!archive.Covers(121.0, 14.65, 14) && !archive.Covers(121.1029, 14.6507, 12) && !archive.Covers(double.NaN, 14.65, 14),
                "outside coverage, unsupported zoom and invalid coordinates cannot claim packaged coverage");
            check(archive.ReadTile(12, 0, 0) is null && archive.ReadTile(14, -1, 0) is null && archive.ReadTile(14, 1 << 14, 0) is null && archive.ReadTile(14, 0, 0) is null,
                "unsupported, invalid and missing tile indexes return no fabricated replacement");
        }

        var manifest = new OfflineMapManifest(1, "png", "xyz", 1, 1, 1, new[] { -10d, -10d, 10d, 10d }, "unit fixture");
        byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        byte[] fixture = Package(manifest, png);
        string digest = Convert.ToHexString(SHA256.HashData(fixture));
        using (var archive = new OfflineTileArchive(new MemoryStream(fixture)))
        {
            check(archive.ReadTile(1, 1, 1)!.SequenceEqual(png), "the archive reads the requested XYZ tile exactly");
            check(archive.Covers(1, -1, 1) && !archive.Covers(-1, 1, 1), "coverage checks the actual center tile rather than treating manifest bounds as complete coverage");
        }
        foreach (var invalid in new[] { manifest with { Format = "pbf" }, manifest with { Scheme = "tms" }, manifest with { Version = 2 },
                     manifest with { MinZoom = -1 }, manifest with { MaxZoom = 23 }, manifest with { TileCount = 0 }, manifest with { Bounds = new[] { 10d, -10d, -10d, 10d } } })
        {
            using var stream = new MemoryStream(Package(invalid, png));
            check(Throws<InvalidDataException>(() => new OfflineTileArchive(stream)) && !stream.CanRead,
                "unsupported manifest is rejected and its stream is closed: " + JsonSerializer.Serialize(invalid));
        }
        using (var archive = new OfflineTileArchive(new MemoryStream(Package(manifest, new byte[9]))))
            check(Throws<InvalidDataException>(() => archive.ReadTile(1, 1, 1)), "a non-PNG payload is rejected instead of being displayed as a tile");
        using (var archive = new OfflineTileArchive(new MemoryStream(Package(manifest, new byte[2_097_153]))))
            check(Throws<InvalidDataException>(() => archive.ReadTile(1, 1, 1)), "an oversized decompressed tile is rejected before reading its payload");

        int opens = 0;
        Task<Stream> Open() { opens++; return Task.FromResult<Stream>(new MemoryStream(fixture)); }
        var copies = new DerivedMapCache(Path.Combine(fixtures, "cache"));
        string path = await OfflineMapInstaller.InstallAsync(copies.Root, Open, default, digest, fixture.Length);
        check(opens == 1 && File.ReadAllBytes(path).SequenceEqual(fixture), "installation creates an exact verified copy only in the dedicated map-cache root");
        await OfflineMapInstaller.InstallAsync(copies.Root, Open, default, digest, fixture.Length);
        check(opens == 1, "an intact map copy is reused without opening another package stream");
        File.WriteAllBytes(path, new byte[] { 1 });
        await OfflineMapInstaller.InstallAsync(copies.Root, Open, default, digest, fixture.Length);
        check(opens == 2 && File.ReadAllBytes(path).SequenceEqual(fixture), "a corrupt local map copy is replaced only after verifying the new payload");
        var bad = fixture.ToArray(); bad[^1] ^= 1;
        check(await ThrowsAsync<InvalidDataException>(() => OfflineMapInstaller.InstallAsync(copies.Root, () => Task.FromResult<Stream>(new MemoryStream(bad)), default, "bad hash", bad.Length)),
            "a wrong package checksum is surfaced as an installation failure");
        check(File.ReadAllBytes(path).SequenceEqual(fixture) && !Directory.GetFiles(copies.Root, "*.tmp").Any(),
            "a failed replacement preserves the previous copy and removes only its own temporary file");
        check(await ThrowsAsync<InvalidDataException>(() => OfflineMapInstaller.InstallAsync(Path.Combine(fixtures, "oversized"), Open, default, digest, fixture.Length - 1)),
            "installation bounds package bytes before publishing an oversized copy");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel(); int before = opens;
            check(await ThrowsAsync<OperationCanceledException>(() => OfflineMapInstaller.InstallAsync(Path.Combine(fixtures, "cancelled"), Open, cancel.Token, digest, fixture.Length)) && opens == before,
                "a cancelled page cannot open or install a package");
        }

        string privateFile = Path.Combine(fixtures, "cache/pending-photo.jpg"); File.WriteAllText(privateFile, "keep");
        using (var cache = new OfflineMapCache(copies, Open, digest, fixture.Length))
        {
            await cache.PrepareAsync();
            check((await cache.ReadTileAsync(1, 1, 1, default))!.SequenceEqual(png), "the shared cache reader uses the verified archive");
            check(await cache.CoversAsync(1, -1, 1, default) && !await cache.CoversAsync(-1, 1, 1, default), "the shared cache exposes actual tile coverage");
            var cleared = await cache.ClearAsync();
            check(cleared.DeletedBytes == fixture.Length && cleared.Remaining.Bytes == 0 && !File.Exists(path),
                "coordinated clearing closes an open archive before removing its replaceable copy");
            check(File.ReadAllText(privateFile) == "keep", "archive clearing preserves a pending photo beside the dedicated map-cache directory");
            int before = opens;
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                check(await ThrowsAsync<OperationCanceledException>(() => cache.ReadTileAsync(1, 1, 1, cancel.Token)) && opens == before && !File.Exists(path),
                    "a stale tile request cannot reinstall a cleared archive");
            }
            await cache.PrepareAsync();
            check(opens == before + 1 && File.Exists(path), "the next enabled map preparation recreates the verified replaceable copy");
            await cache.ClearAsync();
        }
        var disposed = new OfflineMapCache(copies, Open, digest, fixture.Length); disposed.Dispose();
        check(await ThrowsAsync<ObjectDisposedException>(() => disposed.PrepareAsync()), "a disposed cache cannot reopen package files");

        await OwnershipAsync(check);
    }

    private static async Task OwnershipAsync(Action<bool, string> check)
    {
        var requests = new List<TaskCompletionSource<IReadOnlyList<Layer>>>();
        var tokens = new List<CancellationToken>();
        var states = new List<OfflineDisplayState>();
        var route = new Layer("dev route"); var marker = new Layer("person pin");
        var first = new List<Layer> { route, marker }; var second = new List<Layer> { route, marker };
        Task<IReadOnlyList<Layer>> Load(CancellationToken token)
        {
            tokens.Add(token); var request = new TaskCompletionSource<IReadOnlyList<Layer>>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Add(request); return request.Task;
        }
        var session = new OfflineOverlaySession<List<Layer>, Layer>(Load, (map, layers) => map.InsertRange(0, layers),
            (map, layers) => { foreach (var layer in layers) map.Remove(layer); }, layer => layer.Disposed = true, states.Add);
        var pending = session.ActivateAsync(first, true);
        check(session.State == OfflineDisplayState.Loading && requests.Count == 1, "enabled activation starts one cancellable detail load");
        await session.RefreshAsync(false);
        check(tokens[0].IsCancellationRequested && session.State == OfflineDisplayState.Disabled && first.SequenceEqual(new[] { route, marker }),
            "turning detail Off cancels a pending load while retaining dev route and person overlays");
        var stale = new Layer("stale detail"); requests[0].SetResult(new[] { stale }); await pending;
        check(stale.Disposed && !first.Contains(stale) && session.State == OfflineDisplayState.Disabled, "a late load cannot reattach detail after the switch is Off");
        pending = session.RefreshAsync(true);
        var water = new Layer("water"); var tiles = new Layer("tiles"); requests[1].SetResult(new[] { tiles, water }); await pending;
        check(first.SequenceEqual(new[] { tiles, water, route, marker }) && session.State == OfflineDisplayState.Ready, "ready detail stays below retained dev route and person overlays");
        pending = session.ActivateAsync(second, true);
        check(tiles.Disposed && water.Disposed && first.SequenceEqual(new[] { route, marker }) && !route.Disposed && !marker.Disposed,
            "switching map owners removes and disposes only optional layers");
        session.Invalidate(); var old = new Layer("cleared request"); requests[2].SetResult(new[] { old }); await pending;
        check(old.Disposed && second.SequenceEqual(new[] { route, marker }) && session.State == OfflineDisplayState.Cleared && requests.Count == 3,
            "cache invalidation cancels pending layers and does not immediately reinstall them");
        pending = session.RefreshAsync(true); requests[3].SetException(new IOException("missing package")); await pending;
        check(session.State == OfflineDisplayState.Unavailable && second.SequenceEqual(new[] { route, marker }), "failed detail loading leaves basic map overlays intact and reports unavailable");
        pending = session.RefreshAsync(true); session.Deactivate(); var hidden = new Layer("hidden request"); requests[4].SetResult(new[] { hidden }); await pending;
        check(hidden.Disposed && session.State == OfflineDisplayState.Inactive && session.Map is null && !route.Disposed && !marker.Disposed,
            "a hidden page cannot attach delayed map detail or dispose another module's overlays");
        await session.RefreshAsync(true);
        check(requests.Count == 5, "inactive detail owners do not load or reinstall an archive");
        var partial = new Layer("partial");
        var failing = new OfflineOverlaySession<List<Layer>, Layer>(_ => Task.FromResult<IReadOnlyList<Layer>>(new[] { partial }),
            (map, layers) => { map.Add(layers[0]); throw new IOException("attach failure"); }, (map, layers) => { foreach (var layer in layers) map.Remove(layer); },
            layer => layer.Disposed = true, _ => { });
        await failing.ActivateAsync(first, true);
        check(partial.Disposed && !first.Contains(partial) && failing.State == OfflineDisplayState.Unavailable,
            "partial attachment failure rolls back only its own optional layers");
    }

    private sealed class Layer(string name) { public string Name { get; } = name; public bool Disposed; }
    private static bool Throws<T>(Action action) where T : Exception { try { action(); return false; } catch (T) { return true; } }
    private static async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); return false; } catch (T) { return true; } }
    private static byte[] Package(OfflineMapManifest manifest, byte[] png)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using (var metadata = archive.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(metadata, manifest);
            using (var tile = archive.CreateEntry("tiles/1/1/1.png").Open()) tile.Write(png);
        }
        return stream.ToArray();
    }
}
