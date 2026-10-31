using System;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Guidance;

/// <summary>
/// Eligibility for a separate user-reported arrival action. This is not GPS
/// confirmation of arrival or evidence that an evacuation facility is safe.
/// </summary>
public static class UserArrivalConfirmationPolicy
{
    public const double MaximumDistanceMeters = 15;
    public const double MaximumAccuracyMeters = 15;
    public static readonly TimeSpan MaximumFixAge = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumNoticeFixAge = MaximumFixAge;

    public static bool CanShowNotice(GeoCoordinate position, GeoCoordinate destination,
        double? accuracyMeters, DateTimeOffset observedAt, DateTimeOffset now) =>
        CanOffer(position, destination, accuracyMeters, observedAt, now) &&
        now - observedAt <= MaximumNoticeFixAge;

    public static bool CanOffer(GeoCoordinate position, GeoCoordinate destination,
        double? accuracyMeters, DateTimeOffset observedAt, DateTimeOffset now,
        bool majorRoadBarrier = false, bool observationHeld = false)
    {
        TimeSpan age = now - observedAt;
        return !majorRoadBarrier && !observationHeld && position.IsValid && destination.IsValid &&
            accuracyMeters is double accuracy && double.IsFinite(accuracy) &&
            accuracy >= 0 && accuracy <= MaximumAccuracyMeters &&
            age >= TimeSpan.FromSeconds(-2) && age <= MaximumFixAge &&
            position.DistanceTo(destination) <= MaximumDistanceMeters;
    }
}
