using System;

namespace RescuAR.AR;

/// <summary>
/// Converts a low-rate Android battery-temperature/power-saver observation
/// into bounded AR workload limits. Hysteresis prevents rapid mode switching
/// when temperature fluctuates around a boundary.
/// </summary>
public static class ARPowerThermalPolicy
{
    private const float WarmEntryCelsius =
        39.0f;

    private const float HotEntryCelsius =
        43.0f;

    private const float CriticalEntryCelsius =
        46.0f;

    private const float WarmExitCelsius =
        37.5f;

    private const float HotExitCelsius =
        41.5f;

    private const float CriticalExitCelsius =
        44.5f;

    public static WorkloadDecision Evaluate(
        float batteryTemperatureCelsius,
        bool powerSaveMode,
        WorkloadMode currentMode)
    {
        WorkloadMode nextMode =
            SelectMode(
                batteryTemperatureCelsius,
                powerSaveMode,
                currentMode);

        return nextMode switch
        {
            WorkloadMode.Critical =>
                new WorkloadDecision(
                    nextMode,
                    100,
                    6,
                    false,
                    60_000,
                    batteryTemperatureCelsius,
                    powerSaveMode),

            WorkloadMode.Hot =>
                new WorkloadDecision(
                    nextMode,
                    67,
                    4,
                    false,
                    30_000,
                    batteryTemperatureCelsius,
                    powerSaveMode),

            WorkloadMode.Warm =>
                new WorkloadDecision(
                    nextMode,
                    50,
                    2,
                    true,
                    30_000,
                    batteryTemperatureCelsius,
                    powerSaveMode),

            WorkloadMode.Conserve =>
                new WorkloadDecision(
                    nextMode,
                    50,
                    2,
                    true,
                    30_000,
                    batteryTemperatureCelsius,
                    powerSaveMode),

            _ =>
                new WorkloadDecision(
                    WorkloadMode.Normal,
                    33,
                    1,
                    true,
                    10_000,
                    batteryTemperatureCelsius,
                    powerSaveMode)
        };
    }

    public static long AdjustDepthIntervalNanoseconds(
        long baseIntervalNanoseconds,
        WorkloadDecision decision)
    {
        if (baseIntervalNanoseconds <=
            0)
        {
            return baseIntervalNanoseconds;
        }

        return checked(
            baseIntervalNanoseconds *
            Math.Max(
                1,
                decision.DepthIntervalMultiplier));
    }

    private static WorkloadMode SelectMode(
        float temperatureCelsius,
        bool powerSaveMode,
        WorkloadMode currentMode)
    {
        bool temperatureAvailable =
            float.IsFinite(
                temperatureCelsius);

        if (temperatureAvailable)
        {
            if (currentMode ==
                    WorkloadMode.Critical &&
                temperatureCelsius >=
                    CriticalExitCelsius)
            {
                return WorkloadMode.Critical;
            }

            if (temperatureCelsius >=
                CriticalEntryCelsius)
            {
                return WorkloadMode.Critical;
            }

            if ((currentMode ==
                        WorkloadMode.Hot ||
                 currentMode ==
                        WorkloadMode.Critical) &&
                temperatureCelsius >=
                    HotExitCelsius)
            {
                return WorkloadMode.Hot;
            }

            if (temperatureCelsius >=
                HotEntryCelsius)
            {
                return WorkloadMode.Hot;
            }

            if ((currentMode ==
                        WorkloadMode.Warm ||
                 currentMode ==
                        WorkloadMode.Hot ||
                 currentMode ==
                        WorkloadMode.Critical) &&
                temperatureCelsius >=
                    WarmExitCelsius)
            {
                return WorkloadMode.Warm;
            }

            if (temperatureCelsius >=
                WarmEntryCelsius)
            {
                return WorkloadMode.Warm;
            }
        }

        return powerSaveMode
            ? WorkloadMode.Conserve
            : WorkloadMode.Normal;
    }

    public enum WorkloadMode
    {
        Normal,
        Conserve,
        Warm,
        Hot,
        Critical
    }

    public readonly record struct WorkloadDecision(
        WorkloadMode Mode,
        int MinimumFrameIntervalMilliseconds,
        int DepthIntervalMultiplier,
        bool RouteDepthAllowed,
        int DiagnosticLogIntervalMilliseconds,
        float BatteryTemperatureCelsius,
        bool PowerSaveMode)
    {
        public static WorkloadDecision Normal =>
            Evaluate(
                float.NaN,
                false,
                WorkloadMode.Normal);

        public int TargetMaximumFramesPerSecond =>
            Math.Max(
                1,
                (int)Math.Round(
                    1000.0 /
                        Math.Max(
                            1,
                            MinimumFrameIntervalMilliseconds)));
    }
}
