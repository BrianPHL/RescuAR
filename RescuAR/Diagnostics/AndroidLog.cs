using System;
using System.Linq;
using System.Reflection;

namespace RescuAR.Diagnostics;

/// <summary>
/// Android Logcat bridge for the shared RescuAR/Evergine project.
///
/// The shared engine targets plain .NET and cannot directly reference
/// Mono.Android. At runtime inside the Android MAUI host, this resolves and
/// invokes Android.Util.Log. No Console.WriteLine/Debug.WriteLine fallback is
/// used because the device diagnostics are expected in Logcat.
/// </summary>
public static class AndroidLog
{
    /// <summary>
    /// Detailed diagnostics are compiled only into explicit diagnostic builds.
    /// Information, warning, and error messages remain available for field
    /// validation, with sensitive fields passed through DiagnosticPrivacyPolicy.
    /// </summary>
    [System.Diagnostics.Conditional("RESCUAR_DIAGNOSTICS")]
    public static void Debug(
        string tag,
        string message)
    {
        Write(
            "Debug",
            tag,
            message);
    }

    public static void Info(
        string tag,
        string message)
    {
        Write(
            "Info",
            tag,
            message);
    }

    public static void Warn(
        string tag,
        string message)
    {
        Write(
            "Warn",
            tag,
            message);
    }

    public static void Error(
        string tag,
        string message)
    {
        Write(
            "Error",
            tag,
            message);
    }

    private static void Write(
        string level,
        string tag,
        string message)
    {
        try
        {
            Type? logType =
                AppDomain.CurrentDomain
                    .GetAssemblies()
                    .Select(
                        assembly =>
                            assembly.GetType(
                                "Android.Util.Log",
                                throwOnError: false))
                    .FirstOrDefault(
                        type =>
                            type is not null);

            if (logType is null)
            {
                return;
            }

            MethodInfo? method =
                logType.GetMethod(
                    level,
                    BindingFlags.Public |
                    BindingFlags.Static,
                    binder: null,
                    types:
                    [
                        typeof(string),
                        typeof(string)
                    ],
                    modifiers: null);

            method?.Invoke(
                null,
                [
                    tag,
                    message
                ]);
        }
        catch
        {
            // Diagnostics must never break AR/navigation execution.
        }
    }
}
