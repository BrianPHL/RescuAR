using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RescuAR.Diagnostics;

/// <summary>Exact diagnostic inputs for local navigation-policy replay.
/// Production call sites are removed by Conditional, independently of consent UI.</summary>
public static class NavigationReplayCapture
{
    private static readonly object sync = new();
    private static readonly string captureId = Guid.NewGuid().ToString("N");
    private static long sequence;
    private static long writtenRecords;
    private static long droppedRecords;
    public static bool IsEnabled => DiagnosticPrivacyPolicy.IsDiagnosticBuild;

    [Conditional("RESCUAR_DIAGNOSTICS")]
    public static void Record<T>(string kind, T data, long? monotonicMilliseconds = null)
    {
        if (!IsEnabled) return;
        lock (sync)
        {
            if (sequence == 0)
                Write("capture_start", new { exactCoordinates = true, coordinateSystem = "WGS84",
                    clock = "Environment.TickCount64", retentionDays = DiagnosticPrivacyPolicy.MaximumRetentionDays },
                    Environment.TickCount64);
            Write(kind, data, monotonicMilliseconds ?? Environment.TickCount64);
            if (sequence % 100 == 0)
                Write("capture_checkpoint", new { writtenRecords, droppedRecords, throughSequence = sequence },
                    Environment.TickCount64);
        }
    }

    private static void Write<T>(string kind, T data, long monotonicMilliseconds)
    {
        long next = ++sequence;
        try
        {
            foreach (string line in NavigationReplayCodec.Encode(captureId, next, kind,
                DateTimeOffset.UtcNow, monotonicMilliseconds, data))
                AndroidLog.Info("RescuAR-Replay", line);
            writtenRecords++;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            droppedRecords++;
            // Explicitly preserve sequence/record failure evidence. Diagnostic
            // serialization must never interrupt location acquisition/navigation.
            AndroidLog.Warn("RescuAR-Replay", $"NAV_REPLAY_DROP {captureId} {next} {exception.GetType().Name}");
        }
    }
}

/// <summary>Bounded ASCII transport, safe below Android's Logcat message limit.</summary>
public static class NavigationReplayCodec
{
    public const string Schema = "rescuar.navigation-replay";
    public const int Version = 1;
    public const int MaximumRecordBytes = 256 * 1024;
    public const int ChunkCharacters = 3000;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    public static IReadOnlyList<string> Encode<T>(string captureId, long sequence, string kind,
        DateTimeOffset utc, long monotonicMs, T data)
    {
        if (!Guid.TryParseExact(captureId, "N", out _) || sequence < 1 ||
            string.IsNullOrWhiteSpace(kind) || kind.Length > 64)
            throw new ArgumentException("Invalid navigation replay envelope identity.");
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            schema = Schema, version = Version, captureId, sequence, kind, utc,
            monotonicMs, data
        }, JsonOptions);
        if (bytes.Length > MaximumRecordBytes)
            throw new ArgumentException("Replay record exceeds the 256 KiB limit; split route geometry into batches.");
        string digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        string encoded = Convert.ToBase64String(bytes);
        int parts = (encoded.Length + ChunkCharacters - 1) / ChunkCharacters;
        var lines = new List<string>(parts);
        for (int part = 0; part < parts; part++)
        {
            int offset = part * ChunkCharacters;
            lines.Add($"NAV_REPLAY_V1 {captureId} {sequence} {part + 1}/{parts} {digest} " +
                encoded.Substring(offset, Math.Min(ChunkCharacters, encoded.Length - offset)));
        }
        return lines;
    }
}
