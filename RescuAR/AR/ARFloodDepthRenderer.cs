using Evergine.Common.Graphics;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Materials;
using RescuAR.Diagnostics;
using System;

namespace RescuAR.AR;

/// <summary>
/// Draw-thread state holder for Flood Depth Visualization V7.
///
/// V6's 60 m transparent Evergine cube was metrically correct, but the live
/// camera is only a flat background inside Evergine. As a result, the cube was
/// alpha-blended over desks, chairs, monitors, and other real objects that are
/// physically above/in front of the water. That is what made 0.10 m appear
/// waist-high.
///
/// V7 deliberately renders NO Evergine water mesh. The visible flood is now
/// composited by the MAUI FloodDepthOcclusionView from ARCore's per-frame depth
/// map, so only real scene pixels that are actually below the selected world
/// water height receive the flood tint.
///
/// This class keeps the existing public renderer contract intact so
/// ARCameraSpatialController and the feature-state/continuity logic do not need
/// to be rewritten.
/// </summary>
public static class ARFloodDepthRenderer
{
    private const string LogTag =
        "RescuAR-FloodDepth";

    public const string FloodRootEntityName =
        "ARFloodDepthRoot";

    private const float MinimumRenderableDepthMeters =
        0.01f;

    private static readonly object sync =
        new();

    private static Entity? activeRoot;

    private static long boundGraphicsGeneration;

    private static long appliedVersion =
        -1;

    private static float appliedDepthMeters;

    public static long AppliedVersion
    {
        get
        {
            lock (sync)
            {
                return appliedVersion;
            }
        }
    }

    public static float AppliedDepthMeters
    {
        get
        {
            lock (sync)
            {
                return appliedDepthMeters;
            }
        }
    }

    public static Entity Create(
        Material sourceMaterial,
        RenderLayerDescription alphaDoubleSidedLayer)
    {
        ArgumentNullException.ThrowIfNull(
            sourceMaterial);

        ArgumentNullException.ThrowIfNull(
            alphaDoubleSidedLayer);

        Entity root =
            new()
            {
                Name = FloodRootEntityName,
                IsEnabled = false
            };

        root.AddComponent(
            new Transform3D());

        lock (sync)
        {
            activeRoot = root;
            boundGraphicsGeneration =
                ARRenderGenerationBridge.Current.GraphicsGeneration;
            appliedVersion = -1;
            appliedDepthMeters = 0.0f;
        }

        AndroidLog.Info(
            LogTag,
            "AR flood-depth V7 renderer created: " +
            "Evergine flood mesh SUPPRESSED; visible flood uses " +
            "ARCore depth-aware MAUI occlusion compositor.");

        return root;
    }

    /// <summary>
    /// Tracks bridge availability/depth on the Evergine draw thread without
    /// creating any visible geometry. Returning true for a valid flood keeps
    /// the existing spatial/continuity state machine authoritative while the
    /// visible composition is performed by FloodDepthOcclusionView.
    /// </summary>
    public static bool ProcessDrawThreadWork(
        Entity floodRoot)
    {
        ArgumentNullException.ThrowIfNull(
            floodRoot);

        lock (sync)
        {
            if (!ReferenceEquals(
                    activeRoot,
                    floodRoot) ||
                !ARRenderGenerationBridge.IsCurrentGraphics(
                    boundGraphicsGeneration))
            {
                return false;
            }
        }

        ARFloodDepthBridge.FloodDepthSnapshot snapshot =
            ARFloodDepthBridge.Current;

        if (!ARRenderGenerationBridge.IsCurrentSession(
                snapshot.Generation))
        {
            floodRoot.IsEnabled = false;
            appliedDepthMeters = 0.0f;
            ARFloodDepthBridge.AcknowledgeDrawThreadVersion(
                snapshot.Version);
            return false;
        }

        if (snapshot.Version == appliedVersion)
        {
            return snapshot.IsAvailable &&
                appliedDepthMeters >=
                    MinimumRenderableDepthMeters;
        }

        appliedVersion =
            snapshot.Version;

        if (!snapshot.IsAvailable ||
            !float.IsFinite(snapshot.LocalDepthMeters) ||
            snapshot.LocalDepthMeters <
                MinimumRenderableDepthMeters)
        {
            appliedDepthMeters =
                0.0f;

            AndroidLog.Debug(
                LogTag,
                "AR flood-depth V7 renderer cleared/disabled: " +
                $"version={snapshot.Version}; " +
                "no Evergine water mesh is rendered.");

            ARFloodDepthBridge.AcknowledgeDrawThreadVersion(
                snapshot.Version);

            return false;
        }

        appliedDepthMeters =
            Math.Clamp(
                snapshot.LocalDepthMeters,
                MinimumRenderableDepthMeters,
                ARFloodDepthBridge.MaximumSupportedDepthMeters);

        AndroidLog.Info(
            LogTag,
            "AR FLOOD DEPTH V7 STATE APPLIED: " +
            $"version={snapshot.Version}, " +
            $"depth={appliedDepthMeters:F2} m, " +
            "mode=ARCORE_DEPTH_OCCLUSION, " +
            "evergineFloodMesh=False, " +
            $"source='{snapshot.Source}'.");

        ARFloodDepthBridge.AcknowledgeDrawThreadVersion(
            snapshot.Version);

        return true;
    }

    public static void TeardownGraphicsGeneration(
        long graphicsGeneration)
    {
        lock (sync)
        {
            if (boundGraphicsGeneration != graphicsGeneration)
            {
                return;
            }

            if (activeRoot is not null)
            {
                activeRoot.IsEnabled = false;
            }

            activeRoot = null;
            boundGraphicsGeneration = 0;
            appliedVersion = -1;
            appliedDepthMeters = 0.0f;
        }
    }
}
