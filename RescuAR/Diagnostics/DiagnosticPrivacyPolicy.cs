using System;
using System.Globalization;
using System.Threading;

namespace RescuAR.Diagnostics;

/// <summary>
/// Build and consent boundary for AR field diagnostics. Production builds
/// cannot enable route-visibility overrides or location-bearing diagnostics.
/// Diagnostic builds still require an explicit tester decision before a
/// coordinate can be emitted, and accepted coordinates are quantized to
/// roughly an 11 m grid rather than logged at GPS precision.
/// </summary>
public static class DiagnosticPrivacyPolicy
{
#if RESCUAR_DIAGNOSTICS
    public const bool IsDiagnosticBuild =
        true;

    public const bool DiagnosticRouteVisibilityOverrideEnabled =
        true;
#else
    public const bool IsDiagnosticBuild =
        false;

    public const bool DiagnosticRouteVisibilityOverrideEnabled =
        false;
#endif

    public const int MaximumRetentionDays =
        30;

    public const string LocationRetentionPolicy =
        "Tester-controlled export; delete field bundles after evidence review " +
        "and no later than 30 days.";

#if RESCUAR_DIAGNOSTICS
    private static int locationLoggingConsent;
#endif

    public static bool HasLocationLoggingConsent
    {
        get
        {
#if RESCUAR_DIAGNOSTICS
            return Volatile.Read(
                ref locationLoggingConsent) ==
                    1;
#else
            return false;
#endif
        }
    }

    public static void SetLocationLoggingConsent(
        bool granted)
    {
#if RESCUAR_DIAGNOSTICS
        Volatile.Write(
            ref locationLoggingConsent,
            granted
                ? 1
                : 0);
#else
        _ = granted;
#endif
    }

    public static string FormatCoordinate(
        double latitude,
        double longitude)
    {
#if RESCUAR_DIAGNOSTICS
        if (!HasLocationLoggingConsent)
        {
            return "<location-consent-required>";
        }

        if (!double.IsFinite(latitude) ||
            !double.IsFinite(longitude))
        {
            return "<invalid-location>";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"({latitude:F4},{longitude:F4})");
#else
        _ = latitude;
        _ = longitude;
        return "<redacted-location>";
#endif
    }

    public static string FormatDeviceProfile(
        string? manufacturer,
        string? model)
    {
#if RESCUAR_DIAGNOSTICS
        string normalizedManufacturer =
            string.IsNullOrWhiteSpace(manufacturer)
                ? "unknown"
                : manufacturer.Trim();

        string normalizedModel =
            string.IsNullOrWhiteSpace(model)
                ? "unknown"
                : model.Trim();

        return $"{normalizedManufacturer}/{normalizedModel}";
#else
        _ = manufacturer;
        _ = model;
        return "<redacted-device-profile>";
#endif
    }

    public static string FormatRouteLabel(
        string? value)
    {
#if RESCUAR_DIAGNOSTICS
        if (!HasLocationLoggingConsent)
        {
            return "<location-consent-required>";
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return "<unnamed>";
        }

        string normalized =
            value.Trim()
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ');

        return normalized.Length <=
                64
            ? normalized
            : normalized[..64];
#else
        _ = value;
        return "<redacted-route-label>";
#endif
    }

    public static string FormatException(
        Exception? exception)
    {
        if (exception is null)
        {
            return "<none>";
        }

        string typeName =
            exception.GetType().Name;

#if RESCUAR_DIAGNOSTICS
        if (!HasLocationLoggingConsent)
        {
            return typeName;
        }

        string message =
            (exception.Message ?? string.Empty)
                .Replace(
                    '\r',
                    ' ')
                .Replace(
                    '\n',
                    ' ')
                .Trim();

        if (message.Length >
            256)
        {
            message =
                message[..256];
        }

        return string.IsNullOrWhiteSpace(message)
            ? typeName
            : $"{typeName}: {message}";
#else
        return typeName;
#endif
    }
}
