using Evergine.Components.Graphics3D;
using Evergine.Framework;
using RescuAR.AR;
using System;
using System.Diagnostics;

namespace RescuAR.AR;

/// <summary>
/// Evergine-side behavior that assigns the ARCore camera texture to a
/// BackgroundImage component.
///
/// Keep this behavior and BackgroundImage on the same Entity.
/// </summary>
public sealed class ARCameraBackgroundBehavior : Behavior
{
    private const string Tag =
        "RescuAR-Background";

    private BackgroundImage? backgroundImage;
    private long appliedVersion = -1;

    protected override bool OnAttached()
    {
        if (!base.OnAttached())
        {
            return false;
        }

        backgroundImage =
            Owner.FindComponent<BackgroundImage>();

        if (backgroundImage is null)
        {
            Debug.WriteLine(
            $"[{Tag}] ARCameraBackgroundBehavior requires a " +
            "BackgroundImage component on the same Entity.");

            return false;
        }

        Debug.WriteLine(
            $"[{Tag}] AR camera BackgroundImage behavior attached.");

        return true;
    }

    protected override void Update(
        TimeSpan gameTime)
    {
        if (backgroundImage is null)
        {
            return;
        }

        long bridgeVersion =
            ARCameraTextureBridge.Version;

        if (bridgeVersion ==
            appliedVersion)
        {
            return;
        }

        var texture =
            ARCameraTextureBridge.CurrentTexture;

        if (texture is null)
        {
            /*
             * Do not force the component to null during startup unless it
             * previously had an AR camera texture.
             */
            appliedVersion =
                bridgeVersion;

            return;
        }

        backgroundImage.Texture =
            texture;

        appliedVersion =
            bridgeVersion;

        Debug.WriteLine(
            $"[{Tag}] AR camera texture applied to BackgroundImage. " +
            $"NativePointer=0x{texture.NativePointer:X}");
    }

    protected override void OnDetached()
    {
        backgroundImage =
            null;

        base.OnDetached();
    }
}