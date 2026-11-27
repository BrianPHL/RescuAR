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
    private static Func<bool>? drawThreadProcessor;
    private static long processorGraphicsGeneration;
    private static bool processingSuspended = true;
    private static int activeProcessorCalls;
    private static TeardownRequest? teardown;

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
        Func<bool> processor)
    {
        ArgumentNullException.ThrowIfNull(processor);

        if (graphicsGeneration <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(graphicsGeneration));
        }

        lock (processingSync)
        {
            if (activeProcessorCalls != 0 || teardown is { Step: not TeardownStep.Complete })
                throw new InvalidOperationException("The previous camera draw owner has not finished.");

            if (processorGraphicsGeneration == graphicsGeneration && teardown is not null)
                throw new InvalidOperationException("A retired camera generation cannot be registered again.");

            drawThreadProcessor = processor;
            processorGraphicsGeneration = graphicsGeneration;
            processingSuspended = true;
            teardown = null;
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
            if (graphicsGeneration != processorGraphicsGeneration || teardown is not null)
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

            return GetTeardownLocked(teardownAction).Completion.Task;
        }
    }

    private static TeardownRequest GetTeardownLocked(Action action)
    {
        processingSuspended = true;
        drawThreadProcessor = null;
        return teardown ??= new TeardownRequest(action);
    }

    /// <summary>
    /// Executes the teardown inline when Android raises its final surface
    /// boundary on the Evergine owner thread and no later draw frame is
    /// guaranteed. Any waiter for the same generation receives the result.
    /// </summary>
    public static Task ExecuteDrawThreadTeardownAtSurfaceBoundaryAsync(
        long graphicsGeneration,
        Action teardownAction)
    {
        ArgumentNullException.ThrowIfNull(teardownAction);

        TeardownRequest request;

        lock (processingSync)
        {
            if (graphicsGeneration != processorGraphicsGeneration)
            {
                return Task.CompletedTask;
            }

            if (teardown is { Step: TeardownStep.AwaitingFrameBoundary } finished)
            {
                CompleteTeardownLocked(finished);
                return finished.Completion.Task;
            }

            if (teardown is { Step: TeardownStep.Running or TeardownStep.Complete } claimed)
                return claimed.Completion.Task;

            if (activeProcessorCalls != 0)
            {
                throw new InvalidOperationException(
                    "Cannot tear down AR Vulkan resources while draw-thread " +
                    "camera work is still active.");
            }

            request = GetTeardownLocked(teardownAction);
            request.Step = TeardownStep.Running;
            activeProcessorCalls++;
            processorIdle.Reset();
        }

        request.Run();
        lock (processingSync)
        {
            if (--activeProcessorCalls == 0) processorIdle.Set();
            CompleteTeardownLocked(request);
        }

        return request.Completion.Task;
    }

    /// <summary>
    /// Runs at most one camera conversion or teardown command. True means the
    /// caller must skip Evergine's base draw/present for this graphics context.
    /// </summary>
    public static bool ProcessDrawThreadWork(
        long drawGraphicsGeneration)
    {
        Func<bool>? work = null;
        TeardownRequest? drawTeardown = null;
        bool skipDraw;

        lock (processingSync)
        {
            skipDraw =
                processorGraphicsGeneration > 0 &&
                (processingSuspended ||
                 drawGraphicsGeneration != processorGraphicsGeneration);

            if (drawGraphicsGeneration == processorGraphicsGeneration &&
                teardown is { Step: TeardownStep.Queued } queued)
            {
                drawTeardown = queued;
                drawTeardown.Step = TeardownStep.Running;
                work = () => { queued.Run(); return true; };
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
            skipDraw |= work();
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

                if (drawTeardown is not null)
                {
                    drawTeardown.Step = TeardownStep.AwaitingFrameBoundary;
                }

                // The importer may suspend this generation after a GPU error.
                // Do not present again after that suspension in the same call.
                skipDraw |= processingSuspended;
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
        lock (processingSync)
        {
            if (drawGraphicsGeneration == processorGraphicsGeneration &&
                teardown is { Step: TeardownStep.AwaitingFrameBoundary } finished)
                CompleteTeardownLocked(finished);
        }
    }

    private static void CompleteTeardownLocked(TeardownRequest request)
    {
        request.Step = TeardownStep.Complete;
        if (request.Exception is { } failure) request.Completion.TrySetException(failure);
        else request.Completion.TrySetResult();
    }

    public static bool Publish(
        ARFrameMetadata metadata,
        Texture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        if (!metadata.IsFresh(MaximumTextureAgeMilliseconds, Environment.TickCount64) ||
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
                metadata.AcquiredAtMonotonicMilliseconds,
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

    private enum TeardownStep { Queued, Running, AwaitingFrameBoundary, Complete }

    private sealed class TeardownRequest(Action action)
    {
        private Action? actionToRun = action;

        public TeardownStep Step { get; set; }
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Exception? Exception { get; private set; }

        public void Run()
        {
            var run = actionToRun;
            actionToRun = null;
            try { run!(); }
            catch (Exception exception) { Exception = exception; }
        }
    }
}
