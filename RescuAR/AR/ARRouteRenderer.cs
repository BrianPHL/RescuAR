using Evergine.Common.Graphics;
using Evergine.Components.Graphics3D;
using Evergine.Framework;
using Evergine.Framework.Graphics;
using Evergine.Framework.Graphics.Materials;
using Evergine.Mathematics;
using System;

namespace RescuAR.AR;

/// <summary>
/// Milestone 3 hardcoded AR route renderer.
///
/// This deliberately uses a few thin CubeMesh entities instead of introducing
/// GeoJSON/routing/PDR yet. The route is authored in a small local coordinate
/// frame whose origin is positioned on the detected ARCore ground anchor by
/// ARCameraSpatialController.
/// </summary>
public static class ARRouteRenderer
{
    public const string RouteRootEntityName =
        "ARRouteRoot";

    private const float RouteWidthMeters =
        0.40f;

    private const float RouteThicknessMeters =
        0.03f;

    /*
     * Hardcoded local route for Milestone 3:
     *
     * P0 = (0.0, 0.0,  0.0)
     * P1 = (0.0, 0.0, -1.5)
     * P2 = (0.8, 0.0, -3.0)
     * P3 = (0.8, 0.0, -5.0)
     *
     * This gives us a straight section followed by a visible bend.
     */
    private static readonly Vector3[] RoutePoints =
    {
        new(0.0f, 0.0f,  0.0f),
        new(0.0f, 0.0f, -1.5f),
        new(0.8f, 0.0f, -3.0f),
        new(0.8f, 0.0f, -5.0f)
    };

    /// <summary>
    /// Creates the complete route hierarchy using a clone of the capsule's
    /// already-working material. Cloning preserves the material's render layer
    /// and effect configuration, avoiding a second rendering-path experiment.
    /// </summary>
    public static Entity Create(
        Material sourceMaterial)
    {
        ArgumentNullException.ThrowIfNull(
            sourceMaterial);

        Material routeMaterial =
            sourceMaterial.Clone();

        /*
         * The supplied Evergine StandardMaterial API exposes BaseColorLinear,
         * so no assumptions about Evergine.Common.Graphics.Color constructors
         * are necessary here.
         *
         * Approximate intended UI cyan:
         * RGB ~= (0, 210, 235).
         */
        StandardMaterial standardMaterial =
            new(routeMaterial)
            {
                BaseColorLinear =
                    new LinearColor
                    {
                        A = 1.0f,
                        AsVector3 =
                            new Vector3(
                                0.0f,
                                0.82f,
                                0.92f)
                    },

                LightingEnabled =
                    false,

                IBLEnabled =
                    false,

                Metallic =
                    0.0f,

                Roughness =
                    1.0f
            };

        Entity routeRoot =
            new()
            {
                Name =
                    RouteRootEntityName,

                /*
                 * Do not render the serialized/local route until a valid
                 * ARCore ground anchor is available.
                 */
                IsEnabled =
                    false
            };

        routeRoot.AddComponent(
            new Transform3D());

        for (int i = 0;
             i < RoutePoints.Length - 1;
             i++)
        {
            Entity segment =
                CreateSegment(
                    i,
                    RoutePoints[i],
                    RoutePoints[i + 1],
                    standardMaterial.Material);

            routeRoot.AddChild(
                segment);
        }

        return routeRoot;
    }

    private static Entity CreateSegment(
        int index,
        Vector3 start,
        Vector3 end,
        Material material)
    {
        Vector3 delta =
            end -
            start;

        float horizontalLength =
            MathF.Sqrt(
                delta.X * delta.X +
                delta.Z * delta.Z);

        if (horizontalLength <=
            float.Epsilon)
        {
            throw new InvalidOperationException(
                $"Route segment {index} has zero horizontal length.");
        }

        Vector3 midpoint =
            (start + end) *
            0.5f;

        /*
         * CubeMesh's unit cube is centered at the origin. Stretch its local Z
         * axis to the route-segment length, local X to ribbon width, and local
         * Y to a very small thickness.
         *
         * The cube is symmetric along its length, so a 180-degree yaw is
         * visually equivalent. atan2(X, Z) therefore gives all segment/bend
         * orientations needed for this validation route.
         */
        float yaw =
            MathF.Atan2(
                delta.X,
                delta.Z);

        Transform3D transform =
            new()
            {
                LocalPosition =
                    midpoint,

                LocalRotation =
                    new Vector3(
                        0.0f,
                        yaw,
                        0.0f),

                LocalScale =
                    new Vector3(
                        RouteWidthMeters,
                        RouteThicknessMeters,
                        horizontalLength)
            };

        return new Entity()
        {
            Name =
                $"ARRouteSegment_{index}"
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
    }
}
