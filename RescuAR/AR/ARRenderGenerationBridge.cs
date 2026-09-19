using System;
using System.Threading;
using RescuAR.Diagnostics;

namespace RescuAR.AR;

/// <summary>
/// Identifies the ARCore session, Vulkan graphics context, and applied display
/// geometry that produced one render snapshot.
/// </summary>
public readonly record struct ARRenderGenerationToken(
    long SessionGeneration,
    long GraphicsGeneration,
    long GeometryGeneration)
{
    public static ARRenderGenerationToken Invalid =>
        new(0, 0, 0);

    public bool IsValid =>
        SessionGeneration > 0 &&
        GraphicsGeneration > 0 &&
        GeometryGeneration > 0;
}

/// <summary>
/// Process-wide generation authority for the static AR publication bridges.
/// Android owns activation/suspension; consumers use it to reject snapshots
/// from a previous session, graphics context, or display geometry.
/// </summary>
public static class ARRenderGenerationBridge
{
    private const string LogTag = "RescuAR-ARGeneration";
    private static readonly object sync = new();

    private static ARRenderGenerationToken current =
        ARRenderGenerationToken.Invalid;

    private static bool acceptingFrames;
    private static long rejectedCallbackCount;

    public static long RejectedCallbackCount =>
        Interlocked.Read(ref rejectedCallbackCount);

    public static ARRenderGenerationToken Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public static bool IsAcceptingFrames
    {
        get
        {
            lock (sync)
            {
                return acceptingFrames;
            }
        }
    }

    public static void Activate(
        ARRenderGenerationToken token)
    {
        lock (sync)
        {
            current = token;
            acceptingFrames = token.IsValid;
        }
    }

    public static void RegisterGraphicsContext(
        long graphicsGeneration)
    {
        if (graphicsGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(graphicsGeneration));
        }

        lock (sync)
        {
            current = new ARRenderGenerationToken(
                current.SessionGeneration,
                graphicsGeneration,
                current.GeometryGeneration);
            acceptingFrames = false;
        }
    }

    public static void Suspend(
        long sessionGeneration,
        long graphicsGeneration)
    {
        lock (sync)
        {
            if (current.SessionGeneration == sessionGeneration &&
                current.GraphicsGeneration == graphicsGeneration)
            {
                acceptingFrames = false;
            }
        }
    }

    public static void Invalidate()
    {
        lock (sync)
        {
            current = ARRenderGenerationToken.Invalid;
            acceptingFrames = false;
        }
    }

    public static bool IsCurrent(
        ARRenderGenerationToken token,
        bool requireGeometry = true)
    {
        lock (sync)
        {
            return acceptingFrames &&
                token.SessionGeneration == current.SessionGeneration &&
                token.GraphicsGeneration == current.GraphicsGeneration &&
                (!requireGeometry ||
                 token.GeometryGeneration == current.GeometryGeneration);
        }
    }

    public static bool TryAcceptCallback(
        ARRenderGenerationToken token,
        string callbackName)
    {
        if (IsCurrent(token))
        {
            return true;
        }

        long rejected =
            Interlocked.Increment(ref rejectedCallbackCount);

        if (rejected == 1 ||
            rejected % 25 == 0)
        {
            ARRenderGenerationToken active = Current;

            AndroidLog.Warn(
                LogTag,
                "Rejected stale AR callback: " +
                $"callback='{callbackName}', rejected={rejected}, " +
                $"callbackGeneration={token}, activeGeneration={active}, " +
                $"acceptingFrames={IsAcceptingFrames}.");
        }

        return false;
    }

    public static bool IsCurrentGraphics(
        long graphicsGeneration)
    {
        lock (sync)
        {
            return acceptingFrames &&
                graphicsGeneration > 0 &&
                current.GraphicsGeneration == graphicsGeneration;
        }
    }

    public static bool IsCurrentSession(
        ARRenderGenerationToken token)
    {
        lock (sync)
        {
            return acceptingFrames &&
                token.SessionGeneration > 0 &&
                token.SessionGeneration == current.SessionGeneration;
        }
    }
}
