using Evergine.Common.Graphics;
using Evergine.Common.Input.Keyboard;
using Evergine.Common.Input.Mouse;
using Evergine.Common.Input.Pointer;
using Evergine.Framework.Graphics;

namespace RescuAR.MAUI.Evergine;

/// <summary>
/// Publishes a stable surface snapshot to Evergine before UpdateFrame. Native
/// callbacks must not replace framebuffers later in GraphicsPresenter.Prepare,
/// after camera conversion and scene preparation have already used them.
/// </summary>
internal sealed class FrameBoundaryDisplay : Display
{
    private readonly SnapshotSurface snapshot;
    private readonly Action quiesceGraphics;

    public FrameBoundaryDisplay(Surface source, SwapChain swapChain, Action quiesceGraphics)
        : this(new SnapshotSurface(source), swapChain, quiesceGraphics) { }

    private FrameBoundaryDisplay(SnapshotSurface snapshot, SwapChain swapChain, Action quiesceGraphics)
        : base(snapshot, swapChain)
    {
        this.snapshot = snapshot;
        this.quiesceGraphics = quiesceGraphics ?? throw new ArgumentNullException(nameof(quiesceGraphics));
    }

    public bool ApplySurfaceChangesAtFrameStart(bool refreshSurfaceInfo)
    {
        uint width = snapshot.Source.Width, height = snapshot.Source.Height;
        if (width == 0 || height == 0) return false;
        SurfaceInfo info = snapshot.Source.SurfaceInfo;
        bool sizeChanged = width != snapshot.Width || height != snapshot.Height;
        bool infoChanged = refreshSurfaceInfo || !info.Equals(snapshot.SurfaceInfo);
        if (!sizeChanged && !infoChanged) return false;

        // Do not expose the new size or enqueue a rebuild if GPU quiescence fails.
        quiesceGraphics();
        snapshot.Publish(width, height, info, sizeChanged, infoChanged);
        base.Prepare();
        return true;
    }

    public override void Prepare()
    {
        // The engine calls this during DrawFrame. The frame-start method above
        // is the only resize owner; late native changes wait for the next frame.
    }

    private sealed class SnapshotSurface : Surface
    {
        public Surface Source { get; }
        public override KeyboardDispatcher KeyboardDispatcher => Source.KeyboardDispatcher;
        public override MouseDispatcher MouseDispatcher => Source.MouseDispatcher;
        public override PointerDispatcher TouchDispatcher => Source.TouchDispatcher;

        public SnapshotSurface(Surface source) : base(source.Width, source.Height)
        {
            Source = source;
            SurfaceInfo = source.SurfaceInfo;
            DPIDensity = source.DPIDensity;
            source.GotFocus += SourceGotFocus;
            source.LostFocus += SourceLostFocus;
        }

        public void Publish(uint width, uint height, SurfaceInfo info, bool sizeChanged, bool infoChanged)
        {
            Width = width;
            Height = height;
            SurfaceInfo = info;
            DPIDensity = Source.DPIDensity;
            if (sizeChanged) OnSizeChanged();
            if (infoChanged) OnInfoChanged();
        }

        private void SourceGotFocus(object? sender, EventArgs args) => OnGotFocus();
        private void SourceLostFocus(object? sender, EventArgs args) => OnLostFocus();

        protected override void Destroy()
        {
            Source.GotFocus -= SourceGotFocus;
            Source.LostFocus -= SourceLostFocus;
            // The Android handler owns Source and its native surface.
        }
    }
}
