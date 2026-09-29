using System;
using System.Collections.Generic;
using System.Globalization;
using RescuAR.Navigation.Models;

namespace RescuAR.Navigation.Guidance;

/// <summary>
/// Historical location fallback from the pre-field-testing builds. These entries
/// do not establish current opening status, occupancy, contacts, or facilities.
/// A successful online response replaces this fallback; saved data takes priority.
/// </summary>
public static class BundledEvacuationCenterCatalog
{
    public sealed record Center(string Name, string Barangay, string Classification,
        string Address, GeoCoordinate Coordinate);

    public static IReadOnlyList<Center> Centers { get; } = Array.AsReadOnly(new[]
    {
        new Center("Malanday Elementary School", "Malanday", "Flood-Safe Major",
            "48 Visayas St., Malanday, Marikina City", new(14.650283, 121.094409)),
        new Center("H. Bautista Elementary School", "Concepcion Uno", "Flood-Safe Major",
            "Liwasang Kalayaan, Concepcion Uno, Marikina City", new(14.657914, 121.104240)),
        new Center("Nangka Elementary School", "Nangka", "Flood-Safe Major",
            "Nangka, Marikina City", new(14.672991, 121.108440)),
        new Center("Concepcion Elementary School", "Concepcion Uno", "Flood-Safe Major",
            "Concepcion Uno, Marikina City", new(14.647648, 121.103974)),
        new Center("Sto. Niño Elementary School", "Sto. Niño", "Flood-Safe Major",
            "Sto. Niño, Marikina City", new(14.638324, 121.098368)),
        new Center("St. Mary Elem. School", "Parang", "Flood-Safe Minor",
            "Parang, Marikina City", new(14.668643, 121.113418)),
        new Center("Concepcion Integrated School ES", "Concepcion Uno", "Dual-Purpose Major",
            "Concepcion Uno, Marikina City", new(14.649954, 121.101893)),
        new Center("Fortune Elem. School Fields", "Fortune", "Dual-Purpose Minor",
            "Fortune, Marikina City", new(14.655000, 121.115000)),
        new Center("San Roque High School", "San Roque", "Earthquake-Safe Minor",
            "Nicanor Roxas St., San Roque, Marikina City", new(14.622798, 121.097105)),
    });

    public static bool IsUsableCoordinate(GeoCoordinate coordinate) =>
        coordinate.IsValid && (coordinate.Latitude != 0 || coordinate.Longitude != 0);

    public static bool TryParseCoordinate(string? latitude, string? longitude,
        out GeoCoordinate coordinate)
    {
        coordinate = default;
        if (!double.TryParse(latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat) ||
            !double.TryParse(longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out double lng))
            return false;
        coordinate = new(lat, lng);
        return IsUsableCoordinate(coordinate);
    }
}
