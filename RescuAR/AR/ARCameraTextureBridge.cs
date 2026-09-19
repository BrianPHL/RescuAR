using Evergine.Common.Graphics;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RescuAR.AR;

/// <summary>
/// Generation-scoped handoff between the ARCore producer and Evergine draw
/// thread. It also owns the draw-thread teardown barrier used before Android
/// detaches a Vulkan surface.
/// </summary>
public static class ARCameraTextureBridge
{
    public const long MaximumTextureAgeMilliseconds = 750;

    private static readonly object processingSync = new();

    private static readonly ManualResetEventSlim processorIdle =
        new(initialState: true);

    private static TextureSnapshot current =
        TextureSnapshot.Unavailable;

    private static long version;
    private static Action? drawThreadProcessor;
    private static long processorGraphicsGeneration;
    private static bool processingSuspended = true;
    private static int activeProcessorCalls;
    private static TeardownRequest? pendingTeardown;
    private static TeardownRequest? teardownAwaitingFrameBoundary;
    private static long completedTeardownGeneration;
    private static Task completedTeardownTask = Task.CompletedTask;

    public static TextureSnapshot Current
    {
        get
        {
            TextureSnapshot snapshot;

            lock (processingSync)
            {
                snapshot = current;
            }

            /*
             * Do not call the generation bridge while holding processingSync.
             * Activation takes the inverse lock order when it resumes camera
             * processing, so validating outside the lock avoids a teardown /
             * resume deadlock.
             */
            if (snapshot.Texture is null ||
                !snapshot.IsFresh ||
                !ARRenderGenerationBridge.IsCurrent(snapshot.Generation))
            {
                return TextureSnapshot.UnavailableWithVersion(
                    snapshot.Version);
            }

            return snapshot;
        }
    }

    public static Texture? CurrentTexture =>
        Current.Texture;

    public static long Version =>
        Interlocked.Read(ref version);

    public static void SetDrawThreadProcessor(
        long graphicsGeneration,
        Action processor)
    {
        ArgumentNullException.ThrowIfNull(processor);

        if (graphicsGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(graphicsGeneration));
        }

