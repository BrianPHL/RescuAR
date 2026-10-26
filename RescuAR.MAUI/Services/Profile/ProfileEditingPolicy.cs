using System.Linq.Expressions;
using RescuAR.App.Models;
using RescuAR.App.Services.Authentication;

namespace RescuAR.App.Services.Profile;

public readonly record struct ProfileAccountScope(string Project, string UserId, long Revision);
internal sealed record PersonalProfileValues(string Username, string FirstName, string MiddleName,
    string LastName, string PhoneNumber, string Address)
{
    public static PersonalProfileValues From(User user) => new(user.Username ?? "", user.FirstName ?? "",
        user.MiddleName ?? "", user.LastName ?? "", user.PhoneNumber ?? "", user.Address ?? "");
}
internal sealed record PersonalProfileEdit(string Username, string FirstName, string MiddleName,
    string LastName, string PhoneNumber, string Street, string Barangay);
internal sealed record HealthProfileValues(string Card, string BloodType, string Allergies,
    string Medications, string BloodPressure, string Needs, bool OrganDonor)
{
    public static HealthProfileValues From(User user) => new(user.HealthCardNumber ?? "", user.BloodType ?? "",
        user.Allergies ?? "", user.MaintenanceMedications ?? "", user.AverageBloodPressure ?? "",
        user.DisabilityOrSpecialNeeds ?? "", user.IsOrganDonor);
}

internal static class ProfileEditingPolicy
{
    public static IReadOnlyList<string> BloodTypes { get; } = new[] { "A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-" };

    public static (string Street, string Barangay, string City) ParseAddress(string address)
    {
        var parts = address.Split(',').Select(part => part.Trim()).ToArray();
        // Read the city/barangay from the right so commas in a street are retained.
        if (parts.Length >= 5)
            return (string.Join(", ", parts[..^4]), parts[^4], parts[^3]);
        if (parts.Length == 3) return (parts[0], parts[1], parts[2]);
        return (address, string.Empty, "Marikina City");
    }

    public static (Expression<Func<User, object>> Field, object Value)[] PersonalChanges(
        PersonalProfileValues original, PersonalProfileEdit edit)
    {
        if (string.IsNullOrWhiteSpace(edit.FirstName) || string.IsNullOrWhiteSpace(edit.LastName))
            throw new InvalidOperationException("First name and last name are required.");
        var fields = new List<(Expression<Func<User, object>> Field, object Value)>();
        Add(user => user.Username, original.Username, edit.Username.Trim());
        Add(user => user.FirstName, original.FirstName, edit.FirstName.Trim());
        Add(user => user.MiddleName, original.MiddleName, edit.MiddleName.Trim());
        Add(user => user.LastName, original.LastName, edit.LastName.Trim());
        Add(user => user.PhoneNumber, original.PhoneNumber, edit.PhoneNumber.Trim());
        var parts = ParseAddress(original.Address);
        if (edit.Street != parts.Street || edit.Barangay != parts.Barangay)
        {
            if (string.IsNullOrWhiteSpace(edit.Street) || !AuthenticationFormPolicy.MarikinaBarangays.Contains(edit.Barangay))
                throw new InvalidOperationException("Enter your street address and select a Marikina City barangay.");
            string address = $"{edit.Street.Trim()}, {edit.Barangay}, {parts.City}";
            var originalParts = original.Address.Split(',');
            if (originalParts.Length >= 5)
                address += $", {originalParts[^2].Trim()}, {originalParts[^1].Trim()}";
            Add(user => user.Address, original.Address, address);
        }
        return fields.ToArray();
        void Add(Expression<Func<User, object>> field, string before, string after)
        {
            if (before != after) fields.Add((field, after));
        }
    }

    public static (Expression<Func<User, object>> Field, object Value)[] HealthChanges(
        HealthProfileValues original, HealthProfileValues edit)
    {
        var fields = new List<(Expression<Func<User, object>> Field, object Value)>();
        Add(user => user.HealthCardNumber, original.Card, edit.Card.Trim());
        // Unknown existing values remain intact unless the resident edits them.
        if (original.BloodType != edit.BloodType && !string.IsNullOrWhiteSpace(edit.BloodType) && !BloodTypes.Contains(edit.BloodType))
            throw new InvalidOperationException("Select a blood type from the list, or leave it blank.");
        Add(user => user.BloodType, original.BloodType, edit.BloodType.Trim());
        Add(user => user.Allergies, original.Allergies, edit.Allergies.Trim());
        Add(user => user.MaintenanceMedications, original.Medications, edit.Medications.Trim());
        Add(user => user.AverageBloodPressure, original.BloodPressure, edit.BloodPressure.Trim());
        Add(user => user.DisabilityOrSpecialNeeds, original.Needs, edit.Needs.Trim());
        if (original.OrganDonor != edit.OrganDonor) fields.Add((user => user.IsOrganDonor, edit.OrganDonor));
        return fields.ToArray();
        void Add(Expression<Func<User, object>> field, string before, string after)
        {
            if (before != after) fields.Add((field, after));
        }
    }
}
