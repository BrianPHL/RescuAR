namespace RescuAR.App.Models;

// Only non-secret resident details travel with the signup verification flow.
public sealed class ResidentProfileDraft
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string MiddleName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public DateTime Birthday { get; set; }
    public string HouseNumber { get; set; } = string.Empty;
    public string StreetName { get; set; } = string.Empty;
    public string Barangay { get; set; } = string.Empty;
    public string Address => $"{HouseNumber.Trim()}, {StreetName.Trim()}, {Barangay.Trim()}, Marikina City, Metro Manila, 1800";
}
