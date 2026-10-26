using System.Text.RegularExpressions;
using RescuAR.App.Models;

namespace RescuAR.App.Services.Authentication;

internal static class AuthenticationFormPolicy
{
    public const int MinimumPasswordLength = 10;
    public static bool HasSpecialCharacter(string? value) => Regex.IsMatch(value ?? string.Empty, @"[!@#$%^&*()]");
    public static bool HasDigit(string? value) => Regex.IsMatch(value ?? string.Empty, @"\d");
    public static bool HasUppercase(string? value) => Regex.IsMatch(value ?? string.Empty, @"[A-Z]");
    public static bool ValidEmail(string? value)
    {
        try { return Regex.IsMatch(value?.Trim() ?? string.Empty, @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public static string? PasswordError(string? password, string? confirmation)
    {
        password ??= string.Empty;
        confirmation ??= string.Empty;
        if (password.Length < MinimumPasswordLength || !HasSpecialCharacter(password) || !HasDigit(password) || !HasUppercase(password))
            return "Use at least 10 characters, an uppercase letter, a number, and one of !@#$%^&*().";
        return password != confirmation ? "Passwords do not match." : null;
    }

    // Names come from dev's existing Marikina barangay catalog, including Calumpang.
    public static IReadOnlyList<string> MarikinaBarangays { get; } = new[]
    {
        "Barangka", "Calumpang", "Concepcion Dos", "Concepcion Uno", "Fortune",
        "Industrial Valley", "Jesus Dela Peña", "Malanday", "Marikina Heights", "Nangka",
        "Parang", "San Roque", "Santa Elena", "Santo Niño", "Tañong", "Tumana"
    };

    public static string? ResidentError(ResidentProfileDraft draft, DateTime today)
    {
        if (string.IsNullOrWhiteSpace(draft.FirstName)) return "First name is required.";
        if (string.IsNullOrWhiteSpace(draft.LastName)) return "Last name is required.";
        if (string.IsNullOrWhiteSpace(draft.PhoneNumber)) return "Mobile number is required.";
        if (string.IsNullOrWhiteSpace(draft.HouseNumber)) return "House / lot / block number is required.";
        if (string.IsNullOrWhiteSpace(draft.StreetName)) return "Street name is required.";
        if (!MarikinaBarangays.Contains(draft.Barangay, StringComparer.OrdinalIgnoreCase)) return "Select your Marikina City barangay.";
        // The age restriction was explicitly deferred. Only future dates are rejected.
        return draft.Birthday.Date > today.Date ? "Birthday cannot be in the future." : null;
    }
}
