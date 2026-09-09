using System;
using System.Collections.Generic;

namespace RescuAR.App.Services.Translation;

public static class AdvisoryTranslationService
{
    public static string TranslateAlertLevel(string? alertLevel)
    {
        if (string.IsNullOrWhiteSpace(alertLevel)) return "Nakasubaybay";
        var lower = alertLevel.Trim().ToLowerInvariant();
        if (lower.Contains("critical") || lower.Contains("level 3") || lower.Contains("evacuate") || lower.Contains("high"))
            return "Kritikal - Lumikas Agad";
        if (lower.Contains("warning") || lower.Contains("level 2") || lower.Contains("alarm") || lower.Contains("medium") || lower.Contains("moderate"))
            return "Babala - Maghanda sa Paglikas";
        if (lower.Contains("standby") || lower.Contains("level 1") || lower.Contains("alert") || lower.Contains("low"))
            return "Nakasubaybay";
        return alertLevel;
    }

    public static string TranslateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string translated = text;

        // Common exact phrases
        translated = ReplaceWord(translated, "Waist-deep Flood Water along J.P. Rizal St.", "Lalim-baywang na Baha sa kahabaan ng J.P. Rizal St.");
        translated = ReplaceWord(translated, "Flood waters reaching waist level near Malanday market area.", "Baha na umaabot sa baywang malapit sa pampublikong pamilihan ng Malanday.");
        translated = ReplaceWord(translated, "Passable only to heavy rescue trucks.", "Maaari lamang daanan ng malalaking sasakyang pampagligtas.");
        translated = ReplaceWord(translated, "Fallen Tree Blocking Entrance to Evacuation Center", "Bumagsak na Puno na Nakaharang sa Entrada ng Evacuation Center");
        translated = ReplaceWord(translated, "Large acacia branch down near H. Bautista Elem. Gate 2.", "Malaking sanga ng acacia ang nabuwal malapit sa Gate 2 ng H. Bautista Elementary School.");
        translated = ReplaceWord(translated, "Local LGU clearing operations underway.", "Kasalukuyang isinasagawa ang clearing operations ng lokal na pamahalaan (LGU).");
        translated = ReplaceWord(translated, "No additional description provided.", "Walang karagdagang paglalarawan na ibinigay.");

        // General terms
        translated = ReplaceWord(translated, "Waist-deep Flood Water", "Lalim-baywang na Baha");
        translated = ReplaceWord(translated, "Flood Water", "Tubig-Baha");
        translated = ReplaceWord(translated, "Flood waters", "Tubig-baha");
        translated = ReplaceWord(translated, "Fallen Tree", "Bumagsak na Puno");
        translated = ReplaceWord(translated, "Evacuation Center", "Evacuation Center");
        translated = ReplaceWord(translated, "Evacuation Order", "Utos ng Paglilikas");
        translated = ReplaceWord(translated, "Evacuate immediately", "Lumikas agad");
        translated = ReplaceWord(translated, "Water Level", "Antas ng Tubig");
        translated = ReplaceWord(translated, "Heavy Rainfall", "Malakas na Ulan");
        translated = ReplaceWord(translated, "Passable only", "Madaanan lamang");
        translated = ReplaceWord(translated, "Passable", "Madaanan");
        translated = ReplaceWord(translated, "Not passable", "Hindi madaanan");
        translated = ReplaceWord(translated, "Emergency", "Pang-emerhensya");
        translated = ReplaceWord(translated, "Advisory", "Abiso");
        translated = ReplaceWord(translated, "Warning", "Babala");
        translated = ReplaceWord(translated, "Caution", "Pag-iingat");
        translated = ReplaceWord(translated, "Danger", "Panganib");
        translated = ReplaceWord(translated, "Safe", "Ligtas");
        translated = ReplaceWord(translated, "Marikina City", "Lungsod ng Marikina");

        return translated;
    }

    private static string ReplaceWord(string input, string search, string replacement)
    {
        return input.Replace(search, replacement, StringComparison.OrdinalIgnoreCase);
    }
}
