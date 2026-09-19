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
    private bool hasAppliedTexture;
    private long boundGraphicsGeneration;

    private static readonly object activeSync = new();
    private static ARCameraBackgroundBehavior? activeBehavior;

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

        boundGraphicsGeneration =
            ARRenderGenerationBridge.Current.GraphicsGeneration;

        lock (activeSync)
        {
            activeBehavior = this;
        }

        return true;
    }

    protected override void Update(
        TimeSpan gameTime)
    {
        if (backgroundImage is null)
        {
            return;
        }

        ARCameraTextureBridge.TextureSnapshot snapshot =
            ARCameraTextureBridge.Current;

        long bridgeVersion =
            snapshot.Version;

        var texture =
            snapshot.Texture;

        if (texture is null)
        {
            if (hasAppliedTexture)
            {
                backgroundImage.Texture = null;
                hasAppliedTexture = false;
            }

            appliedVersion =
                bridgeVersion;

            return;
        }

        if (bridgeVersion ==
            appliedVersion)
        {
            return;
        }

        backgroundImage.Texture =
            texture;

        hasAppliedTexture = true;

        appliedVersion =
            bridgeVersion;

        Debug.WriteLine(
            $"[{Tag}] AR camera texture applied to BackgroundImage. " +
            $"NativePointer=0x{texture.NativePointer:X}");
    }

    protected override void OnDetached()
    {
        lock (activeSync)
        {
            if (ReferenceEquals(activeBehavior, this))
            {
                activeBehavior = null;
            }
        }

        if (backgroundImage is not null &&
            hasAppliedTexture)
        {
            backgroundImage.Texture = null;
        }

        hasAppliedTexture = false;
        backgroundImage =
            null;
        base.OnDetached();
    }

    public static void TeardownGraphicsGeneration(
        long graphicsGeneration)
    {
        ARCameraBackgroundBehavior? behavior;

        lock (activeSync)
        {
            behavior = activeBehavior;

            if (behavior is null ||
                behavior.boundGraphicsGeneration != graphicsGeneration)
            {
                return;
            }

            activeBehavior = null;
        }

        if (behavior.backgroundImage is not null &&
            behavior.hasAppliedTexture)
        {
            behavior.backgroundImage.Texture = null;
        }

        behavior.hasAppliedTexture = false;
        behavior.appliedVersion = -1;
        behavior.boundGraphicsGeneration = 0;
    }
}
