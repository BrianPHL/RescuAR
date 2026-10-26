using Newtonsoft.Json.Linq;

namespace RescuAR.App.Services.Profile;

internal static class ContactShortcutPolicy
{
    public static bool IsOwned(string id) => id is "emergency_contact_1" or "emergency_contact_2";
    public static string Identity(string name, string phone) => name.Trim().ToUpperInvariant() + "\n" +
        string.Concat(phone.Where(character => !char.IsWhiteSpace(character) && character is not ('-' or '(' or ')')));

    public static string Synchronize(string json, ContactValues values)
    {
        var source = JArray.Parse(json);
        if (source.Any(item => item is not JObject)) throw new InvalidOperationException("The saved shortcuts could not be read.");
        var rows = source.Cast<JObject>().ToArray();
        var desired = new List<(int Slot, string Name, string Phone)>();
        for (int slot = 1; slot <= 2; slot++)
        {
            var contact = values.Slot(slot);
            if (!string.IsNullOrWhiteSpace(contact.Name) && !string.IsNullOrWhiteSpace(contact.Phone))
                desired.Add((slot, contact.Name.Trim(), contact.Phone.Trim()));
        }
        var groups = desired.GroupBy(contact => Identity(contact.Name, contact.Phone));
        var replacements = new Dictionary<int, JObject>();
        var remove = new HashSet<int>();
        var appended = new List<JObject>();
        foreach (var group in groups)
        {
            var contact = group.First();
            var ids = group.Select(item => $"emergency_contact_{item.Slot}").ToHashSet();
            var candidates = Enumerable.Range(0, rows.Length).Where(index => ids.Contains((string?)rows[index]["Id"] ?? "") ||
                (!IsOwned((string?)rows[index]["Id"] ?? "") && (bool?)rows[index]["IsCustom"] == true &&
                 (string?)rows[index]["ActionType"] == "CustomContact" &&
                 Identity((string?)rows[index]["Title"] ?? "", (string?)rows[index]["PhoneNumber"] ?? "") == group.Key)).ToArray();
            var row = candidates.Length > 0 ? (JObject)rows[candidates[0]].DeepClone() : new JObject
            {
                ["IsEnabled"] = true, ["IconImage"] = "icon_hotlines.svg", ["IconBg"] = "#EFF6FF", ["IconColor"] = "#2563EB"
            };
            row["Id"] = $"emergency_contact_{contact.Slot}";
            row["Title"] = contact.Name; row["PhoneNumber"] = contact.Phone;
            row["Subtitle"] = $"Emergency Contact: {contact.Phone}";
            row["ActionType"] = "CustomContact"; row["IsCustom"] = true;
            if (candidates.Length == 0) appended.Add(row);
            else
            {
                replacements[candidates[0]] = row;
                foreach (int index in candidates) remove.Add(index);
            }
        }
        var result = new JArray();
        for (int index = 0; index < rows.Length; index++)
        {
            if (replacements.TryGetValue(index, out var row)) result.Add(row);
            else if (!remove.Contains(index) && !IsOwned((string?)rows[index]["Id"] ?? "")) result.Add(rows[index].DeepClone());
        }
        foreach (var row in appended) result.Add(row);
        return result.ToString(Newtonsoft.Json.Formatting.None);
    }
}
