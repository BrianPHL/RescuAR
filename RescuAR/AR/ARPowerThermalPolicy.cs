using System;

namespace RescuAR.AR;

/// <summary>
/// Converts a low-rate Android battery-temperature/power-saver observation
/// into bounded AR workload limits. Hysteresis prevents rapid mode switching
/// when temperature fluctuates around a boundary.
/// </summary>
public static class ARPowerThermalPolicy
{
    private static readonly object sync = new();

    private static WorkloadDecision currentDecision =
        WorkloadDecision.Normal;

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
        AndroidThermalSeverity androidThermalSeverity,
        WorkloadMode currentMode)
    {
        WorkloadMode nextMode =
            SelectMode(
                batteryTemperatureCelsius,
                powerSaveMode,
                androidThermalSeverity,
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
                    8,
                    1,
                    48,
                    500,
                    batteryTemperatureCelsius,
                    powerSaveMode,
                    androidThermalSeverity),

            WorkloadMode.Hot =>
                new WorkloadDecision(
                    nextMode,
                    67,
                    4,
                    false,
                    30_000,
                    4,
                    3,
                    72,
                    250,
                    batteryTemperatureCelsius,
                    powerSaveMode,
                    androidThermalSeverity),

            WorkloadMode.Warm =>
                new WorkloadDecision(
                    nextMode,
                    50,
                    2,
                    true,
                    30_000,
                    2,
                    6,
                    96,
                    150,
                    batteryTemperatureCelsius,
                    powerSaveMode,
                    androidThermalSeverity),

            WorkloadMode.Conserve =>
                new WorkloadDecision(
                    nextMode,
                    50,
                    2,
                    true,
                    30_000,
                    2,
                    6,
                    96,
                    150,
                    batteryTemperatureCelsius,
                    powerSaveMode,
                    androidThermalSeverity),

            _ =>
                new WorkloadDecision(
                    WorkloadMode.Normal,
                    33,
                    1,
                    true,
                    30_000,
                    1,
                    10,
                    120,
                    100,
                    batteryTemperatureCelsius,
                    powerSaveMode,
                    androidThermalSeverity)
        };
    }

    public static WorkloadDecision CurrentDecision
    {
        get
        {
            lock (sync)
            {
                return currentDecision;
            }
        }
    }

    public static void PublishCurrentDecision(WorkloadDecision decision)
    {
        lock (sync)
        {
            currentDecision = decision;
        }
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
        AndroidThermalSeverity androidThermalSeverity,
        WorkloadMode currentMode)
    {
        if (androidThermalSeverity >= AndroidThermalSeverity.Critical)
        {
            return WorkloadMode.Critical;
        }

        if (androidThermalSeverity >= AndroidThermalSeverity.Severe)
        {
            return WorkloadMode.Hot;
        }

        if (androidThermalSeverity >= AndroidThermalSeverity.Moderate)
        {
            return WorkloadMode.Warm;
        }

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

        return powerSaveMode ||
               androidThermalSeverity == AndroidThermalSeverity.Light
            ? WorkloadMode.Conserve
            : WorkloadMode.Normal;
    }

    public enum AndroidThermalSeverity
    {
        Unknown = -1,
        None = 0,
        Light = 1,
        Moderate = 2,
        Severe = 3,
        Critical = 4,
        Emergency = 5,
        Shutdown = 6
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
        int GroundProbeIntervalMultiplier,
        int MaximumGroundProbesPerSweep,
        int FloodMaskWidthPixels,
        int FloodRefreshMilliseconds,
        float BatteryTemperatureCelsius,
        bool PowerSaveMode,
        AndroidThermalSeverity ThermalSeverity)
    {
        public static WorkloadDecision Normal =>
            Evaluate(
                float.NaN,
                false,
                AndroidThermalSeverity.Unknown,
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
