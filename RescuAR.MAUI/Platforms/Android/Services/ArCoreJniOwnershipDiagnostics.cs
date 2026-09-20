using Android.Util;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Narrow, non-owning diagnostics for Java/native wrapper boundaries. This
/// class deliberately does not call Dispose, Close, DeleteLocalRef, or change
/// handle ownership. Field logs must first identify the exact high-volume
/// wrapper and call site before disposal semantics are corrected.
/// </summary>
internal static class ArCoreJniOwnershipDiagnostics
{
    private const string LogTag = "RescuAR-JNI";
    private const long SummaryIntervalMilliseconds = 10_000;

    private static readonly object sync = new();
    private static readonly Dictionary<BoundaryKey, long> totals = new();
    private static readonly Dictionary<BoundaryKey, long> intervalCounts = new();
    private static long lastSummaryTimestamp = Environment.TickCount64;

    public static void Record(
        string wrapperType,
        string callSite,
        string boundary)
    {
        BoundaryKey key =
            new(
                wrapperType,
                callSite,
                boundary);

        string? summary = null;

        lock (sync)
        {
            totals[key] = totals.TryGetValue(key, out long total)
                ? total + 1
                : 1;

            intervalCounts[key] = intervalCounts.TryGetValue(
                key,
                out long intervalTotal)
                    ? intervalTotal + 1
                    : 1;

            long now = Environment.TickCount64;

            if (now - lastSummaryTimestamp >=
                SummaryIntervalMilliseconds)
            {
                summary = string.Join(
                    ", ",
                    intervalCounts
                        .OrderByDescending(item => item.Value)
                        .Select(item =>
                            $"{item.Key.WrapperType}|" +
                            $"{item.Key.CallSite}|" +
                            $"{item.Key.Boundary}:" +
                            $"{item.Value}/" +
                            $"{totals[item.Key]}"));

                intervalCounts.Clear();
                lastSummaryTimestamp = now;
            }
        }

        if (!string.IsNullOrEmpty(summary))
        {
            Log.Info(
                LogTag,
                "ARCORE_JNI_OWNERSHIP_METRICS " +
                "format=type|callsite|boundary:interval/total; " +
                summary);
        }
    }

    private readonly record struct BoundaryKey(
        string WrapperType,
        string CallSite,
        string Boundary);
}
