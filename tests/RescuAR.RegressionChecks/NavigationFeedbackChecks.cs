using System.Collections.Concurrent;
using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Guidance;

internal static class NavigationFeedbackChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-26T13:00:00Z");
        var route = new object();
        var turn = new PedestrianTurnGuidanceService.TurnGuidanceSnapshot(true, PedestrianTurnGuidanceService.TurnInstruction.Right, 25, 90, 100, "Turn right");
        var frame = new NavigationFeedbackFrame(7, route, true, true, false, turn, 10, "Turn right ahead.", "Selected center", now);
        var speech = new Speech(); var pulses = new List<TimeSpan>();
        var feedback = new NavigationFeedbackController(speech, pulses.Add);
        await feedback.UpdateAsync(frame);
        check(speech.Texts.IsEmpty && pulses.Count == 0, "navigation voice and vibration cannot start before their configured preferences allow them");
        feedback.Configure(true, false);
        await feedback.UpdateAsync(frame with { Active = false });
        check(speech.Texts.IsEmpty, "a hidden, stopped or Flood view cannot speak navigation instructions");
        await feedback.UpdateAsync(frame with { Qualified = false });
        check(speech.Texts.IsEmpty, "unqualified location/progress cannot start spoken navigation");
        await feedback.UpdateAsync(frame);
        check(speech.Texts.Count == 1 && speech.Texts.Last() == frame.Text, "enabled feedback speaks the supplied qualified dev instruction");
        await feedback.UpdateAsync(frame with { Now = now.AddSeconds(1), ProgressMeters = 11, Guidance = turn with { DistanceToTurnMeters = 24 } });
        check(speech.Texts.Count == 1, "routine progress updates do not repeat the same spoken cue");
        await feedback.UpdateAsync(frame with { Now = now.AddSeconds(2), Guidance = turn with { DistanceToTurnMeters = 15 } });
        check(speech.Texts.Count == 1, "near-turn band changes respect the minimum speech interval");
        await feedback.UpdateAsync(frame with { Now = now.AddSeconds(6), Guidance = turn with { DistanceToTurnMeters = 15 } });
        check(speech.Texts.Count == 2 && pulses.Count == 0, "a meaningful later turn-distance band can speak while haptics stay independently disabled");
        await feedback.UpdateAsync(frame with { Now = now.AddSeconds(7), ArrivalConfirmed = true, Qualified = false, Text = "untrusted ordinary text" });
        check(speech.Texts.Count == 3 && speech.Texts.Last().StartsWith("Arrival confirmed at Selected center"),
            "dev-confirmed arrival cancels ordinary guidance and can announce immediately without a current turn");
        await feedback.UpdateAsync(frame with { Now = now.AddSeconds(8), ArrivalConfirmed = true });
        check(speech.Texts.Count == 3, "unchanged confirmed-arrival frames do not repeatedly announce arrival");

        speech = new Speech(); pulses = new(); feedback = new(speech, pulses.Add); feedback.Configure(false, true);
        var near = frame with { Guidance = turn with { DistanceToTurnMeters = 18 } };
        await feedback.UpdateAsync(near);
        check(pulses.Count == 1 && pulses[0] == TimeSpan.FromMilliseconds(180) && speech.Texts.IsEmpty,
            "the shared haptic preference can produce one short qualified near-turn pulse while voice is Off");
        await feedback.UpdateAsync(near with { Now = now.AddSeconds(1), ProgressMeters = 11, Guidance = turn with { DistanceToTurnMeters = 17 } });
        check(pulses.Count == 1, "GPS jitter around the same turn cannot produce repeated vibration");
        await feedback.UpdateAsync(near with { Qualified = false, RouteIdentity = new object(), Now = now.AddSeconds(7) });
        check(pulses.Count == 1, "an unqualified replacement route cannot vibrate merely because it has a new identity");
        await feedback.UpdateAsync(near with { RouteIdentity = new object(), Now = now.AddSeconds(8) });
        check(pulses.Count == 2, "a separately accepted route owns its own qualified turn pulse");
        feedback.Configure(false, false);
        await feedback.UpdateAsync(near with { RouteIdentity = new object(), Now = now.AddSeconds(10) });
        check(pulses.Count == 2, "turning the shared haptic switch Off suppresses subsequent turn pulses");
        feedback.Configure(false, true);
        await feedback.UpdateAsync(near with { ArrivalConfirmed = true, Now = now.AddSeconds(11) });
        check(pulses.Count == 2, "arrival confirmation cannot be mistaken for a near-turn vibration");

        foreach (string cancellation in new[] { "mute", "uncertain", "destination", "exit", "app stop" })
        {
            speech = new Speech { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously) };
            feedback = new(speech, _ => { }); feedback.Configure(true, false);
            var speaking = feedback.UpdateAsync(frame);
            check(speech.Texts.Count == 1, "a cancellable speech request starts for the " + cancellation + " case");
            switch (cancellation)
            {
                case "mute": feedback.Configure(false, false); break;
                case "uncertain": await feedback.UpdateAsync(frame with { Qualified = false }); break;
                case "destination": await feedback.UpdateAsync(frame with { Trip = 8, RouteIdentity = null, Qualified = false }); break;
                default: feedback.Stop(); break;
            }
            await speaking.WaitAsync(TimeSpan.FromSeconds(2));
            check(speech.Tokens.First().IsCancellationRequested && speech.Active == 0,
                cancellation + " cancels pending speech instead of leaving an old instruction queued");
        }

        speech = new Speech { Hold = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreCancellation = true };
        feedback = new(speech, _ => { }); feedback.Configure(true, false);
        var old = feedback.UpdateAsync(frame);
        var next = feedback.UpdateAsync(frame with { Trip = 8, RouteIdentity = new object(), Now = now.AddSeconds(1), Text = "New trip instruction" });
        check(speech.Texts.Count == 1 && speech.Tokens.First().IsCancellationRequested, "a replacement trip cancels its predecessor and waits for the single speaker");
        speech.Hold.SetResult();
        await Task.WhenAll(old, next).WaitAsync(TimeSpan.FromSeconds(2));
        check(speech.Texts.Count == 2 && speech.MaximumActive == 1 && speech.Texts.Last() == "New trip instruction",
            "two trip updates never speak concurrently even when a provider is slow to stop");

        speech = new Speech { Fail = true }; feedback = new(speech, _ => { }); feedback.Configure(true, false);
        await feedback.UpdateAsync(frame);
        check(feedback.LastError?.Contains("Voice unavailable") == true, "a real speech failure exposes written-directions fallback instead of silently claiming working audio");
        await feedback.UpdateAsync(frame with { Now = now.AddSeconds(10) });
        check(speech.Texts.Count == 1, "a failed provider is not called again on every UI tick");
        feedback.Configure(false, false); feedback.Configure(true, false); speech.Fail = false;
        await feedback.UpdateAsync(frame with { Now = now.AddSeconds(11) });
        check(feedback.LastError is null && speech.Texts.Count == 2, "turning voice Off/On explicitly allows a fresh provider attempt");

        speech = new Speech(); int attempts = 0; feedback = new(speech, _ => { attempts++; throw new InvalidOperationException("no vibration device"); });
        feedback.Configure(false, true); await feedback.UpdateAsync(near);
        await feedback.UpdateAsync(near with { RouteIdentity = new object(), Now = now.AddSeconds(6) });
        check(attempts == 1 && feedback.LastError?.Contains("Turn vibration unavailable") == true,
            "unsupported haptics report a fallback and do not fail repeatedly on each route update");
        await feedback.UpdateAsync(frame with { Trip = 0, ArrivalConfirmed = true });
        check(speech.Texts.IsEmpty, "an absent navigation trip cannot manufacture an arrival announcement");
    }

    private sealed class Speech : INavigationSpeech
    {
        public ConcurrentQueue<string> Texts { get; } = new();
        public ConcurrentQueue<CancellationToken> Tokens { get; } = new();
        public TaskCompletionSource? Hold;
        public bool IgnoreCancellation, Fail;
        public int Active, MaximumActive;
        public async Task SpeakAsync(string text, CancellationToken token)
        {
            Texts.Enqueue(text); Tokens.Enqueue(token); Active++; MaximumActive = Math.Max(MaximumActive, Active);
            try
            {
                if (Fail) throw new IOException("test speech provider failure");
                if (Hold is { } pending)
                {
                    if (IgnoreCancellation) await pending.Task;
                    else await pending.Task.WaitAsync(token);
                }
            }
            finally { Active--; }
        }
    }
}
