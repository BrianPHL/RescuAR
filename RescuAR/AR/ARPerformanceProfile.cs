#if RESCUAR_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using RescuAR.Diagnostics;

namespace RescuAR.AR;

/// <summary>Bounded, location-free samples from the Android AR producer and draw owner.</summary>
public static class ARPerformanceProfile
{
    private const string Tag = "RescuAR-ARProfile";
    private const int BatchSize = 120;
    private static readonly string[] metricNames =
    [
        "presentInterval", "cameraGap", "frameWork", "sceneDrawPresent", "nativeImport",
        "conversionObserved", "firstCameraFrame", "firstRouteOverlay",
    ];
    private static readonly object sync = new();
    private static readonly string capture = Guid.NewGuid().ToString("N");
    private static readonly long[,] samples = new long[metricNames.Length, BatchSize];
    private static readonly int[] sampleCounts = new int[metricNames.Length];
    private static long currentRun;
    private static long lastRun;
    private static long sequence;
    private static long session;
    private static long graphics;
    private static long startedAt;
    private static long startedAtMs;
    private static long lastPresentedAt;
    private static long lastCameraImage;
    private static long lastCameraAt;
    private static bool routeOverlayRecorded;

    public enum Metric
    {
        PresentInterval,
        CameraGap,
        FrameWork,
        SceneDrawPresent,
        NativeImport,
        ConversionObserved,
        FirstCameraFrame,
        FirstRouteOverlay,
    }

    public static long CurrentRun => Volatile.Read(ref currentRun);

    public static long BeginRun(long sessionGeneration, long graphicsGeneration)
    {
        if (sessionGeneration <= 0 || graphicsGeneration <= 0) return 0;

        lock (sync)
        {
            EndRunLocked();
            session = sessionGeneration;
            graphics = graphicsGeneration;
            sequence = 0;
            startedAt = Stopwatch.GetTimestamp();
            startedAtMs = Environment.TickCount64;
            lastPresentedAt = lastCameraImage = lastCameraAt = 0;
            routeOverlayRecorded = false;
            Volatile.Write(ref currentRun, ++lastRun);
            WriteRecord(BeginRecord("start", startedAtMs));
            return currentRun;
        }
    }

    public static void EndRun(long run)
    {
        lock (sync)
        {
            if (run == currentRun) EndRunLocked();
        }
    }

    private static void EndRunLocked()
    {
        if (currentRun == 0) return;
        long now = Environment.TickCount64;
        FlushTimings(now);
        WriteRecord(BeginRecord("end", now));
        Volatile.Write(ref currentRun, 0);
    }

    public static void RecordElapsed(long run, Metric metric, long fromTimestamp)
    {
        if (run == 0) return;
        long toTimestamp = Stopwatch.GetTimestamp();
        lock (sync)
        {
            if (run == currentRun) AddTiming(metric, fromTimestamp, toTimestamp);
        }
    }

    public static void RecordPresentation(long run, in ARFrameMetadata camera, long now)
    {
        if (run == 0 || !camera.IsValid) return;
        long presentedAt = Stopwatch.GetTimestamp();
        lock (sync)
        {
            if (run != currentRun || camera.Generation.SessionGeneration != session ||
                camera.Generation.GraphicsGeneration != graphics) return;

            if (lastPresentedAt == 0) AddTiming(Metric.FirstCameraFrame, startedAt, presentedAt);
            else AddTiming(Metric.PresentInterval, lastPresentedAt, presentedAt);
            lastPresentedAt = presentedAt;

            // Repeated draws of one texture count as presents but not new camera images.
            if (camera.FrameTimestamp > lastCameraImage)
            {
                if (lastCameraAt != 0) AddTiming(Metric.CameraGap, lastCameraAt, presentedAt);
                lastCameraImage = camera.FrameTimestamp;
                lastCameraAt = presentedAt;
            }

            if (!routeOverlayRecorded)
            {
                // Existing clipped route bounds approximate viewport coverage.
                // They do not identify which pixels the device displayed.
                var visible = ARRouteRenderer.Visibility;
                if (visible.RootEnabled && visible.Session == session && visible.MeasuredAt >= startedAtMs &&
                    now - visible.MeasuredAt is >= 0 and <= 1_000 &&
                    visible.Placement.IsAvailable &&
                    (visible.RibbonBounds.Segments > 0 || visible.ArrowBounds.Segments > 0))
                {
                    routeOverlayRecorded = true;
                    AddTiming(Metric.FirstRouteOverlay, startedAt, presentedAt);
                }
            }
        }
    }

    public static void RecordSystem(ARPowerThermalPolicy.AndroidThermalSeverity thermal,
        float batteryCelsius, bool powerSaver)
    {
        long run = CurrentRun;
        if (run == 0) return;
        long managedBytes = GC.GetTotalMemory(forceFullCollection: false);
        long managedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        long now = Environment.TickCount64;
        lock (sync)
        {
            if (run != currentRun) return;
            FlushTimings(now);
            var record = BeginRecord("system", now);
            record.Append(FormattableString.Invariant($",\"managedBytes\":{managedBytes},\"managedAllocatedBytes\":{managedAllocatedBytes},\"gen0\":{GC.CollectionCount(0)},\"gen1\":{GC.CollectionCount(1)},\"gen2\":{GC.CollectionCount(2)},\"thermal\":\"{thermal}\",\"batteryCelsius\":"));
            record.Append(float.IsFinite(batteryCelsius) ? batteryCelsius.ToString("R", CultureInfo.InvariantCulture) : "null");
            record.Append(powerSaver ? ",\"powerSaver\":true" : ",\"powerSaver\":false");
            WriteRecord(record);
        }
    }

    private static void AddTiming(Metric metric, long fromTimestamp, long toTimestamp)
    {
        if (fromTimestamp <= 0 || toTimestamp < fromTimestamp) return;
        int index = (int)metric;
        int count = sampleCounts[index];
        samples[index, count] = Stopwatch.GetElapsedTime(fromTimestamp, toTimestamp).Ticks / 10;
        sampleCounts[index] = ++count;
        if (count == BatchSize) Flush(metric, Environment.TickCount64);
    }

    private static void FlushTimings(long now)
    {
        for (int index = 0; index < sampleCounts.Length; index++)
            if (sampleCounts[index] > 0) Flush((Metric)index, now);
    }

    private static void Flush(Metric metric, long now)
    {
        int index = (int)metric;
        var record = BeginRecord("timings", now);
        record.Append(",\"metric\":\"").Append(metricNames[index]).Append("\",\"samplesUs\":[");
        for (int i = 0; i < sampleCounts[index]; i++)
        {
            if (i != 0) record.Append(',');
            record.Append(samples[index, i].ToString(CultureInfo.InvariantCulture));
        }
        record.Append(']');
        WriteRecord(record);
        sampleCounts[index] = 0;
    }

    private static StringBuilder BeginRecord(string kind, long now) => new(
        FormattableString.Invariant($"AR_PROFILE_V1 {{\"kind\":\"{kind}\",\"capture\":\"{capture}\",\"session\":{session},\"graphics\":{graphics},\"run\":{currentRun},\"sequence\":{++sequence},\"monotonicMs\":{now}"));

    private static void WriteRecord(StringBuilder record) => AndroidLog.Info(Tag, record.Append('}').ToString());
}
#endif
