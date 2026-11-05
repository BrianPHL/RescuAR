using System;
using System.Globalization;

namespace RescuAR.Diagnostics;

/// <summary>
/// Build boundary for AR field diagnostics. Production builds
/// cannot enable route-visibility overrides or location-bearing diagnostics.
/// Diagnostic field builds retain exact coordinates for local replay at the
/// owner's request. Production builds keep location-bearing fields redacted.
/// </summary>
public static class DiagnosticPrivacyPolicy
{
#if RESCUAR_DIAGNOSTICS
    public const bool IsDiagnosticBuild =
        true;

    // Road inspection must exercise the same confidence gates as navigation.
    public const bool DiagnosticRouteVisibilityOverrideEnabled =
        false;
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

    // Retained for manifest/caller compatibility. Diagnostic capture is now
    // automatic; the build profile, rather than an in-app prompt, is the gate.
    public static bool HasLocationLoggingConsent => IsDiagnosticBuild;

    public static void SetLocationLoggingConsent(
        bool granted)
    {
        _ = granted;
    }

    public static string FormatCoordinate(
        double latitude,
        double longitude)
    {
#if RESCUAR_DIAGNOSTICS
        if (!double.IsFinite(latitude) ||
            !double.IsFinite(longitude))
        {
            return "<invalid-location>";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"({latitude:R},{longitude:R})");
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
