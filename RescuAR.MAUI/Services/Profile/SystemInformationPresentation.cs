using System.Globalization;

namespace RescuAR.App.Services.Profile;

internal static class SystemInformationPresentation
{
    public static string Version(string? version, string? build) => string.IsNullOrWhiteSpace(version) ? "Unavailable" :
        $"v{version}" + (string.IsNullOrWhiteSpace(build) ? string.Empty : $" (build {build})");
    public static string Updated(long? milliseconds)
    {
        if (milliseconds is null or <= 0) return "Unavailable";
        try { return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds.Value).ToLocalTime().ToString("g", CultureInfo.CurrentCulture); }
        catch (ArgumentOutOfRangeException) { return "Unavailable"; }
    }
}
