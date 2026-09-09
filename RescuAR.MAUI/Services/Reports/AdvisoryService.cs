using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using RescuAR.App.Models;
using RescuAR.Services;

namespace RescuAR.App.Services.Reports;

public class AdvisoryService
{
    private const string LogTag = "RescuAR-AlertOverlay";

    private Supabase.Client? GetClient()
    {
        return SupabaseService.Instance.Client;
    }

    /// <summary>
    /// UI-facing advisory fetch. Falls back to local sample advisories if the
    /// remote source is unavailable so existing dashboard/feed screens retain
    /// their current offline behavior.
    /// </summary>
    public async Task<List<DisasterAdvisory>> GetAdvisoriesAsync()
    {
        List<DisasterAdvisory>? remote =
            await TryGetRemoteAdvisoriesAsync();

        if (remote is { Count: > 0 })
        {
            return remote;
        }

        return GetFallbackAdvisories();
    }

    /// <summary>
    /// Remote-only fetch used by the real-time advisory listener.
    ///
    /// Important: a network failure returns null instead of the local fallback
    /// rows. This prevents offline/fallback data from being misclassified as a
    /// newly pushed emergency advisory.
    /// </summary>
    public async Task<List<DisasterAdvisory>?> TryGetRemoteAdvisoriesAsync()
    {
        Supabase.Client? client =
            GetClient();

        if (client is null)
        {
            return null;
        }

        try
        {
            var response =
                await client
                    .From<DisasterAdvisory>()
                    .Order(
                        "created_at",
                        Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();

            return response.Models?.ToList() ??
                   new List<DisasterAdvisory>();
        }
        catch (Exception exception)
        {
#if ANDROID
            Android.Util.Log.Warn(
                LogTag,
                $"Remote advisory fetch unavailable: {exception.Message}");
#else
            System.Diagnostics.Debug.WriteLine(
                $"Remote advisory fetch unavailable: {exception.Message}");
#endif
            return null;
        }
    }

    private static List<DisasterAdvisory> GetFallbackAdvisories()
    {
        /*
         * Stable IDs are deliberate. These rows are display fallbacks, not
         * remote push events. Stable identities also prevent callers from
         * interpreting a newly allocated fallback object as a new advisory.
         */
        return new List<DisasterAdvisory>
        {
            new()
            {
                Id = "fallback-flood-warning-level2",
                Title = "FLOOD WARNING — Marikina River Level 2",
                Message = "Water level has continuously risen to 16.5 meters. Residents in low-lying areas near Barangay Tumana and Malanday are advised to prepare emergency kits for possible evacuation.",
                WaterLevel = 16.5,
                AlertLevel = "Warning",
                Severity = "High",
                Category = "Flood",
                AffectedArea = "Barangay Tumana / Malanday",
                CreatedAt = DateTime.UtcNow.AddMinutes(-20)
            },
            new()
            {
                Id = "fallback-flood-standby-level1",
                Title = "Marikina River Alert Level 1 (Standby)",
                Message = "Marikina River water level reached 15.0 meters due to continuous moderate rain upstream. Disaster Response teams are on standby.",
                WaterLevel = 15.0,
                AlertLevel = "Standby",
                Severity = "Low",
                Category = "Flood",
                AffectedArea = "Marikina Riverbanks",
                CreatedAt = DateTime.UtcNow.AddHours(-3)
            }
        };
    }
}

/// <summary>
/// Existing application-wide advisory poller.
///
/// Stage 6 reuses this manager as the single emergency-advisory source for the
/// dashboard, advisory feed, and AR Camera. No second alert service/bridge is
/// introduced.
/// </summary>
public static class RealtimeAdvisoryManager
{
    private const string LogTag =
        "RescuAR-AlertOverlay";

    private static string? _lastAdvisoryId;
    private static readonly AdvisoryService _service =
        new();

    private static IDispatcherTimer? _timer;
    private static int _pollInProgress;

#if ANDROID
    private static Android.Media.MediaPlayer? _activePlayer;
#endif

    public static event Action<DisasterAdvisory>?
        OnNewAdvisoryPushed;

