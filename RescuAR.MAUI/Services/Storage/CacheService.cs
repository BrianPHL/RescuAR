using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Maui.Storage;
using Newtonsoft.Json;

namespace RescuAR.App.Services.Storage;

/// <summary>
/// Data Transfer Object representing an evacuation center in local offline storage.
/// </summary>
public class CachedEvacuationCenterItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Barangay { get; set; } = string.Empty;
    public string Classification { get; set; } = "Flood-Safe Major";
    public string Address { get; set; } = string.Empty;
    public string VerifiedBy { get; set; } = "Marikina LGU";
    public string FacilityImageUrl { get; set; } = string.Empty;
    public string MapImageSource { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public int Capacity { get; set; } = 500;
    public int CurrentEvacuees { get; set; }
    public string Status { get; set; } = "Standby";
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
    public DateTimeOffset LastUpdatedUtc { get; set; }
    public List<CachedEvacuationCenterItem> Centers { get; set; } = new();
    public List<CachedEmergencyHotlineItem> Hotlines { get; set; } = new();
}

/// <summary>
/// Service providing persistent offline local cache for Evacuation Center module.
/// </summary>
public static class CacheService
{
    private const string CacheFileName = "evacuation_centers_cache.json";
    private const string LastUpdatedPrefKey = "EvacuationCenters_LastUpdatedUtc";
    private const string HasCachePrefKey = "EvacuationCenters_HasSavedCache";

    private static string GetCacheFilePath() =>
        Path.Combine(FileSystem.AppDataDirectory, CacheFileName);

    /// <summary>
    /// Checks whether local saved evacuation center data exists.
    /// </summary>
    public static bool HasSavedEvacuationData()
    {
        try
        {
            var path = GetCacheFilePath();
            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length > 10) return true;
            }
        }
        catch { }

        return Preferences.Default.Get(HasCachePrefKey, false);
    }

    /// <summary>
    /// Loads the most recent successfully retrieved evacuation centers from local storage.
    /// </summary>
    public static EvacuationCenterCacheEnvelope? LoadEvacuationCenters()
    {
        try
        {
            var path = GetCacheFilePath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var envelope = JsonConvert.DeserializeObject<EvacuationCenterCacheEnvelope>(json);
                    if (envelope != null && envelope.Centers != null && envelope.Centers.Count > 0)
                    {
                        return envelope;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheService] Error reading evacuation cache: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Saves the latest successfully loaded evacuation center information to local storage.
    /// Uses atomic write semantics to prevent file corruption.
    /// </summary>
    public static void SaveEvacuationCenters(
        IEnumerable<CachedEvacuationCenterItem> centers,
        IEnumerable<CachedEmergencyHotlineItem>? hotlines,
        DateTimeOffset timestamp)
    {
        if (centers == null) return;
        var centerList = centers.ToList();
        if (centerList.Count == 0) return; // Never overwrite with empty results!

        try
        {
            var envelope = new EvacuationCenterCacheEnvelope
            {
                LastUpdatedUtc = timestamp,
                Centers = centerList,
                Hotlines = hotlines?.ToList() ?? new List<CachedEmergencyHotlineItem>()
            };

            string json = JsonConvert.SerializeObject(envelope, Formatting.Indented);
            var path = GetCacheFilePath();
            var tempPath = path + ".tmp";

            try
            {
                File.WriteAllText(tempPath, json);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(tempPath, path);
            }
            catch
            {
                // Fallback to direct write if temp file move fails
                File.WriteAllText(path, json);
            }

            Preferences.Default.Set(LastUpdatedPrefKey, timestamp.ToUnixTimeSeconds());
            Preferences.Default.Set(HasCachePrefKey, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CacheService] Error saving evacuation cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Retrieves the timestamp when the evacuation center data was last successfully updated.
    /// </summary>
    public static DateTimeOffset? GetLastUpdatedTime()
    {
        try
        {
            long epochSeconds = Preferences.Default.Get(LastUpdatedPrefKey, 0L);
            if (epochSeconds > 0)
            {
                return DateTimeOffset.FromUnixTimeSeconds(epochSeconds);
            }

            var envelope = LoadEvacuationCenters();
            if (envelope != null && envelope.LastUpdatedUtc != default)
            {
                return envelope.LastUpdatedUtc;
            }
        }
        catch { }

        return null;
    }
}
