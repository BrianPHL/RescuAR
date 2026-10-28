using Evergine.Common.Graphics;
using Evergine.Common.Input.Keyboard;
using Evergine.Common.Input.Mouse;
using Evergine.Common.Input.Pointer;
using Evergine.Framework.Graphics;
using RescuAR.MAUI.Evergine;

internal static class SurfaceResizeChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Use the pinned engine's real Display with native-free Surface and
        // SwapChain implementations. Reproduce the previous competing owners.
        var source = new TestSurface();
        var chain = new TestSwapChain();
        using var previous = new Display(source, chain);
        source.Resize(1080, 1683);
        chain.ResizeSwapChain(source.Width, source.Height); // Former Android handler.
        var firstBuffer = chain.FrameBuffer;
        check(!ReferenceEquals(previous.FrameBuffer, firstBuffer), "former direct resize leaves Evergine Display pointing at its old framebuffer");
        previous.Prepare(); // Pinned engine's delayed resize during DrawFrame.
        check(chain.Resizes == 2 && !ReferenceEquals(chain.FrameBuffer, firstBuffer), "real engine reproduces a second framebuffer replacement for one size event");

        source = new TestSurface();
        chain = new TestSwapChain();
        int drains = 0, sizeEvents = 0;
        using var display = new FrameBoundaryDisplay(source, chain, () => drains++);
        display.DisplaySizeChanged += (_, _) => sizeEvents++;
        check(!display.ApplySurfaceChangesAtFrameStart(false) && drains == 0 && chain.Resizes == 0, "unchanged frames do not stall the graphics device or rebuild buffers");

        source.Resize(1080, 1683);
        display.Prepare();
        check(display.Height == 1650 && chain.Resizes == 0 && sizeEvents == 0, "native changes cannot alter display geometry or resize during the current frame");
        check(display.ApplySurfaceChangesAtFrameStart(false) && chain.Resizes == 1 && drains == 1 && sizeEvents == 1,
            "one size event is drained and applied once at the next frame start");
        check(display.Height == 1683 && ReferenceEquals(display.FrameBuffer, chain.FrameBuffer), "frame-start resize updates the actual engine Display framebuffer before scene preparation");
        firstBuffer = chain.FrameBuffer;
        display.Prepare();
        check(chain.Resizes == 1 && ReferenceEquals(chain.FrameBuffer, firstBuffer), "engine Prepare cannot replace a framebuffer already prepared for this frame");

        source.Resize(1080, 1650);
        display.Prepare();
        check(display.Height == 1683 && ReferenceEquals(chain.FrameBuffer, firstBuffer), "a late return-to-AR resize keeps the current frame's geometry and buffer stable");
        display.ApplySurfaceChangesAtFrameStart(false);
        check(display.Height == 1650 && chain.Resizes == 2 && drains == 2, "the deferred return-to-AR size applies on the following frame");

        source.Resize(1600, 1000);
        source.Resize(1920, 1080);
        display.ApplySurfaceChangesAtFrameStart(false);
        check(display.Width == 1920 && display.Height == 1080 && chain.Resizes == 3 && drains == 3, "rapid layout events coalesce into one rebuild with the latest dimensions");
        source.Resize(0, 0);
        check(!display.ApplySurfaceChangesAtFrameStart(false) && display.Width == 1920 && drains == 3, "temporary zero-size surfaces cannot replace usable geometry or invoke native resizing");
        source.Resize(1920, 1080);
        check(!display.ApplySurfaceChangesAtFrameStart(false) && chain.Resizes == 3, "restoring unchanged positive dimensions does not rebuild again");

        display.ApplySurfaceChangesAtFrameStart(true);
        check(chain.Refreshes == 1 && drains == 4 && ReferenceEquals(display.FrameBuffer, chain.FrameBuffer), "same-size surface recreation refreshes once and publishes the new framebuffer");
        firstBuffer = chain.FrameBuffer;
        display.Prepare();
        check(chain.Refreshes == 1 && ReferenceEquals(firstBuffer, chain.FrameBuffer), "surface recreation is not repeated by the engine's later Prepare call");

        int focusEvents = 0;
        display.Surface.GotFocus += (_, _) => focusEvents++;
        source.Focus();
        check(focusEvents == 1, "surface snapshots continue forwarding focus and the source input dispatchers");
        check(ReferenceEquals(display.KeyboardDispatcher, source.KeyboardDispatcher) &&
            ReferenceEquals(display.MouseDispatcher, source.MouseDispatcher) &&
            ReferenceEquals(display.TouchDispatcher, source.TouchDispatcher), "input is read from the existing Android surface rather than a replacement device");

        display.Dispose();
        check(!source.Destroyed, "disposing the display snapshot does not destroy the Android-owned native surface");
        source.Focus();
        check(focusEvents == 1, "disposing the display snapshot removes its source focus subscriptions");

        source = new TestSurface();
        chain = new TestSwapChain();
        bool failDrain = true;
        using var failing = new FrameBoundaryDisplay(source, chain, () => { if (failDrain) throw new InvalidOperationException("test GPU wait failure"); });
        source.Resize(1080, 1683);
        bool rejected = false;
        try { failing.ApplySurfaceChangesAtFrameStart(false); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && failing.Height == 1650 && chain.Resizes == 0, "failed graphics quiescence leaves the old framebuffer and geometry untouched");
        failDrain = false;
        check(failing.ApplySurfaceChangesAtFrameStart(false) && failing.Height == 1683 && chain.Resizes == 1, "a later safe frame can apply the retained geometry change");
    }

    private sealed class TestSurface() : Surface(1080, 1650)
    {
        public bool Destroyed { get; private set; }
        protected override void Destroy() => Destroyed = true;
        public override KeyboardDispatcher KeyboardDispatcher => null!;
        public override MouseDispatcher MouseDispatcher => null!;
        public override PointerDispatcher TouchDispatcher => null!;
        public void Resize(uint width, uint height) { Width = width; Height = height; OnSizeChanged(); }
        public void Focus() => OnGotFocus();
    }

    private sealed class TestFrameBuffer : FrameBuffer
    {
        public override string Name { get; set; } = "test framebuffer";
    }

    private sealed class TestSwapChain : SwapChain
    {
        public int Resizes { get; private set; }
        public int Refreshes { get; private set; }
        public override string Name { get; set; } = "test swapchain";
        public override void ResizeSwapChain(uint width, uint height)
        {
            Resizes++;
            FrameBuffer?.Dispose();
            FrameBuffer = new TestFrameBuffer();
        }
        public override void RefreshSurfaceInfo(SurfaceInfo info)
        {
            Refreshes++;
            FrameBuffer?.Dispose();
            FrameBuffer = new TestFrameBuffer();
        }
        public override Texture GetCurrentFramebufferTexture() => null!;
        public override void Present() { }
        public override void Dispose() => FrameBuffer?.Dispose();
    }
}
