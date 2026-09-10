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
/// It loads the embedded ROADS.geojson and POINTS.geojson files from the
/// RescuAR assembly, parses them with the real loader, builds the pedestrian
/// road graph, and publishes a compact diagnostic result.
///
/// This does not modify any AR camera/rendering state.
/// </summary>
public static class NavigationDataBootstrap
{
    private const string RoadsResourceSuffix =
        ".Navigation.Data.Resources.ROADS.geojson";

    private const string PointsResourceSuffix =
        ".Navigation.Data.Resources.POINTS.geojson";

    private static readonly object sync =
        new();

    private static Task<NavigationRuntimeValidationResult>? validationTask;

    private static RoadGraph? cachedRoadGraph;

    public static NavigationRuntimeValidationResult? LastResult { get; private set; }

    /// <summary>
    /// Returns the one in-memory pedestrian RoadGraph built from the embedded
    /// ROADS.geojson dataset. The existing one-time validation task owns graph
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

    public static Task<NavigationRuntimeValidationResult> ValidateOnceAsync()
    {
        lock (sync)
        {
            validationTask ??=
                ValidateWithDiagnosticsAsync();

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

        Assembly assembly =
            typeof(NavigationDataBootstrap)
                .Assembly;

        string roadsResourceName =
            FindResourceName(
                assembly,
                RoadsResourceSuffix);

        string pointsResourceName =
            FindResourceName(
                assembly,
                PointsResourceSuffix);

        GeoJsonRoadLoader loader =
            new();

        IReadOnlyList<GeoJsonRoadFeature> roads;

        IReadOnlyList<GeoJsonPointFeature> points;

        using (Stream roadsStream =
               assembly.GetManifestResourceStream(
                   roadsResourceName)
               ?? throw new InvalidOperationException(
                   $"Embedded resource not found: {roadsResourceName}"))
        {
            roads =
                await loader.LoadRoadsAsync(
                    roadsStream);
        }

        long roadsParsedMilliseconds =
            stopwatch.ElapsedMilliseconds;

        using (Stream pointsStream =
               assembly.GetManifestResourceStream(
                   pointsResourceName)
               ?? throw new InvalidOperationException(
                   $"Embedded resource not found: {pointsResourceName}"))
        {
            points =
                await loader.LoadPointsAsync(
                    pointsStream);
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
                roads);

        graphStopwatch.Stop();

        lock (sync)
        {
            cachedRoadGraph =
                graph;
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

    private static string FindResourceName(
        Assembly assembly,
        string suffix)
    {
        string? name =
            assembly
                .GetManifestResourceNames()
                .FirstOrDefault(
                    candidate =>
                        candidate.EndsWith(
                            suffix,
                            StringComparison.Ordinal));

        if (name is null)
        {
            string available =
                string.Join(
                    Environment.NewLine,
                    assembly.GetManifestResourceNames());

            throw new InvalidOperationException(
                $"Could not find embedded resource ending in '{suffix}'." +
                Environment.NewLine +
                "Available resources:" +
                Environment.NewLine +
                available);
        }

        return name;
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
            "Expected reference counts = roads 7198, accepted 5432, " +
            "points 8995, nodes 17408, directed edges 39402" +
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
