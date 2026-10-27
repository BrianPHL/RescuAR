namespace RescuAR.App.Services.Storage;

public class CachedEvacuationCenterItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Barangay { get; set; } = string.Empty;
    public string Classification { get; set; } = "Unspecified";
    public string Address { get; set; } = string.Empty;
    public string VerifiedBy { get; set; } = "Not recorded";
    public string FacilityImageUrl { get; set; } = string.Empty;
    public string MapImageSource { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Capacity { get; set; }
    public int CurrentEvacuees { get; set; }
    public string Status { get; set; } = "Unknown";
    public string HeadOfficer { get; set; } = "Unassigned";
    public string Contact { get; set; } = "N/A";
    public List<string> Facilities { get; set; } = new();
}

/// <summary>
/// DTO for emergency hotline items in local offline cache.
/// </summary>
public class CachedEmergencyHotlineItem
{
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string BadgeText { get; set; } = "EMS";
}

/// <summary>
/// Envelope containing the cached evacuation center records and sync metadata.
/// </summary>
public class EvacuationCenterCacheEnvelope
{
    public string SourceKey { get; set; } = string.Empty;
    public bool DirectoryConfirmed { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }
    public List<CachedEvacuationCenterItem> Centers { get; set; } = new();
    public List<CachedEmergencyHotlineItem> Hotlines { get; set; } = new();
}