        lock (processingSync)
        {
            drawThreadProcessor = processor;
            processorGraphicsGeneration = graphicsGeneration;
            processingSuspended = true;
        }
    }

    /// <summary>
    /// Rejects new camera conversions and waits for the currently executing
    /// conversion callback. A queued teardown command can still run while the
    /// ordinary processor is suspended.
    /// </summary>
    public static bool SuspendProcessing(
        TimeSpan timeout)
    {
        lock (processingSync)
        {
            processingSuspended = true;

            if (activeProcessorCalls == 0)
            {
                processorIdle.Set();
            }
        }

        return processorIdle.Wait(timeout);
    }

    public static void ResumeProcessing(
        long graphicsGeneration)
    {
        lock (processingSync)
        {
            if (graphicsGeneration != processorGraphicsGeneration ||
                pendingTeardown is not null ||
                teardownAwaitingFrameBoundary is not null)
            {
                return;
            }

            processingSuspended = false;
        }
    }

    /// <summary>
    /// Queues one context-specific teardown action for the Evergine draw
    /// thread. Completion occurs only after MyApplication has skipped the
    /// corresponding base draw/present call and reached the frame boundary.
    /// </summary>
    public static Task RequestDrawThreadTeardownAsync(
        long graphicsGeneration,
        Action teardownAction)
    {
        ArgumentNullException.ThrowIfNull(teardownAction);

        lock (processingSync)
        {
            if (graphicsGeneration != processorGraphicsGeneration)
            {
                return Task.CompletedTask;
            }

            if (completedTeardownGeneration == graphicsGeneration)
            {
                return completedTeardownTask;
            }

            if (pendingTeardown is not null &&
                pendingTeardown.GraphicsGeneration == graphicsGeneration)
            {
                return pendingTeardown.Completion.Task;
            }

            if (teardownAwaitingFrameBoundary is not null &&
                teardownAwaitingFrameBoundary.GraphicsGeneration ==
                    graphicsGeneration)
            {
                return teardownAwaitingFrameBoundary.Completion.Task;
            }

            processingSuspended = true;
            drawThreadProcessor = null;

            TeardownRequest request =
                new(graphicsGeneration, teardownAction);

            pendingTeardown = request;
            return request.Completion.Task;
        }
    }

    /// <summary>
    /// Executes the teardown inline when Android raises its final surface
    /// boundary on the Evergine owner thread and no later draw frame is
    /// guaranteed. Any waiter for the same generation receives the result.
    /// </summary>
    public static void ExecuteDrawThreadTeardownAtSurfaceBoundary(
        long graphicsGeneration,
        Action teardownAction)
    {
        ArgumentNullException.ThrowIfNull(teardownAction);

        TeardownRequest request;

        lock (processingSync)
        {
            if (graphicsGeneration != processorGraphicsGeneration)
            {
                return;
            }

            if (completedTeardownGeneration == graphicsGeneration)
            {
                completedTeardownTask.GetAwaiter().GetResult();
                return;
            }

            if (activeProcessorCalls != 0)
            {
                throw new InvalidOperationException(
                    "Cannot tear down AR Vulkan resources while draw-thread " +
                    "camera work is still active.");
            }

            request =
                pendingTeardown ??
                teardownAwaitingFrameBoundary ??
                new TeardownRequest(
                    graphicsGeneration,
                    teardownAction);

            pendingTeardown = null;
            teardownAwaitingFrameBoundary = null;
            processingSuspended = true;
            drawThreadProcessor = null;
        }

        Exception? failure = null;

        try
        {
            request.Action();
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        lock (processingSync)
        {
            completedTeardownGeneration = graphicsGeneration;
            completedTeardownTask = request.Completion.Task;
        }

        if (failure is null)
        {
            request.Completion.TrySetResult();
        }
        else
        {
            request.Completion.TrySetException(failure);
            throw failure;
        }
    }

    /// <summary>
    /// Runs at most one camera conversion or teardown command. True means the
    /// caller must skip Evergine's base draw/present for this graphics context.
    /// </summary>
    public static bool ProcessDrawThreadWork(
        long drawGraphicsGeneration)
    {
        Action? work = null;
        TeardownRequest? teardown = null;
        bool skipDraw;

        lock (processingSync)
        {
            skipDraw =
                processorGraphicsGeneration > 0 &&
                (processingSuspended ||
                 drawGraphicsGeneration != processorGraphicsGeneration);

            if (pendingTeardown is not null &&
                pendingTeardown.GraphicsGeneration == drawGraphicsGeneration)
            {
                teardown = pendingTeardown;
                pendingTeardown = null;
                work = teardown.Action;
                skipDraw = true;
            }
            else if (!skipDraw)
            {
                work = drawThreadProcessor;
            }

            if (work is not null)
            {
                activeProcessorCalls++;
                processorIdle.Reset();
            }
        }

        if (work is null)
        {
            return skipDraw;
        }

        try
        {
            work();
        }
        catch (Exception exception)
        {
            if (teardown is null)
            {
                throw;
            }

            teardown.Exception = exception;
        }
        finally
        {
            lock (processingSync)
            {
                activeProcessorCalls--;

                if (activeProcessorCalls == 0)
                {
                    processorIdle.Set();
                }

                if (teardown is not null)
                {
                    teardownAwaitingFrameBoundary = teardown;
                }
            }
        }

        return skipDraw;
    }

    /// <summary>
    /// Called from MyApplication after the draw was either completed normally
    /// or intentionally skipped. This is the acknowledgement consumed by the
    /// Android handler before it pauses/detaches the surface.
    /// </summary>
    public static void CompleteDrawThreadFrame(
        long drawGraphicsGeneration)
    {
        TeardownRequest? completed = null;

        lock (processingSync)
        {
            if (teardownAwaitingFrameBoundary is not null &&
                teardownAwaitingFrameBoundary.GraphicsGeneration ==
                    drawGraphicsGeneration)
            {
                completed = teardownAwaitingFrameBoundary;
                teardownAwaitingFrameBoundary = null;
            }
        }

        if (completed is null)
        {
            return;
        }

        lock (processingSync)
        {
            completedTeardownGeneration =
                completed.GraphicsGeneration;

            completedTeardownTask =
                completed.Completion.Task;
        }

        if (completed.Exception is null)
        {
            completed.Completion.TrySetResult();
        }
        else
        {
            completed.Completion.TrySetException(completed.Exception);
        }
    }

    public static bool Publish(
        ARFrameMetadata metadata,
        Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        if (!metadata.IsValid ||
            !ARRenderGenerationBridge.TryAcceptCallback(
                metadata.Generation,
                "camera-texture-publish"))
        {
            return false;
        }

        lock (processingSync)
        {
            if (processingSuspended ||
                metadata.Generation.GraphicsGeneration !=
                    processorGraphicsGeneration)
            {
                return false;
            }

            if (ReferenceEquals(current.Texture, texture) &&
                current.Metadata == metadata)
            {
                return true;
            }

            long nextVersion = Interlocked.Increment(ref version);
            current = new TextureSnapshot(
                nextVersion,
                metadata,
                Environment.TickCount64,
                texture);

            return true;
        }
    }

    public static void Clear()
    {
        long nextVersion = Interlocked.Increment(ref version);

        lock (processingSync)
        {
            current = TextureSnapshot.UnavailableWithVersion(nextVersion);
        }
    }

    public readonly record struct TextureSnapshot(
        long Version,
        ARFrameMetadata Metadata,
        long PublishedAtMonotonicMilliseconds,
        Texture? Texture)
    {
        public ARRenderGenerationToken Generation => Metadata.Generation;
        public long FrameTimestamp => Metadata.FrameTimestamp;
        public ARDisplayGeometrySnapshot DisplayGeometry =>
            Metadata.DisplayGeometry;

        public bool IsFresh =>
            PublishedAtMonotonicMilliseconds != long.MinValue &&
            Environment.TickCount64 - PublishedAtMonotonicMilliseconds >= 0 &&
            Environment.TickCount64 - PublishedAtMonotonicMilliseconds <=
                MaximumTextureAgeMilliseconds;

        public static TextureSnapshot Unavailable =>
            UnavailableWithVersion(0);

        public static TextureSnapshot UnavailableWithVersion(long version) =>
            new(
                version,
                ARFrameMetadata.Invalid,
                long.MinValue,
                null);
    }

    private sealed class TeardownRequest
    {
        public TeardownRequest(
            long graphicsGeneration,
            Action action)
        {
            GraphicsGeneration = graphicsGeneration;
            Action = action;
            Completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public long GraphicsGeneration { get; }
        public Action Action { get; }
        public TaskCompletionSource Completion { get; }
        public Exception? Exception { get; set; }
    }
}
