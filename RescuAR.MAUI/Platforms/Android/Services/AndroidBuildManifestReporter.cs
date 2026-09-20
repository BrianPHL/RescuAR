using Android.Content;
using Android.Util;
using Microsoft.Maui.ApplicationModel;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using RescuAR.AR;
using RescuAR.Diagnostics;

namespace RescuAR.MAUI.Platforms.Android.Services;

/// <summary>
/// Emits one immutable build/provenance line for every process. The field-log
/// collector supplements it with the installed APK hash and install method.
/// </summary>
internal static class AndroidBuildManifestReporter
{
    private const string Tag = "RescuAR-Build";

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

            string diagnosticBuild =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.DiagnosticBuild");

            string diagnosticRouteOverride =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.DiagnosticRouteOverride");

            string locationLogPolicy =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.LocationLogPolicy");

            string locationLogRetentionDays =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.LocationLogRetentionDays");

            string arSupportPolicy =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.ArSupportPolicy");

            string requiredAbi =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.RequiredAbi");

            string androidMinApi =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.AndroidMinApi");

            string vulkanRequirement =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.VulkanRequirement");

            string compatibilityProfile =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.CompatibilityProfile");

            string mauiVersion =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.MauiVersion");

            string evergineVersion =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.EvergineVersion");

            string arCoreBindingVersion =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.ArCoreBindingVersion");

            string ndkVersion =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.NdkVersion");

            string cmakeVersion =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.CMakeVersion");

            string validationProfile =
                GetAssemblyMetadata(
                    assembly,
                    "RescuAR.ValidationProfile");

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
                $"diagnosticBuild={diagnosticBuild}; " +
                $"diagnosticRouteOverride={diagnosticRouteOverride}; " +
                $"locationLogPolicy={locationLogPolicy}; " +
                $"locationLogRetentionDays={locationLogRetentionDays}; " +
                $"locationLogConsent={DiagnosticPrivacyPolicy.HasLocationLoggingConsent}; " +
                $"stateGlossary={ARStateTerminology.Version}; " +
                $"arSupportPolicy={arSupportPolicy}; " +
                $"requiredAbi={requiredAbi}; " +
                $"androidMinApi={androidMinApi}; " +
                $"vulkanRequirement={vulkanRequirement}; " +
                $"compatibilityProfile={compatibilityProfile}; " +
                $"targetFramework='{targetFramework}'; " +
                $"assemblyInformationalVersion='{informationalVersion}'; " +
                $"package='{context.PackageName}'; " +
                $"abis='{string.Join(",", global::Android.OS.Build.SupportedAbis ?? Array.Empty<string>())}'; " +
                $"nativePath='{(string.IsNullOrWhiteSpace(nativeLibraryPath) ? "unavailable" : nativeLibraryPath)}'; " +
                $"nativeExists={nativeLibraryExists}; " +
                $"nativeBytes={nativeLibraryBytes}; " +
                $"nativeSha256={nativeLibrarySha256}; " +
                $"maui={mauiVersion}; " +
                $"evergine={evergineVersion}; " +
                $"arCoreBinding={arCoreBindingVersion}; " +
                $"ndk={ndkVersion}; " +
                $"cmake={cmakeVersion}; " +
                $"validationProfile={validationProfile}; " +
                "customVulkanImporter=true; depthApi=true; " +
                "apkSha256=FIELD_LOG_COLLECTOR.");
        }
        catch (Exception exception)
        {
            Log.Error(
                Tag,
                "BUILD_MANIFEST_FAILED " +
                $"details='{DiagnosticPrivacyPolicy.FormatException(exception)}'.");
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
