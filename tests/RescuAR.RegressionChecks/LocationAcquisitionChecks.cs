using RescuAR.MAUI.Services.Location;
using RescuAR.Navigation.Models;

internal static class LocationAcquisitionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var at = DateTimeOffset.UtcNow;
        LocationReading Fix(string provider, double accuracy, double age = 0) =>
            new(new GeoCoordinate(14.6448,121.095), accuracy, null, 1.2, 275, at.AddSeconds(-age)) { Provider=provider };
        var first = Fix("network", 11);
        check(!LocationAcquisitionPolicy.CanComplete(first, at, TimeSpan.Zero),
            "acquisition: the recorded 11m first result does not end Best acquisition immediately");
        var precise = Fix("gps", 4);
        check(ReferenceEquals(LocationAcquisitionPolicy.Select(precise, first, at), precise) &&
            LocationAcquisitionPolicy.CanComplete(precise, at, TimeSpan.FromMilliseconds(400)),
            "acquisition: a later precise GPS result wins without waiting for the full timeout");
        check(!LocationAcquisitionPolicy.CanComplete(Fix("gps", 35), at, TimeSpan.FromMilliseconds(100)),
            "acquisition: even a GPS provider cannot use MAUI's first-within-50m shortcut");
        check(LocationAcquisitionPolicy.CanComplete(first, at.AddSeconds(2), TimeSpan.FromSeconds(2)) &&
            first.AccuracyMeters == 11 && first.Timestamp == at && first.Provider == "network",
            "acquisition: a bounded fallback preserves the real uncertainty, provider and acquisition time");
        check(ReferenceEquals(LocationAcquisitionPolicy.Select(Fix("gps", 4, 5), first, at), first) &&
            LocationAcquisitionPolicy.Select(Fix("gps", 4, 5), Fix("network", 7, 4), at) is null,
            "acquisition: a more precise stale fix cannot replace current evidence or become fresh again");
        check(ReferenceEquals(LocationAcquisitionPolicy.Select(Fix("gps", 20), first, at), first),
            "acquisition: provider name alone cannot override better reported positioning quality");
        check(!LocationAcquisitionPolicy.IsCurrent(Fix("gps", double.NaN), at) &&
            !LocationAcquisitionPolicy.IsCurrent(Fix("gps", 0), at) &&
            !LocationAcquisitionPolicy.IsCurrent(Fix("gps", -1), at) &&
            !LocationAcquisitionPolicy.IsCurrent(Fix("gps", 5, -1), at),
            "acquisition: unknown/invalid accuracy and future measurements are rejected");
        var poorCourse = precise with { CourseAccuracyDegrees = 80 };
        check(poorCourse.QualifiedCourseDegrees is null &&
            (precise with { CourseAccuracyDegrees = 5 }).QualifiedCourseDegrees == 275 &&
            precise.QualifiedCourseDegrees == 275,
            "matching: explicitly inaccurate bearing is withheld while good and legacy unknown-accuracy courses are preserved");
        check(LocationAcquisitionPolicy.Select(precise with { Timestamp = at.AddSeconds(-1) },
            Fix("network", 4), at)!.Timestamp == at,
            "acquisition: equally precise observations use the newer acquisition time");
    }
}
