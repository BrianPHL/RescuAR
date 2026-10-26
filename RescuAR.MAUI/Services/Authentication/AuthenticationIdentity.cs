using Newtonsoft.Json;

namespace RescuAR.App.Services.Authentication;

internal interface IIdentityPreferences
{
    bool ContainsKey(string key);
    string Get(string key);
    void Set(string key, string value);
    void Remove(string key);
    void MarkSignedOut();
}

internal static class AuthenticationIdentity
{
    // Legacy aliases remain relevant while the individual Athena screens are ported.
    private static readonly string[] activeKeys =
    [
        "CurrentUserId", "current_user_id", "UserId", "CurrentAuthProject", "UserEmail", "UserName",
        "UserFirstName", "UserMiddleName", "UserLastName", "UserPhone", "UserPhoneNumber",
        "UserContactNumber", "UserAddress", "UserHouseNumber", "UserHouseLot", "UserStreet",
        "UserStreetName", "UserBarangay", "UserCity", "UserBirthday", "UserAvatarUrl",
        "UserAvatarPublicUrl", "UserProfilePicPath", "EmergencyContact1Name",
        "EmergencyContact1Phone", "EmergencyContact1Relationship", "EmergencyContact1Rel",
        "EmergencyContact2Name", "EmergencyContact2Phone", "EmergencyContact2Relationship",
        "EmergencyContact2Rel", "CustomQuickActionsList_v18", "SelectedCircleId",
        "LocalSafetyCircles", "UserJoinedCircleIds", "UserDeletedCircleIds"
    ];

    public static void ClearActive(IIdentityPreferences preferences, string? authenticatedUserHint)
    {
        preferences.MarkSignedOut();
        var values = new Dictionary<string, string>();
        foreach (string key in activeKeys)
            if (preferences.ContainsKey(key)) values[key] = preferences.Get(key);

        if (values.Count > 0)
        {
            // Preserve legacy data without treating its uncertain ownership as verified.
            // Only the later account-migration flow may restore this archive.
            string archive = JsonConvert.SerializeObject(new
            {
                AuthenticatedUserHint = authenticatedUserHint,
                CapturedAt = DateTimeOffset.UtcNow,
                Values = values
            });
            preferences.Set($"AuthIdentityArchive:{Guid.NewGuid():N}", archive);
        }

        foreach (string key in activeKeys) preferences.Remove(key);
    }
}
