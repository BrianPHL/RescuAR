using Android.Hardware;
using Android.Runtime;
using System;
using System.Runtime.InteropServices;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Converts a managed Android HardwareBuffer wrapper into a native
/// AHardwareBuffer pointer that Vulkan can safely consume.
/// </summary>
internal static class AHardwareBufferInterop
{
    private const string NativeLibrary =
        "native_bridge";

    [DllImport(
        NativeLibrary,
        EntryPoint = "rescuar_from_java_hardware_buffer",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern nint FromJavaHardwareBuffer(
        nint jniEnvironment,
        nint javaHardwareBuffer);

    [DllImport(
        NativeLibrary,
        EntryPoint = "rescuar_release_hardware_buffer",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void ReleaseNativeHardwareBuffer(
        nint nativeHardwareBuffer);

    /// <summary>
    /// Converts the Java HardwareBuffer object into a native
    /// AHardwareBuffer pointer and acquires a native reference.
    /// </summary>
    internal static nint ConvertAndAcquire(
        HardwareBuffer hardwareBuffer)
    {
        ArgumentNullException.ThrowIfNull(
            hardwareBuffer);

        if (hardwareBuffer.IsClosed)
        {
            throw new ObjectDisposedException(
                nameof(hardwareBuffer),
                "The Android HardwareBuffer is already closed.");
        }

        nint jniEnvironment =
            JNIEnv.Handle;

        /*
         * This is a JNI jobject handle. It must not be passed
         * directly to Vulkan as an AHardwareBuffer pointer.
         */
        nint javaHardwareBuffer =
            hardwareBuffer.Handle;

        if (jniEnvironment == nint.Zero)
        {
            throw new InvalidOperationException(
                "JNIEnv.Handle returned zero.");
        }

        if (javaHardwareBuffer == nint.Zero)
        {
            throw new InvalidOperationException(
                "HardwareBuffer.Handle returned zero.");
        }

        nint nativeHardwareBuffer =
            FromJavaHardwareBuffer(
                jniEnvironment,
                javaHardwareBuffer);

        if (nativeHardwareBuffer == nint.Zero)
        {
            throw new InvalidOperationException(
                "AHardwareBuffer_fromHardwareBuffer failed.");
        }

        return nativeHardwareBuffer;
    }

    /// <summary>
    /// Releases the native reference acquired by ConvertAndAcquire.
    /// </summary>
    internal static void Release(
        nint nativeHardwareBuffer)
    {
        if (nativeHardwareBuffer == nint.Zero)
        {
            return;
        }

        ReleaseNativeHardwareBuffer(
            nativeHardwareBuffer);
    }
}
