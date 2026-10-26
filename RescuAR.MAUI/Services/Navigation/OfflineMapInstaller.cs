using System.Security.Cryptography;

namespace RescuAR.MAUI.Services.Navigation;

public static class OfflineMapInstaller
{
    public const string PackageName = "offline-map-tiles.zip";
    public const long PackageSize = 93_661_024;
    public const string PackageHash = "1168d7528af49cf0a6d3d6befdbbdfa1a8e3bc2f350ea6ce943928ed07e62b12";

    private static void Guard(string directory, string target)
    {
        string root = Path.GetFullPath(directory);
        if (Path.GetDirectoryName(Path.GetFullPath(target)) != root) throw new IOException("The map copy is outside its reserved cache directory.");
        if ((Directory.Exists(root) && (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) ||
            (File.Exists(target) && (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("Map installation cannot follow a linked cache directory or file.");
    }
    public static async Task<string> InstallAsync(string directory, Func<Task<Stream>> openPackage, CancellationToken token = default,
        string expectedHash = PackageHash, long expectedSize = PackageSize)
    {
        token.ThrowIfCancellationRequested();
        string root = Path.GetFullPath(directory), target = Path.Combine(Path.GetFullPath(directory), PackageName);
        Guard(root, target); Directory.CreateDirectory(root); Guard(root, target);
        if (File.Exists(target) && await Valid(target, expectedHash, expectedSize, token).ConfigureAwait(false)) return target;
        string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var input = await openPackage().ConfigureAwait(false))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                byte[] buffer = new byte[81920]; long bytes = 0; int count;
                while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                {
                    bytes += count;
                    if (bytes > expectedSize) throw new InvalidDataException("The map package exceeds its pinned size.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                }
            }
            if (!await Valid(temporary, expectedHash, expectedSize, token).ConfigureAwait(false)) throw new InvalidDataException("The map package failed its integrity check.");
            token.ThrowIfCancellationRequested(); Guard(root, target); Guard(root, temporary);
            File.Move(temporary, target, true);
            return target;
        }
        finally { if (File.Exists(temporary)) { Guard(root, temporary); File.Delete(temporary); } }
    }
    private static async Task<bool> Valid(string path, string hash, long size, CancellationToken token)
    {
        if (new FileInfo(path).Length != size) return false;
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}
