using System;

namespace RescuAR.AR;

/// <summary>Exact device/OS profiles with field evidence of native depth aborts.</summary>
public static class ARDepthCompatibilityPolicy
{
    public readonly record struct DepthConfigurationDecision(bool DisableDepth, string Mode);

    public static DepthConfigurationDecision Evaluate(string? model, int androidApi,
        bool diagnosticRetest, bool forceOn = false, bool forceOff = false)
    {
        if (forceOff) return new(true, "EXPLICIT_DEPTH_OFF");
        if (forceOn) return new(false, "EXPLICIT_DEPTH_ON");
        bool quarantined = DisableAutomaticDepth(model, androidApi);
        return quarantined && !diagnosticRetest
            ? new(true, "NATIVE_CRASH_PROFILE")
            : new(false, quarantined ? "DEPTH_FIELD_REVALIDATION" : "DEPTH_SESSION_STABLE");
    }

    public static bool DisableAutomaticDepth(string? model, int androidApi)
    {
        bool a546 = string.Equals(model, "SM-A546E", StringComparison.OrdinalIgnoreCase);
        bool a156 = string.Equals(model, "SM-A156E", StringComparison.OrdinalIgnoreCase);
        // API 34 SM-A546E: 2026-09-29 16:31:36, ms_depth / spherical_rectifier.
        // Preserve the previously quarantined API 36+ profiles.
        return (androidApi == 34 && a546) || (androidApi >= 36 && (a546 || a156));
    }
}
