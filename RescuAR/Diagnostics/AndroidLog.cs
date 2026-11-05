using System;
using System.Linq;
using System.Reflection;
using System.Threading;

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
    private static readonly object resolutionSync = new();
    private static LogWriters? writers;

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
            LogWriters? resolved = Volatile.Read(ref writers) ?? ResolveWriters();
            Func<string, string, int>? write = level switch
            {
                "Debug" => resolved?.Debug,
                "Info" => resolved?.Info,
                "Warn" => resolved?.Warn,
                "Error" => resolved?.Error,
                _ => null
            };
            write?.Invoke(tag, message);
        }
        catch
        {
            // Diagnostics must never break AR/navigation execution.
        }
    }

    private static LogWriters? ResolveWriters()
    {
        lock (resolutionSync)
        {
            if (writers is not null) return writers;

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
                // The shared engine also runs on non-Android hosts. Do not
                // cache a miss: Android may load its assembly after startup.
                return null;
            }

            Func<string, string, int>? Resolve(string name) =>
                logType.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                    binder: null, types: [typeof(string), typeof(string)], modifiers: null)
                    ?.CreateDelegate<Func<string, string, int>>();

            var resolved = new LogWriters(Resolve("Debug"), Resolve("Info"), Resolve("Warn"), Resolve("Error"));
            Volatile.Write(ref writers, resolved);
            return resolved;
        }
    }

    private sealed record LogWriters(Func<string, string, int>? Debug,
        Func<string, string, int>? Info, Func<string, string, int>? Warn,
        Func<string, string, int>? Error);
}
