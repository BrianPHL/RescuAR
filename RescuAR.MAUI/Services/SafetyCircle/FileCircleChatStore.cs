using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RescuAR.App.Services.SafetyCircle;

public sealed class FileCircleChatStore(string directory) : ICircleChatStore
{
    private string Root => Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
    public string DirectoryFor(CircleChatScope scope) => Path.Combine(Root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope.Account.Server + "\n" + scope.Account.UserId + "\n" + scope.CircleId))));
    private string StatePath(CircleChatScope scope) => Path.Combine(DirectoryFor(scope), "state.json");
    private void Guard(string path)
    {
        path = Path.GetFullPath(path);
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The chat path is outside its account storage.");
        for (string? part = path; part is not null && part.Length >= Root.Length; part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && (File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Chat storage cannot follow a linked account folder or file.");
    }
    public CircleChatState? Read(CircleChatScope scope)
    {
        string path = StatePath(scope); Guard(path);
        if (!File.Exists(path)) return null;
        try
        {
            var state = JsonConvert.DeserializeObject<CircleChatState>(File.ReadAllText(path));
            if (state is null || state.Server != scope.Account.Server || state.UserId != scope.Account.UserId || state.CircleId != scope.CircleId)
                throw new IOException("The chat file has a different account, server or Circle owner.");
            return state;
        }
        catch (JsonException error) { throw new IOException("The saved chat file is invalid. Its existing contents have been preserved.", error); }
    }
    public void Write(CircleChatScope scope, CircleChatState state)
    {
        if (state.Server != scope.Account.Server || state.UserId != scope.Account.UserId || state.CircleId != scope.CircleId)
            throw new UnauthorizedAccessException("Chat storage ownership does not match.");
        string path = StatePath(scope); Guard(path);
        Directory.CreateDirectory(DirectoryFor(scope)); Guard(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonConvert.SerializeObject(state)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) { Guard(temporary); File.Delete(temporary); } }
    }
    public bool Owns(CircleChatScope scope, ChatFile file)
    {
        try
        {
            if (file.Kind is not ("Image" or "Video" or "LocalPhoto")) return false;
            string expected = Path.Combine(DirectoryFor(scope), file.Kind == "LocalPhoto" ? "photos" : "media");
            string path = Path.GetFullPath(file.Path); Guard(path);
            return string.Equals(Path.GetDirectoryName(path), expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }
    public async Task<ChatFile> ImportAsync(CircleChatScope scope, Stream stream, string name, string kind, CancellationToken token)
    {
        if (kind is not ("Image" or "Video" or "LocalPhoto")) throw new ArgumentException("Choose a photo or video.");
        name = Path.GetFileName(name);
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension.Length > 10 || extension.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character))) throw new ArgumentException("The selected filename is unsupported.");
        string folder = Path.Combine(DirectoryFor(scope), kind == "LocalPhoto" ? "photos" : "media");
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + extension);
        string temporary = path + ".partial"; Guard(path);
        Directory.CreateDirectory(folder); Guard(path);
        long limit = (kind == "Video" ? 50L : 10L) * 1024 * 1024;
        try
        {
            long bytes = 0;
            await using (var output = File.Create(temporary))
            {
                byte[] buffer = new byte[81920]; int count;
                while ((count = await stream.ReadAsync(buffer, token)) > 0)
                {
                    bytes += count;
                    if (bytes > limit) throw new CircleChatInputException(kind == "Video" ? "Choose a video up to 50 MB." : "Choose a photo up to 10 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                }
            }
            token.ThrowIfCancellationRequested();
            if (bytes == 0) throw new CircleChatInputException("The selected file is empty.");
            Guard(path); File.Move(temporary, path);
            return new(path, name, kind, bytes);
        }
        finally { if (File.Exists(temporary)) { Guard(temporary); File.Delete(temporary); } }
    }
}
