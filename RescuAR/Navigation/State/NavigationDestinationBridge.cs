using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;
using System;

namespace RescuAR.Navigation.State;

/// <summary>
/// Cross-module handoff for the verified evacuation destination selected by
/// the MAUI application.
///
/// The Camera tab reads this state when it becomes active. A destination may
/// also be changed while Camera is visible; DestinationChanged lets Camera
/// request a new MLD route without polling.
/// </summary>
public static class NavigationDestinationBridge
{
    private const string LogTag =
        "RescuAR-MLD";

    private static readonly object sync =
        new();

    private static DestinationSnapshot current =
        DestinationSnapshot.Unavailable;

    public static event EventHandler? DestinationChanged;

    public static DestinationSnapshot Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public static bool Set(
        EvacuationCenter center)
    {
        ArgumentNullException.ThrowIfNull(
            center);

        if (center.Coordinate is not GeoCoordinate coordinate ||
            !coordinate.IsValid)
        {
            AndroidLog.Warn(
                LogTag,
                $"Destination rejected: '{center.Name}' has no verified coordinate.");

            return false;
        }

        Set(
            center.Name,
            coordinate);

        return true;
    }

    public static void Set(
        string name,
        GeoCoordinate coordinate)
    {
        if (!coordinate.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(coordinate));
        }

        DestinationSnapshot next =
            new(
                true,
                name ??
                    string.Empty,
                coordinate);

        lock (sync)
        {
            current =
                next;
        }

        AndroidLog.Debug(
            LogTag,
            "Navigation destination published: " +
            $"name='{next.Name}', " +
            $"lat={next.Coordinate.Latitude:F7}, " +
            $"lon={next.Coordinate.Longitude:F7}");

        DestinationChanged?.Invoke(
            null,
            EventArgs.Empty);
    }

    public static void Clear()
    {
        lock (sync)
        {
            current =
                DestinationSnapshot.Unavailable;
        }

        AndroidLog.Debug(
            LogTag,
            "Navigation destination cleared.");

        DestinationChanged?.Invoke(
            null,
            EventArgs.Empty);
    }

    public readonly record struct DestinationSnapshot(
        bool IsAvailable,
        string Name,
        GeoCoordinate Coordinate)
    {
        public static DestinationSnapshot Unavailable =>
            new(
                false,
                string.Empty,
                default);
    }
}
