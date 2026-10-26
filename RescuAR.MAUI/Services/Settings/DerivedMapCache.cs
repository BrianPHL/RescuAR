namespace RescuAR.MAUI.Services.Settings;

public sealed record MapCacheSnapshot(long Bytes, int UnavailableFiles, int SkippedLinks);
public sealed record MapCacheClearResult(long DeletedBytes, MapCacheSnapshot Remaining);

// Only this dedicated, replaceable map-copy directory is eligible. No general cache sweep.
public sealed class DerivedMapCache
{
    public string Root { get; }
    public DerivedMapCache(string appCacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(appCacheDirectory)) throw new ArgumentException("An app cache directory is required.");
        Root = Path.GetFullPath(Path.Combine(appCacheDirectory, "rescuar-map-cache"));
    }
    public MapCacheSnapshot Inspect()
    {
        var files = Enumerate(out int unavailable, out int skipped);
        long bytes = 0;
        foreach (string file in files)
        {
            try { bytes += new FileInfo(file).Length; } catch (IOException) { unavailable++; } catch (UnauthorizedAccessException) { unavailable++; }
        }
        return new(bytes, unavailable, skipped);
    }
    public MapCacheClearResult Clear()
    {
        var files = Enumerate(out _, out _);
        long removed = 0;
        foreach (string file in files)
        {
            try
            {
                if (!IsSafeFile(file)) continue;
                long size = new FileInfo(file).Length;
                File.Delete(file);
                removed += size;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return new(removed, Inspect());
    }
    private bool IsOwned(string path) => Path.GetFullPath(path).StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private bool IsSafeFile(string path)
    {
        if (!IsOwned(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return false;
        var directory = Directory.GetParent(path);
        while (directory is not null && (directory.FullName == Root || IsOwned(directory.FullName)))
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return false;
            if (directory.FullName == Root) return true;
            directory = directory.Parent;
        }
        return false;
    }
    private List<string> Enumerate(out int unavailable, out int skipped)
    {
        unavailable = skipped = 0;
        var result = new List<string>();
        if (!Directory.Exists(Root)) return result;
        var pending = new Stack<string>(); pending.Push(Root);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            try
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    if (!IsOwned(entry)) continue;
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; continue; }
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else result.Add(entry);
                }
            }
            catch (IOException) { unavailable++; }
            catch (UnauthorizedAccessException) { unavailable++; }
        }
        return result;
    }
}
