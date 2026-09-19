using Android.Content;
using Android.Util;
using Microsoft.Maui.ApplicationModel;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Emits one immutable build/provenance line for every process. The field-log
/// collector supplements it with the installed APK hash and install method.
/// </summary>
internal static class AndroidBuildManifestReporter
{
    private const string Tag = "RescuAR-Build";
    private const string EvergineVersion = "2025.10.21.3204";
    private const string ArCoreBindingVersion = "1.47.1";

    private static int hasLogged;

    internal static void LogOnce(
        Context context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (Interlocked.CompareExchange(
                ref hasLogged,
                1,
                0) != 0)
        {
            return;
        }

        try
        {
            Assembly assembly =
                typeof(AndroidBuildManifestReporter).Assembly;

            string configuration =
#if DEBUG
                "Debug";
#else
                "Release";
#endif

            string targetFramework =
                assembly
                    .GetCustomAttribute<TargetFrameworkAttribute>()?
                    .FrameworkName ??
                "unknown";

            string informationalVersion =
                assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion ??
                "unknown";

            string correctiveBatch =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.CorrectiveBatch");

            string sourceRevision =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.SourceRevision");

            string nativeLibraryDirectory =
                context.ApplicationInfo?.NativeLibraryDir ??
                string.Empty;

            string nativeLibraryPath =
                string.IsNullOrWhiteSpace(
                    nativeLibraryDirectory)
                    ? string.Empty
                    : Path.Combine(
                        nativeLibraryDirectory,
                        "libnative_bridge.so");

            bool nativeLibraryExists =
                !string.IsNullOrWhiteSpace(nativeLibraryPath) &&
                File.Exists(
                    nativeLibraryPath);

            long nativeLibraryBytes =
                nativeLibraryExists
                    ? new FileInfo(nativeLibraryPath).Length
                    : 0;

            string nativeLibrarySha256 =
                nativeLibraryExists
                    ? ComputeSha256(nativeLibraryPath)
                    : "unavailable";

            Log.Info(
                Tag,
                "BUILD_MANIFEST " +
                $"batch={correctiveBatch}; " +
                $"sourceRevision={sourceRevision}; " +
                $"appVersion={AppInfo.Current.VersionString}; " +
                $"appBuild={AppInfo.Current.BuildString}; " +
                $"configuration={configuration}; " +
                $"targetFramework='{targetFramework}'; " +
                $"assemblyInformationalVersion='{informationalVersion}'; " +
                $"package='{context.PackageName}'; " +
                $"abis='{string.Join(",", global::Android.OS.Build.SupportedAbis ?? Array.Empty<string>())}'; " +
                $"nativePath='{(string.IsNullOrWhiteSpace(nativeLibraryPath) ? "unavailable" : nativeLibraryPath)}'; " +
                $"nativeExists={nativeLibraryExists}; " +
                $"nativeBytes={nativeLibraryBytes}; " +
                $"nativeSha256={nativeLibrarySha256}; " +
                $"evergine={EvergineVersion}; " +
                $"arCoreBinding={ArCoreBindingVersion}; " +
                "customVulkanImporter=true; depthApi=true; " +
                "apkSha256=FIELD_LOG_COLLECTOR.");
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                "BUILD_MANIFEST_FAILED " +
                $"failureType={exception.GetType().Name}; " +
                $"message='{exception.Message}'.");
        }
    }

    private static string ComputeSha256(
        string path)
    {
        using FileStream stream =
            File.OpenRead(
                path);

        return Convert.ToHexString(
            SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static string GetAssemblyMetadata(
        Assembly assembly,
        string key) =>
        assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(
                attribute =>
                    string.Equals(
                        attribute.Key,
                        key,
                        StringComparison.Ordinal))?
            .Value ??
        "unavailable";
}
