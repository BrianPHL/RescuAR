using System;
using System.Threading;
using RescuAR.Diagnostics;
using RescuAR.Navigation.Models;

namespace RescuAR.AR;

/// <summary>
/// One presentation owner for a computed MLD route. Evidence producers may
/// withhold a frame, but cannot substitute a different navigation arrow.
/// </summary>
public static class MldGuidanceBridge
{
    public enum Phase { Inactive, Preparing, Following, Recovering, SafePrefix, Blocked, Arrived }

    public sealed record Snapshot(bool Active, long Trip, string Geometry, long Calibration,
        Phase State, bool AllowsPath, long Window, long Session, long Ground, long Epoch, long World,
        string Message, string Cause)
    {
        public static Snapshot Inactive { get; } = new(false, 0, "", 0, Phase.Inactive,
            false, -1, 0, 0, 0, 0, "", "");
    }

    private static readonly object sync = new();
    private static Snapshot current = Snapshot.Inactive;
    private static RouteResult? selectedRoute;
    public static Snapshot Current => Volatile.Read(ref current);

    public static bool IsMld(string algorithm) => algorithm.Contains("MLD", StringComparison.OrdinalIgnoreCase);

    public static void SetRouteOwner(RouteResult? route, long trip)
    {
        lock (sync)
        {
            selectedRoute = route;
            if (route is null || !IsMld(route.Algorithm)) { Install(Snapshot.Inactive); return; }
            Install(new(true, trip, route.GeometryFingerprint, 0, Phase.Preparing, false,
                -1, 0, 0, 0, 0, "Preparing mapped guidance — use the map", "new MLD route"));
        }
    }

    public static Snapshot Publish(RouteResult source, long trip, long calibration,
        ARRouteBridge.RouteSnapshot window, ARCameraPoseBridge.SpatialSnapshot frame,
        bool allowsPath, string message, bool hazardBlocked = false, bool arrived = false)
    {
        lock (sync)
        {
            var owner = current;
            if (!Owns(owner, source, trip) || owner.State == Phase.Arrived) return owner;
            bool qualified = !arrived && allowsPath && BoundWindowMatches(owner, window, frame, calibration);
            Phase phase = arrived ? Phase.Arrived : hazardBlocked
                ? qualified ? Phase.SafePrefix : Phase.Blocked
                : qualified ? Phase.Following : Phase.Recovering;
            if (allowsPath && !qualified && !arrived)
                message = "Restoring mapped guidance — use the map";
            Install(owner with { State = phase, AllowsPath = qualified, Message = message,
                Cause = "combined guidance evidence" });
            return current;
        }
    }

    /// <summary>
    /// Binds only windows produced by the selected route's registered publisher.
    /// A qualified publication advances the same owner's window immediately;
    /// normal GPS updates must not wait for the next UI timer to become drawable.
    /// This cannot promote a recovering/blocked owner or cross calibration/world changes.
    /// </summary>
    public static void RebindPublishedWindow(RouteResult source, long trip, long calibration,
        ARRouteBridge.RouteSnapshot window, ARCameraPoseBridge.SpatialSnapshot frame,
        bool hazardBlocked, string? hazardMessage)
    {
        lock (sync)
        {
            var owner = current;
            if (!Owns(owner, source, trip) || owner.State == Phase.Arrived || !WindowMatches(window, frame)) return;
            bool retained = owner.AllowsPath && owner.Calibration == calibration &&
                owner.Session == frame.Generation.SessionGeneration && owner.Epoch == frame.TrackingEpoch &&
                owner.World == frame.WorldFrame;
            Install(owner with { Window = window.Version, Calibration = calibration,
                Session = frame.Generation.SessionGeneration, Ground = frame.Anchor.ReferenceGeneration,
                Epoch = frame.TrackingEpoch, World = frame.WorldFrame, AllowsPath = retained,
                State = retained ? hazardBlocked ? Phase.SafePrefix : Phase.Following :
                    owner.AllowsPath ? Phase.Recovering : owner.State,
                Message = retained && hazardBlocked ? hazardMessage ?? owner.Message :
                    owner.AllowsPath && !retained ? "Restoring mapped guidance — use the map" : owner.Message,
                Cause = "registered mapped window publication" });
        }
    }

    public static void Suppress(string cause)
    {
        lock (sync)
        {
            if (!current.Active || !current.AllowsPath) return;
            bool hazard = current.State == Phase.SafePrefix;
            Install(current with { AllowsPath = false,
                State = hazard ? Phase.Blocked : Phase.Recovering,
                Message = hazard ? current.Message : "Restoring mapped guidance — use the map", Cause = cause });
        }
    }

    public static void MarkArrived(long trip)
    {
        lock (sync)
        {
            if (!current.Active || current.Trip != trip) return;
            Install(current with { State = Phase.Arrived, AllowsPath = false,
                Message = "Arrival confirmed — guidance completed", Cause = "arrival confirmed" });
        }
    }

    public static bool AppliesTo(ARRouteBridge.RouteSnapshot window) => Current.Active || IsMld(window.Algorithm);
    public static bool AllowsStandaloneArrow => !Current.Active && !IsMld(ARRouteBridge.Current.Algorithm);

    public static bool CanRenderPath(ARRouteBridge.RouteSnapshot window,
        ARCameraPoseBridge.SpatialSnapshot frame, long selectedTrip)
    {
        var owner = Current;
        return owner.Active && owner.AllowsPath && owner.Trip == selectedTrip &&
            BoundWindowMatches(owner, window, frame, owner.Calibration);
    }

    private static bool Owns(Snapshot owner, RouteResult source, long trip) =>
        owner.Active && ReferenceEquals(selectedRoute, source) && owner.Trip == trip &&
        owner.Geometry == source.GeometryFingerprint && IsMld(source.Algorithm);

    private static bool BoundWindowMatches(Snapshot owner, ARRouteBridge.RouteSnapshot window,
        ARCameraPoseBridge.SpatialSnapshot frame, long calibration) =>
        owner.Window == window.Version && owner.Calibration == calibration &&
        owner.Session == frame.Generation.SessionGeneration && owner.Ground == frame.Anchor.ReferenceGeneration &&
        owner.Epoch == frame.TrackingEpoch && owner.World == frame.WorldFrame && WindowMatches(window, frame);

    private static bool WindowMatches(ARRouteBridge.RouteSnapshot window, ARCameraPoseBridge.SpatialSnapshot frame) =>
        window.IsAvailable && IsMld(window.Algorithm) && window.Placement.IsAvailable &&
        frame.IsFresh && frame.IsTracking && frame.Pose.IsTracking && frame.Anchor.IsAvailable &&
        frame.Generation.IsValid && ARRenderGenerationBridge.IsCurrentSession(frame.Generation) &&
        frame.Anchor.GroundState.RenderGeneration.SessionGeneration == frame.Generation.SessionGeneration &&
        window.Generation.SessionGeneration == frame.Generation.SessionGeneration && window.Placement.IsFor(frame);

    private static void Install(Snapshot next)
    {
        if (current == next) return;
        Volatile.Write(ref current, next);
        AndroidLog.Debug("RescuAR-ARRoute", $"NAV_MLD_STATE trip={next.Trip}; geometry={next.Geometry}; " +
            $"state={next.State}; path={next.AllowsPath}; window={next.Window}; calibration={next.Calibration}; " +
            $"session={next.Session}; ground={next.Ground}; epoch={next.Epoch}; world={next.World}; " +
            $"message='{next.Message}'; cause='{next.Cause}'.");
    }
}
