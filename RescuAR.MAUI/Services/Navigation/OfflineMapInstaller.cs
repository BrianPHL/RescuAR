using System.Security.Cryptography;

namespace RescuAR.MAUI.Services.Navigation;

/// <summary>Atomically installs only the map package. Never touches account data or outboxes.</summary>
public static class OfflineMapInstaller
{
    public const string PackageName = "offline-map-tiles.zip";
    public const long PackageSize = 93_661_024;
    public const string PackageHash = "1168d7528af49cf0a6d3d6befdbbdfa1a8e3bc2f350ea6ce943928ed07e62b12";

    public static async Task<string> InstallAsync(string directory, Func<Task<Stream>> openPackage,
        CancellationToken token = default, string expectedHash = PackageHash, long expectedSize = PackageSize)
    {
        Directory.CreateDirectory(directory);
        string target = Path.Combine(directory, PackageName);
        if (File.Exists(target) && await IsValidAsync(target, expectedHash, expectedSize, token).ConfigureAwait(false)) return target;
        string temporary = Path.Combine(directory, $"{PackageName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (Stream source = await openPackage().ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await source.CopyToAsync(output, token).ConfigureAwait(false);
            if (!await IsValidAsync(temporary, expectedHash, expectedSize, token).ConfigureAwait(false))
                throw new InvalidDataException("The packaged offline map failed its integrity check.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
            return target;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<bool> IsValidAsync(string path, string hash, long size, CancellationToken token)
    {
        if (new FileInfo(path).Length != size) return false;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var actual = await SHA256.HashDataAsync(input, token).ConfigureAwait(false);
        return Convert.ToHexString(actual).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}
