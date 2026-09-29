using System;
using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Materials;
using Evergine.Mathematics;
using RescuAR.Diagnostics;

namespace RescuAR.AR;

/// <summary>Three reusable arrow parts changed only on the engine draw thread.</summary>
public static class ARRoadApproachRenderer
{
    private static Entity? root;
    private static Transform3D? transform;
    private static Transform3D[] parts = [];
    private static long graphicsGeneration;
    private static bool visible;
    private static System.Numerics.Vector2 lastDirection;

    public static Entity Create(Material source)
    {
        var material = new StandardMaterial(source.Clone())
        {
            BaseColorLinear = new LinearColor { A = 1, AsVector3 = new Vector3(0, 0.82f, 0.92f) },
            LightingEnabled = false, IBLEnabled = false, Metallic = 0, Roughness = 1
        };
        root = new Entity { Name = "ARRoadApproachArrow", IsEnabled = false };
        transform = new Transform3D();
        root.AddComponent(transform);
        parts = new Transform3D[3];
        for (int i = 0; i < parts.Length; i++)
        {
            var part = new Entity { Name = $"ARRoadApproachArrow_{i}" };
            parts[i] = new Transform3D();
            part.AddComponent(parts[i]);
            part.AddComponent(new MaterialComponent { Material = material.Material,
                UseCopy = false, AsignedTo = "Default" });
            part.AddComponent(new CubeMesh { Size = 1 });
            part.AddComponent(new MeshRenderer { IsCullingEnabled = false, IsEnabled = true });
            root.AddChild(part);
        }
        graphicsGeneration = ARRenderGenerationBridge.Current.GraphicsGeneration;
        visible = false;
        lastDirection = new(float.NaN, float.NaN);
        return root;
    }

    public static bool ProcessDrawThreadWork(ARCameraPoseBridge.SpatialSnapshot frame,
        bool floorPlausible, bool diagnosticsActive)
    {
        if (root is null || transform is null || parts.Length != 3) return false;
        var cue = ARRoadApproachBridge.GetForFrame(frame);
        bool show = cue.Active && floorPlausible && !diagnosticsActive &&
            ARRenderGenerationBridge.IsCurrentGraphics(graphicsGeneration);
        root.IsEnabled = show;
        if (show)
        {
            transform.Position = new Vector3(frame.Pose.PositionX,
                frame.Anchor.PositionY + 0.035f, frame.Pose.PositionZ);
            if (lastDirection != cue.Direction)
            {
                var geometry = RoadApproachArrowGeometry.Create(cue.Direction);
                show = geometry.Length == parts.Length;
                root.IsEnabled = show;
                if (show)
                {
                    for (int i = 0; i < geometry.Length; i++)
                    {
                        var a = geometry[i].Start;
                        var b = geometry[i].End;
                        var delta = b - a;
                        parts[i].LocalPosition = new Vector3((a.X + b.X) / 2, 0, (a.Y + b.Y) / 2);
                        parts[i].LocalScale = new Vector3(i == 0 ? 0.22f : 0.26f, 0.04f, delta.Length());
                        parts[i].LocalRotation = new Vector3(0,
                            geometry[i].YawRadians, 0);
                    }
                    lastDirection = cue.Direction;
                }
            }
        }
        if (visible != show)
        {
            visible = show;
            AndroidLog.Info("RescuAR-ARRoute", $"AR ROAD APPROACH ARROW: visible={show}, session={frame.Generation.SessionGeneration}.");
        }
        return show;
    }

    public static void TeardownGraphicsGeneration(long generation)
    {
        if (graphicsGeneration != generation) return;
        if (root is not null) root.IsEnabled = false;
        root = null;
        transform = null;
        parts = [];
        graphicsGeneration = 0;
        visible = false;
        ARRoadApproachBridge.Clear();
    }
}
