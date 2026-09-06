using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Materials;
using Evergine.Mathematics;
using RescuAR.Diagnostics;
using System;

namespace RescuAR.AR;

/// <summary>
/// Evergine draw-thread renderer for local AR-space flood depth.
///
/// The renderer creates one reusable transparent water volume and one thin
/// water-surface slab. Their vertical dimensions are expressed directly in
/// meters because the existing ARCore -> Evergine spatial bridge already uses
/// ARCore meter coordinates.
///
/// World placement is owned by ARCameraSpatialController so the water root is
/// evaluated from the SAME frame-coherent ARCore camera/ground-anchor snapshot
/// used by the rest of the AR scene.
/// </summary>
public static class ARFloodDepthRenderer
{
    private const string LogTag =
        "RescuAR-FloodDepth";

    public const string FloodRootEntityName =
        "ARFloodDepthRoot";

    private const float MinimumRenderableDepthMeters =
        0.01f;

    private const float WaterSurfaceThicknessMeters =
        0.018f;

    private static readonly object sync =
        new();

    private static Entity? activeRoot;
    private static Entity? volumeEntity;
    private static Transform3D? volumeTransform;
    private static Entity? surfaceEntity;
    private static Transform3D? surfaceTransform;

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

        Material volumeMaterial =
            CreateWaterMaterial(
                sourceMaterial,
                alphaDoubleSidedLayer,
                alpha: 0.18f,
                brightnessScale: 0.72f);

        Material surfaceMaterial =
            CreateWaterMaterial(
                sourceMaterial,
                alphaDoubleSidedLayer,
                alpha: 0.42f,
                brightnessScale: 1.00f);

        Transform3D rootTransform =
            new();

        Entity root =
            new()
            {
                Name =
                    FloodRootEntityName,

                IsEnabled =
                    false
            };

        root.AddComponent(
            rootTransform);

        (Entity volume, Transform3D volumeTx) =
            CreateCube(
                "ARFloodDepthVolume",
                volumeMaterial);

        (Entity surface, Transform3D surfaceTx) =
            CreateCube(
                "ARFloodDepthSurface",
                surfaceMaterial);

        root.AddChild(
            volume);

        root.AddChild(
            surface);

        lock (sync)
        {
            activeRoot =
                root;

            volumeEntity =
                volume;

            volumeTransform =
                volumeTx;

            surfaceEntity =
                surface;

            surfaceTransform =
                surfaceTx;

            appliedVersion =
                -1;

            appliedDepthMeters =
                0.0f;
        }

        AndroidLog.Debug(
            LogTag,
            "AR flood-depth renderer created: transparent water volume + " +
            "surface, AlphaDoubleSided layer, initially disabled.");

