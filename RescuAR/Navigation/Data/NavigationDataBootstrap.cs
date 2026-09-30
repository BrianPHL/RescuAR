using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Data;

/// <summary>
/// One-time Milestone 4 runtime validator.
///
/// It loads all embedded primary and regional road/point GeoJSON files from
/// the RescuAR assembly, parses them with the real loader, builds the pedestrian
/// road graph, and publishes a compact diagnostic result.
///
/// This does not modify any AR camera/rendering state.
/// </summary>
public static class NavigationDataBootstrap
{
    public const string GeoJsonResourcePrefix =
        "RescuAR.Navigation.Data.Resources.";

    private static readonly object sync =
        new();

    private static Task<NavigationRuntimeValidationResult>? validationTask;

    private static RoadGraph? cachedRoadGraph;
    private static IReadOnlyList<GeoJsonRoadFeature>? cachedRoadFeatures;

    public static NavigationRuntimeValidationResult? LastResult { get; private set; }

    /// <summary>Primary data first, then every named regional dataset.</summary>
    public static string[] GetGeoJsonResourceNames(string kind)
    {
        if (kind is not ("ROADS" or "POINTS"))
            throw new ArgumentException("Expected ROADS or POINTS.", nameof(kind));

        string primary = GeoJsonResourcePrefix + kind + ".geojson";
        string regionalSuffix = "_" + kind + ".geojson";

        return typeof(NavigationDataBootstrap).Assembly
            .GetManifestResourceNames()
            .Where(name =>
                name.StartsWith(GeoJsonResourcePrefix, StringComparison.Ordinal) &&
                (name.Equals(primary, StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith(regionalSuffix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(name =>
                name.Equals(primary, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    public static Stream OpenGeoJsonResource(string resourceName) =>
        typeof(NavigationDataBootstrap).Assembly
            .GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException(
            $"Embedded GeoJSON not found: {resourceName}");

    /// <summary>
    /// Returns the one in-memory pedestrian RoadGraph built from the embedded
    /// primary and regional datasets. The one-time validation task owns graph
    /// creation, so offline routing never reparses or rebuilds the dataset.
    /// </summary>
    public static async Task<RoadGraph> GetRoadGraphAsync(
        CancellationToken cancellationToken = default)
    {
        Task<NavigationRuntimeValidationResult> validation =
            ValidateOnceAsync();

        await validation.WaitAsync(
            cancellationToken);

        lock (sync)
        {
            return cachedRoadGraph ??
                throw new InvalidOperationException(
                    "Navigation data validation completed without publishing the RoadGraph.");
        }
    }

    /// <summary>Raw embedded lines, including those excluded from routing.</summary>
    public static async Task<IReadOnlyList<GeoJsonRoadFeature>> GetRoadFeaturesAsync(
        CancellationToken cancellationToken = default)
    {
        await ValidateOnceAsync().WaitAsync(cancellationToken);
        lock (sync)
        {
            return cachedRoadFeatures ??
                throw new InvalidOperationException("Road GeoJSON datasets were not loaded.");
        }
    }

    public static Task<NavigationRuntimeValidationResult> ValidateOnceAsync()
    {
        lock (sync)
        {
            // Parsing the road datasets and building their graph must not
            // resume on the MAUI UI thread after an awaited stream read.
            validationTask ??=
                Task.Run(ValidateWithDiagnosticsAsync);

            return validationTask;
        }
    }

    private static async Task<NavigationRuntimeValidationResult> ValidateWithDiagnosticsAsync()
    {
        try
        {
            return await ValidateCoreAsync();
        }
        catch (Exception ex)
        {
            string message =
                "RescuAR Navigation Data Validation FAILED:" +
                Environment.NewLine +
                ex;

            Console.WriteLine(
                message);

            Trace.WriteLine(
                message);

            WriteAndroidLog(
                "Error",
                message);

            throw;
        }
    }

    private static async Task<NavigationRuntimeValidationResult> ValidateCoreAsync()
    {
        Stopwatch stopwatch =
            Stopwatch.StartNew();

        GeoJsonRoadLoader loader =
            new();
        List<GeoJsonRoadFeature> roads = new();
        List<GeoJsonPointFeature> points = new();

        foreach (string resourceName in GetGeoJsonResourceNames("ROADS"))
        {
            using Stream stream = OpenGeoJsonResource(resourceName);
            roads.AddRange(await loader.LoadRoadsAsync(stream));
        }

        if (roads.Count == 0)
            throw new InvalidOperationException(
                "No usable road GeoJSON datasets were included.");

        long roadsParsedMilliseconds =
            stopwatch.ElapsedMilliseconds;

        foreach (string resourceName in GetGeoJsonResourceNames("POINTS"))
        {
            using Stream stream = OpenGeoJsonResource(resourceName);
            points.AddRange(await loader.LoadPointsAsync(stream));
        }

        long pointsParsedMilliseconds =
            stopwatch.ElapsedMilliseconds -
            roadsParsedMilliseconds;

        PedestrianRoadFilter filter =
            new();

        int acceptedRoadFeatureCount =
            roads.Count(
                filter.IsWalkable);

        RoadGraphBuilder graphBuilder =
            new(
                filter);

        Stopwatch graphStopwatch =
            Stopwatch.StartNew();

        RoadGraph graph =
            graphBuilder.Build(
                roads,
                points);

        graphStopwatch.Stop();

        lock (sync)
        {
            cachedRoadGraph =
                graph;
            cachedRoadFeatures = roads;
        }

        NavigationRuntimeValidationResult result =
            new(
                roads.Count,
                acceptedRoadFeatureCount,
                points.Count,
                graph.Nodes.Count,
                graph.Edges.Count,
                roadsParsedMilliseconds,
                pointsParsedMilliseconds,
                graphStopwatch.ElapsedMilliseconds,
                stopwatch.ElapsedMilliseconds);

        LastResult =
            result;

        WriteDiagnostic(
            result);

        return result;
    }

    private static void WriteDiagnostic(
        NavigationRuntimeValidationResult result)
    {
        string message =
            Environment.NewLine +
            "========== RescuAR Navigation Data Validation ==========" +
            Environment.NewLine +
            $"ROADS parsed             = {result.SourceRoadFeatureCount}" +
            Environment.NewLine +
            $"ROADS pedestrian accepted= {result.AcceptedRoadFeatureCount}" +
            Environment.NewLine +
            $"POINTS parsed            = {result.PointFeatureCount}" +
            Environment.NewLine +
            $"Graph nodes              = {result.GraphNodeCount}" +
            Environment.NewLine +
            $"Graph directed edges     = {result.DirectedEdgeCount}" +
            Environment.NewLine +
            $"ROADS parse time         = {result.RoadsParseMilliseconds} ms" +
            Environment.NewLine +
            $"POINTS parse time        = {result.PointsParseMilliseconds} ms" +
            Environment.NewLine +
            $"Graph build time         = {result.GraphBuildMilliseconds} ms" +
            Environment.NewLine +
            $"Total validation time    = {result.TotalMilliseconds} ms" +
            Environment.NewLine +
            "========================================================";

        /*
         * Keep both outputs for the diagnostic build:
         * - Console.WriteLine is generally surfaced by the Android/.NET host.
         * - Trace.WriteLine remains visible under attached debugging.
         */
        Console.WriteLine(
            message);

        Trace.WriteLine(
            message);

        WriteAndroidLog(
            "Debug",
            message);
    }

    /// <summary>
    /// Writes to Android Logcat without adding a compile-time dependency on
    /// Mono.Android. RescuAR targets net8.0, so directly referencing
    /// Android.Util.Log here would break the shared project build.
    ///
    /// When this assembly runs inside the Android MAUI process, Mono.Android
    /// is already loaded and Android.Util.Log can be resolved by reflection.
    /// On non-Android hosts this method simply does nothing.
    /// </summary>
    private static void WriteAndroidLog(
        string level,
        string message)
    {
        const string Tag =
            "RescuAR-Navigation";

        try
        {
            Type? logType =
                AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(
                        assembly =>
                            assembly.GetType(
                                "Android.Util.Log",
                                throwOnError: false))
                    .FirstOrDefault(
                        type =>
                            type is not null);

            if (logType is null)
            {
                return;
            }

            MethodInfo? method =
                logType.GetMethod(
                    level,
                    BindingFlags.Public |
                    BindingFlags.Static,
                    binder: null,
                    types:
                    [
                        typeof(string),
                        typeof(string)
                    ],
                    modifiers: null);

            method?.Invoke(
                null,
                [
                    Tag,
                    message
                ]);
        }
        catch
        {
            /*
             * Diagnostics must never interfere with app startup or navigation
             * data validation.
             */
        }
    }
}

public sealed record NavigationRuntimeValidationResult(
    int SourceRoadFeatureCount,
    int AcceptedRoadFeatureCount,
    int PointFeatureCount,
    int GraphNodeCount,
    int DirectedEdgeCount,
    long RoadsParseMilliseconds,
    long PointsParseMilliseconds,
    long GraphBuildMilliseconds,
    long TotalMilliseconds);
