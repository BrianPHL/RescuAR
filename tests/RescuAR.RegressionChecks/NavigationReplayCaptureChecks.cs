using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RescuAR.Diagnostics;

internal static class NavigationReplayCaptureChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string capture = "0123456789abcdef0123456789abcdef";
        var utc = DateTimeOffset.Parse("2026-11-05T09:00:00.1234567+00:00", CultureInfo.InvariantCulture);
        const double latitude = 14.123456789012345;
        const double longitude = 121.98765432109876;
        var lines = NavigationReplayCodec.Encode(capture, 1, "location.raw", utc, 123456,
            new { latitude, longitude, unavailable = double.NaN, infinite = double.PositiveInfinity,
                acquisitionElapsedRealtimeNanoseconds = 1234567890123456L });
        var bytes = Decode(lines);
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        check(root.GetProperty("schema").GetString() == NavigationReplayCodec.Schema &&
            root.GetProperty("version").GetInt32() == 1 && root.GetProperty("captureId").GetString() == capture &&
            root.GetProperty("sequence").GetInt64() == 1 && root.GetProperty("kind").GetString() == "location.raw",
            "replay envelopes retain versioned capture and event identity");
        check(root.GetProperty("utc").GetDateTimeOffset() == utc &&
            root.GetProperty("monotonicMs").GetInt64() == 123456 &&
            root.GetProperty("data").GetProperty("acquisitionElapsedRealtimeNanoseconds").GetInt64() == 1234567890123456L,
            "replay preserves UTC fractions and integer monotonic/native timestamps");
        check(root.GetProperty("data").GetProperty("latitude").GetDouble() == latitude &&
            root.GetProperty("data").GetProperty("longitude").GetDouble() == longitude,
            "exact replay coordinates round-trip as doubles without location quantization");
        check(root.GetProperty("data").GetProperty("unavailable").GetString() == "NaN" &&
            root.GetProperty("data").GetProperty("infinite").GetString() == "Infinity",
            "unavailable policy values serialize without dropping an observation");
        var multipart = NavigationReplayCodec.Encode(capture, 2, "route.geometry", utc, 123457,
            new { routeLabel = new string('路', 4000), samples = Enumerable.Range(0, 300).ToArray() });
        using var multipartJson = JsonDocument.Parse(Decode(multipart));
        check(multipart.Count > 1 && multipart.All(line => Encoding.UTF8.GetByteCount(line) < 3500) &&
            multipartJson.RootElement.GetProperty("data").GetProperty("routeLabel").GetString() == new string('路', 4000),
            "Unicode and large route payloads use bounded ASCII Logcat chunks and reconstruct intact");
        check(Throws<ArgumentException>(() => NavigationReplayCodec.Encode(capture, 3, "route.geometry", utc, 1,
            new { points = new string('x', NavigationReplayCodec.MaximumRecordBytes) })),
            "oversized replay records are rejected before emitting partial chunks");
        check(Throws<ArgumentException>(() => NavigationReplayCodec.Encode("invalid", 1, "location.raw", utc, 1, new { })) &&
            Throws<ArgumentException>(() => NavigationReplayCodec.Encode(capture, 0, "location.raw", utc, 1, new { })),
            "replay transport rejects invalid capture and sequence identities");
        DiagnosticPrivacyPolicy.SetLocationLoggingConsent(false);
#if RESCUAR_DIAGNOSTICS
            check(NavigationReplayCapture.IsEnabled && DiagnosticPrivacyPolicy.HasLocationLoggingConsent &&
                DiagnosticPrivacyPolicy.FormatCoordinate(latitude, longitude) ==
                    string.Create(CultureInfo.InvariantCulture, $"({latitude:R},{longitude:R})"),
                "ordinary diagnostic field capture retains exact coordinates without a consent prompt");
#else
            DiagnosticPrivacyPolicy.SetLocationLoggingConsent(true);
            check(!NavigationReplayCapture.IsEnabled && !DiagnosticPrivacyPolicy.HasLocationLoggingConsent &&
                DiagnosticPrivacyPolicy.FormatCoordinate(latitude, longitude) == "<redacted-location>",
                "production builds cannot enable exact location diagnostics through the compatibility consent API");
#endif
    }

    private static byte[] Decode(IReadOnlyList<string> lines)
    {
        string[] first = lines[0].Split(' ');
        byte[] bytes = Convert.FromBase64String(string.Concat(lines.Select(line => line.Split(' ')[5])));
        if (Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != first[4])
            throw new InvalidOperationException("Replay checksum did not match its reconstructed payload.");
        return bytes;
    }

    private static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }
}
