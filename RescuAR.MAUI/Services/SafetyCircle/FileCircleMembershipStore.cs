using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace RescuAR.App.Services.SafetyCircle;

public sealed class FileCircleMembershipStore(string directory) : ICircleMembershipStore
{
    public string PathFor(CircleMembershipScope scope)
    {
        string root = Path.GetFullPath(directory);
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope.Server + "\n" + scope.UserId)));
        string path = Path.GetFullPath(Path.Combine(root, key + ".json"));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("The Circle cache path is outside its account-storage directory.");
        return path;
    }
    public CircleMembershipState? Read(CircleMembershipScope scope)
    {
        string path = PathFor(scope);
        if (!File.Exists(path)) return null;
        try
        {
            var state = JsonConvert.DeserializeObject<CircleMembershipState>(File.ReadAllText(path));
            if (state is null || state.Server != scope.Server || state.UserId != scope.UserId || state.Circles is null)
                throw new IOException("The saved Circle list is invalid or belongs to another account/server.");
            return state;
        }
        catch (JsonException error) { throw new IOException("The saved Circle list could not be read. Its existing file has been preserved.", error); }
    }
    public void Write(CircleMembershipScope scope, CircleMembershipState state)
    {
        if (state.Server != scope.Server || state.UserId != scope.UserId) throw new UnauthorizedAccessException("Circle cache ownership does not match.");
        string path = PathFor(scope);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonConvert.SerializeObject(state)); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
