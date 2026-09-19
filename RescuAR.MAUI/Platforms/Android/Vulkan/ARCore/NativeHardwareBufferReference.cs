using Android.Hardware;
using RescuAR.MAUI.Platforms.Android.Services;

namespace RescuAR.MAUI.Platforms.Android.Vulkan.ARCore;

/// <summary>
/// Owns one acquired native AHardwareBuffer reference derived from an Android
/// HardwareBuffer.
///
/// Construction calls AHardwareBufferInterop.ConvertAndAcquire(). Disposal
/// releases exactly that acquired native reference.
///
/// The Java HardwareBuffer itself is not owned or closed by this class.
/// </summary>
internal sealed class NativeHardwareBufferReference : IDisposable
{
    private nint pointer;
    private bool disposed;

    private NativeHardwareBufferReference(
        nint pointer)
    {
        if (pointer == nint.Zero)
        {
            throw new ArgumentException(
                "Native AHardwareBuffer pointer cannot be zero.",
                nameof(pointer));
        }

        this.pointer =
            pointer;
    }

    public nint Pointer
    {
        get
        {
            ThrowIfDisposed();
            return pointer;
        }
    }

    public bool IsDisposed =>
        disposed;

    /// <summary>
    /// Converts the Java HardwareBuffer to a native AHardwareBuffer pointer
    /// and acquires a native reference that remains valid until Dispose().
    /// </summary>
    public static NativeHardwareBufferReference Acquire(
        HardwareBuffer hardwareBuffer)
    {
        ArgumentNullException.ThrowIfNull(
            hardwareBuffer);

        if (hardwareBuffer.IsClosed)
        {
            throw new InvalidOperationException(
                "HardwareBuffer has already been closed.");
        }

        nint nativePointer =
            AHardwareBufferInterop.ConvertAndAcquire(
                hardwareBuffer);

        if (nativePointer == nint.Zero)
        {
            throw new InvalidOperationException(
                "AHardwareBufferInterop returned a zero native pointer.");
        }

        return new NativeHardwareBufferReference(
            nativePointer);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(
                nameof(NativeHardwareBufferReference));
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        if (pointer != nint.Zero)
        {
            AHardwareBufferInterop.Release(
                pointer);

            pointer =
                nint.Zero;
        }

        disposed =
            true;
    }
}
