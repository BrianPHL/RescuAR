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

    private static readonly object readinessLock =
        new();

    private static NativeBridgeReadiness readiness =
        NativeBridgeReadiness.NotTested;

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
    /// Forces Android's native loader to resolve the bridge and the release
    /// export before ARCore starts publishing camera frames. The zero pointer
    /// is explicitly accepted by the native function and has no side effects.
    /// The result is cached for the process lifetime so a missing or corrupt
    /// bridge cannot create a per-frame exception storm.
    /// </summary>
    internal static NativeBridgeReadiness EnsureReady()
    {
        lock (readinessLock)
        {
            if (readiness.WasTested)
            {
                return readiness;
            }

            try
            {
                ReleaseNativeHardwareBuffer(
                    nint.Zero);

                readiness =
                    new NativeBridgeReadiness(
                        true,
                        true,
                        NativeLibrary,
                        string.Empty,
                        string.Empty);
            }
            catch (Exception exception)
            {
                readiness =
                    new NativeBridgeReadiness(
                        true,
                        false,
                        NativeLibrary,
                        exception.GetType().Name,
                        exception.Message);
            }

            return readiness;
        }
    }

    internal static NativeBridgeReadiness CurrentReadiness
    {
        get
        {
            lock (readinessLock)
            {
                return readiness;
            }
        }
    }

    internal static NativeBridgeReadiness MarkUnavailable(
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(
            exception);

        lock (readinessLock)
        {
            readiness =
                new NativeBridgeReadiness(
                    true,
                    false,
                    NativeLibrary,
                    exception.GetType().Name,
                    exception.Message);

            return readiness;
        }
    }

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

        ArCoreJniOwnershipDiagnostics.Record(
            "AHardwareBuffer",
            "AHardwareBuffer_fromHardwareBuffer",
            "NATIVE_ACQUIRE");

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

        ArCoreJniOwnershipDiagnostics.Record(
            "AHardwareBuffer",
            "AHardwareBuffer_release",
            "NATIVE_RELEASE");
    }
}

internal sealed record NativeBridgeReadiness(
    bool WasTested,
    bool IsReady,
    string LoaderName,
    string FailureType,
    string FailureMessage)
{
    public static NativeBridgeReadiness NotTested { get; } =
        new(
            false,
            false,
            "native_bridge",
            string.Empty,
            string.Empty);
}