        return root;
    }

    /// <summary>
    /// Applies the newest bridge depth to the reusable flood geometry.
    /// Must be called from the Evergine draw thread.
    ///
    /// Returns true when a positive local depth is available.
    /// </summary>
    public static bool ProcessDrawThreadWork(
        Entity floodRoot)
    {
        ArgumentNullException.ThrowIfNull(
            floodRoot);

        Transform3D? volumeTx;
        Transform3D? surfaceTx;
        Entity? volume;
        Entity? surface;

        lock (sync)
        {
            if (!ReferenceEquals(
                    activeRoot,
                    floodRoot))
            {
                return false;
            }

            volumeTx =
                volumeTransform;

            surfaceTx =
                surfaceTransform;

            volume =
                volumeEntity;

            surface =
                surfaceEntity;
        }

        if (volumeTx is null ||
            surfaceTx is null ||
            volume is null ||
            surface is null)
        {
            return false;
        }

        ARFloodDepthBridge.FloodDepthSnapshot snapshot =
            ARFloodDepthBridge.Current;

        if (snapshot.Version ==
            appliedVersion)
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
            volume.IsEnabled =
                false;

            surface.IsEnabled =
                false;

            appliedDepthMeters =
                0.0f;

            AndroidLog.Debug(
                LogTag,
                "AR flood-depth renderer cleared/disabled: " +
                $"version={snapshot.Version}.");

            return false;
        }

        float depth =
            Math.Clamp(
                snapshot.LocalDepthMeters,
                MinimumRenderableDepthMeters,
                ARFloodDepthBridge.MaximumSupportedDepthMeters);

        float extent =
            Math.Clamp(
                snapshot.HorizontalExtentMeters,
                2.0f,
                ARFloodDepthBridge.DefaultHorizontalExtentMeters);

        /*
         * Root local Y=0 is the detected ground plane.
         *
         * Water volume spans:
         *     groundY .. groundY + depth
         *
         * CubeMesh is centered, so its center must be depth / 2.
         */
        volumeTx.LocalPosition =
            new Vector3(
                0.0f,
                depth * 0.5f,
                0.0f);

        volumeTx.LocalRotation =
            new Vector3(0.0f, 0.0f, 0.0f);

        volumeTx.LocalScale =
            new Vector3(
                extent,
                depth,
                extent);

        /*
         * A slightly more opaque thin slab makes the actual water surface
         * readable in perspective. Its top surface is positioned at the exact
         * requested local depth above the detected ground plane.
         */
        surfaceTx.LocalPosition =
            new Vector3(
                0.0f,
                depth +
                    (WaterSurfaceThicknessMeters * 0.5f),
                0.0f);

        surfaceTx.LocalRotation =
            new Vector3(0.0f, 0.0f, 0.0f);

        surfaceTx.LocalScale =
            new Vector3(
                extent,
                WaterSurfaceThicknessMeters,
                extent);

        volume.IsEnabled =
            true;

        surface.IsEnabled =
            true;

        appliedDepthMeters =
            depth;

        AndroidLog.Info(
            LogTag,
            "AR FLOOD DEPTH GEOMETRY APPLIED: " +
            $"version={snapshot.Version}, " +
            $"depth={depth:F2} m, " +
            $"surfaceLocalY={depth:F2} m, " +
            $"extent={extent:F1} x {extent:F1} m, " +
            $"source='{snapshot.Source}'.");

        return true;
    }

    private static Material CreateWaterMaterial(
        Material sourceMaterial,
        RenderLayerDescription alphaDoubleSidedLayer,
        float alpha,
        float brightnessScale)
    {
        Material material =
            sourceMaterial.Clone();

        float red =
            0.02f *
            brightnessScale;

        float green =
            0.62f *
            brightnessScale;

        float blue =
            0.92f *
            brightnessScale;

        StandardMaterial standard =
            new(material)
            {
                BaseColorLinear =
                    new LinearColor
                    {
                        A =
                            alpha,

                        AsVector3 =
                            new Vector3(
                                red,
                                green,
                                blue)
                    },

                Alpha =
                    alpha,

                LightingEnabled =
                    false,

                IBLEnabled =
                    false,

                Metallic =
                    0.0f,

                Roughness =
                    0.18f,

                LayerDescription =
                    alphaDoubleSidedLayer
            };

        return standard.Material;
    }

    private static (Entity Entity, Transform3D Transform) CreateCube(
        string name,
        Material material)
    {
        Transform3D transform =
            new()
            {
                LocalPosition =
                    new Vector3(0.0f, 0.0f, 0.0f),

                LocalRotation =
                    new Vector3(0.0f, 0.0f, 0.0f),

                LocalScale =
                    new Vector3(1.0f, 1.0f, 1.0f)
            };

        Entity entity =
            new Entity()
            {
                Name =
                    name,

                IsEnabled =
                    false
            }
            .AddComponent(
                transform)
            .AddComponent(
                new MaterialComponent
                {
                    Material =
                        material,

                    UseCopy =
                        false,

                    AsignedTo =
                        "Default"
                })
            .AddComponent(
                new CubeMesh
                {
                    Size =
                        1.0f
                })
            .AddComponent(
                new MeshRenderer
                {
                    IsCullingEnabled =
                        false,

                    IsEnabled =
                        true
                });

        return (
            entity,
            transform);
    }
}