    public static void PlayAlarmAudio()
    {
#if ANDROID
        try
        {
            StopAlarmAudio();

            using var asset =
                Android.App.Application.Context.Assets?
                    .OpenFd("ndrrmc_alarm.ogg");

            if (asset is null)
            {
                Android.Util.Log.Warn(
                    LogTag,
                    "NDRRMC alarm asset 'ndrrmc_alarm.ogg' was not found.");

                return;
            }

            _activePlayer =
                new Android.Media.MediaPlayer();

            _activePlayer.SetDataSource(
                asset.FileDescriptor,
                asset.StartOffset,
                asset.Length);

            _activePlayer.Prepare();
            _activePlayer.Start();

            _activePlayer.Completion +=
                (_, _) =>
                {
                    try
                    {
                        _activePlayer?.Release();
                    }
                    catch
                    {
                    }
                    finally
                    {
                        _activePlayer =
                            null;
                    }
                };
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn(
                LogTag,
                $"Unable to play NDRRMC alarm audio: {exception.Message}");
        }
#endif
    }

    public static void StopAlarmAudio()
    {
#if ANDROID
        try
        {
            Android.Media.MediaPlayer? player =
                _activePlayer;

            _activePlayer =
                null;

            if (player is null)
            {
                return;
            }

            if (player.IsPlaying)
            {
                player.Stop();
            }

            player.Release();
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn(
                LogTag,
                $"Unable to stop NDRRMC alarm audio: {exception.Message}");
        }
#endif
    }

    public static void StartRealtimeListener()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer =
            Application.Current?.Dispatcher.CreateTimer();

        if (_timer is null)
        {
            return;
        }

        _timer.Interval =
            TimeSpan.FromSeconds(3);

        _timer.Tick +=
            async (_, _) =>
            {
                /*
                 * DispatcherTimer uses an async event handler. Prevent a slow
                 * network request from overlapping the next 3-second poll.
                 */
                if (Interlocked.Exchange(
                        ref _pollInProgress,
                        1) == 1)
                {
                    return;
                }

                try
                {
                    List<DisasterAdvisory>? advisories =
                        await _service.TryGetRemoteAdvisoriesAsync();

                    if (advisories is null ||
                        advisories.Count == 0)
                    {
                        return;
                    }

                    DisasterAdvisory? latest =
                        advisories.FirstOrDefault();

                    if (latest is null ||
                        string.IsNullOrWhiteSpace(latest.Id))
                    {
                        return;
                    }

                    if (latest.Id == _lastAdvisoryId)
                    {
                        return;
                    }

                    bool isInitialRemoteSnapshot =
                        _lastAdvisoryId is null;

                    _lastAdvisoryId =
                        latest.Id;

                    if (isInitialRemoteSnapshot)
                    {
#if ANDROID
                        Android.Util.Log.Debug(
                            LogTag,
                            "Real-time advisory listener established its initial remote baseline; no popup emitted.");
#endif
                        return;
                    }

#if ANDROID
                    Android.Util.Log.Info(
                        LogTag,
                        "NEW REMOTE EMERGENCY ADVISORY: " +
                        $"id='{latest.Id}', " +
                        $"level='{latest.DisplayAlertLevel}', " +
                        $"category='{latest.Category}', " +
                        $"title='{latest.Title}'.");
#endif

                    MainThread.BeginInvokeOnMainThread(
                        () =>
                        {
                            PlayAlarmAudio();

                            OnNewAdvisoryPushed?.Invoke(
                                latest);
                        });
                }
                catch (Exception exception)
                {
#if ANDROID
                    Android.Util.Log.Warn(
                        LogTag,
                        $"Real-time advisory poll failed: {exception.Message}");
#endif
                }
                finally
                {
                    Volatile.Write(
                        ref _pollInProgress,
                        0);
                }
            };

        _timer.Start();

#if ANDROID
        Android.Util.Log.Debug(
            LogTag,
            "Existing real-time advisory listener started (3-second remote polling). Stage 6 Camera integration will consume the same event source.");
#endif
    }
}
