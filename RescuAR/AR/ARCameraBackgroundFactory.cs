using Evergine.Components.Graphics3D;
using Evergine.Framework;
using RescuAR.AR;

namespace RescuAR.AR;

/// <summary>
/// Creates the scene entity that renders the live ARCore camera texture as
/// an Evergine background.
///
/// UniformToFill is intentional for the first visual checkpoint: it fills the
/// display while preserving the camera image aspect ratio. Orientation and
/// ARCore display-geometry UV correction are handled in the next milestone.
/// </summary>
public static class ARCameraBackgroundFactory
{
    public const string EntityName =
        "ARCameraBackground";

    public static Entity Create()
    {
        BackgroundImage background =
            new()
            {
                Stretch =
                    BackgroundImage.StretchType.UniformToFill,

                OrderBias =
                    int.MinValue
            };

        return new Entity()
        {
            Name =
                EntityName
        }
        .AddComponent(
            background)
        .AddComponent(
            new ARCameraBackgroundBehavior());
    }
}
