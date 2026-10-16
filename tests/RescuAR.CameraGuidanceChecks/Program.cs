using RescuAR.MAUI.Services.Navigation;
using RescuAR.Navigation.Data;
using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;
using RescuAR.Navigation.Routing;

int passed = 0;
void Check(bool condition, string behavior)
{
    if (!condition) throw new InvalidOperationException(behavior);
    passed++;
    Console.WriteLine($"PASS: {behavior}");
}
GeoCoordinate C(double east, double north = 0) => new(north / 111195.0, east / 111195.0);
RouteResult Route(params GeoCoordinate[] coordinates)
{
    List<RoutePoint> points = [];
    double distance = 0;
    for (int index = 0; index < coordinates.Length; index++)
    {
        if (index > 0) distance += coordinates[index - 1].DistanceTo(coordinates[index]);
        points.Add(new(coordinates[index], distance));
    }
    return new(points, distance, "Test route");
}
var directions = new RouteDirectionsService();
var shortRoute = Route(C(0), C(4));
var shortSteps = directions.Build(shortRoute, "Selected school");
Check(shortSteps.Count == 2 && shortSteps[^1].Instruction == PedestrianTurnGuidanceService.TurnInstruction.Arrive,
    "two-point short routes include both a start and destination");
Check(shortSteps[^1].InstructionText.Contains("Selected school") &&
    !shortSteps[^1].InstructionText.Contains("arrived", StringComparison.OrdinalIgnoreCase),
    "a geometry endpoint identifies the selected destination without claiming confirmed arrival");
var straight = Route(Enumerable.Range(0, 30).Select(index => C(index * 5)).ToArray());
Check(directions.Build(straight, "School").Count == 2, "dense straight geometry produces one walking leg");
var right = Route(C(0), C(0, 70), C(80, 70));
var rightSteps = directions.Build(right, "School");
Check(rightSteps.Any(step => step.Instruction == PedestrianTurnGuidanceService.TurnInstruction.Right),
    "directions retain the canonical right-turn classification");
Check(rightSteps.Count == 3, "one corner is consolidated into one maneuver");
var leftSteps = directions.Build(Route(C(0), C(0, 70), C(-80, 70)), "School");
Check(leftSteps.Any(step => step.Instruction == PedestrianTurnGuidanceService.TurnInstruction.Left),
    "directions retain the canonical left-turn classification");
Check(rightSteps.Zip(rightSteps.Skip(1)).All(pair => pair.First.AtProgressMeters <= pair.Second.AtProgressMeters),
    "directions follow forward progress on the retained route");
Check(right.Points.Count == 3 && right.Points[1].Coordinate == C(0, 70),
    "building a directions sheet preserves the original route geometry");
Check(directions.Build(Route(C(0)), "School").Count == 0, "single-point routes have no walking directions");
Check(directions.Build(new RouteResult([new(C(0), 0), new(new GeoCoordinate(double.NaN, 0), 10), new(C(50), 50)], 50, "Malformed"), "School").Count == 0,
    "invalid interior locations cannot produce shortcut directions");
Check(directions.Build(new RouteResult([new(C(0), 0), new(C(20), 20), new(C(50), 10)], 50, "Malformed"), "School").Count == 0,
    "backward cumulative distances are rejected");
Check(directions.Build(Route(C(0), C(0), C(0, 70), C(80, 70)), "School").Count == 3,
    "repeated vertices do not duplicate turn instructions");
Check(RouteDirectionsService.IsTurn(PedestrianTurnGuidanceService.TurnInstruction.SharpLeft) &&
    RouteDirectionsService.IsTurn(PedestrianTurnGuidanceService.TurnInstruction.SharpRight),
    "sharp turns remain available in both directions and announcements");

var now = DateTimeOffset.UtcNow;
PedestrianTurnGuidanceService.TurnGuidanceSnapshot Turn(double distance,
    PedestrianTurnGuidanceService.TurnInstruction instruction = PedestrianTurnGuidanceService.TurnInstruction.Right) =>
    new(true, instruction, distance, 90, 100, "Turn right");
var recorder = new RecordingSpeech();
var voice = new NavigationVoiceGuidanceService(recorder);
object routeIdentity = new();
await voice.UpdateAsync(routeIdentity, Turn(30), 0, "Turn right in 30 meters", now);
Check(recorder.Messages.Count == 0, "voice starts muted until explicitly enabled");
voice.SetEnabled(true);
await voice.UpdateAsync(routeIdentity, Turn(30), 0, "Turn right in 30 meters", now);
Check(recorder.Messages.Count == 0, "inactive navigation cannot speak even with voice enabled");
voice.SetActive(true);
await voice.UpdateAsync(routeIdentity, Turn(30), 0, "Turn right in 30 meters", now);
Check(recorder.Messages.Count == 1, "active navigation speaks the first available instruction");
for (int index = 1; index < 9; index++)
    await voice.UpdateAsync(routeIdentity, Turn(30 - index), index, $"Turn right in {30 - index} meters", now.AddSeconds(index));
Check(recorder.Messages.Count == 1, "changing each displayed meter does not repeat the same instruction");
await voice.UpdateAsync(routeIdentity, Turn(19), 11, "Turn right in 19 meters", now.AddSeconds(11));
Check(recorder.Messages.Count == 2, "a meaningful near-turn threshold is announced once");
await voice.UpdateAsync(routeIdentity, Turn(8), 22, "Turn right in 8 meters", now.AddSeconds(12));
Check(recorder.Messages.Count == 2, "rapid threshold changes respect the announcement cooldown");
await voice.UpdateAsync(routeIdentity, Turn(7), 23, "Turn right in 7 meters", now.AddSeconds(17));
Check(recorder.Messages.Count == 3, "the next imminent-turn cue remains eligible after the cooldown");
await voice.UpdateAsync(new object(), Turn(7), 23, "New route", now.AddSeconds(18));
Check(recorder.Messages.Count == 4, "route replacement invalidates announcements from the previous route");
voice.SetActive(false);
await voice.UpdateAsync(new object(), Turn(7), 23, "Hidden page", now.AddSeconds(30));
Check(recorder.Messages.Count == 4, "leaving the navigation page suppresses later instructions");
voice.SetActive(true);
voice.SetEnabled(false);
await voice.UpdateAsync(new object(), Turn(7), 23, "Muted", now.AddSeconds(40));
Check(recorder.Messages.Count == 4, "muting suppresses subsequent instructions");

var held = new HeldSpeech();
var heldVoice = new NavigationVoiceGuidanceService(held);
heldVoice.SetEnabled(true);
heldVoice.SetActive(true);
var firstSpeech = heldVoice.UpdateAsync(routeIdentity, Turn(30), 0, "First route", now);
await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));
heldVoice.SetActive(false);
await firstSpeech.WaitAsync(TimeSpan.FromSeconds(3));
Check(held.Cancellations == 1, "page exit cancels speech already in progress");
heldVoice.SetActive(true);
var nextSpeech = heldVoice.UpdateAsync(routeIdentity, Turn(30), 0, "Resumed route", now.AddSeconds(10));
heldVoice.SetEnabled(false);
await nextSpeech.WaitAsync(TimeSpan.FromSeconds(3));
Check(held.Cancellations == 2, "muting cancels an active announcement");
heldVoice.SetEnabled(true);
var resetSpeech = heldVoice.UpdateAsync(routeIdentity, Turn(30), 0, "Old destination", now.AddSeconds(20));
heldVoice.ResetRoute();
await resetSpeech.WaitAsync(TimeSpan.FromSeconds(3));
Check(held.Cancellations == 3, "clearing the destination cancels its active announcement");

var failure = new FailingSpeech();
var failedVoice = new NavigationVoiceGuidanceService(failure);
failedVoice.SetEnabled(true);
failedVoice.SetActive(true);
await failedVoice.UpdateAsync(routeIdentity, Turn(30), 0, "First", now);
Check(failedVoice.LastError is not null && failedVoice.LastError.Contains("written directions"),
    "missing speech engines report a written-directions fallback");
await failedVoice.UpdateAsync(new object(), Turn(30), 0, "Retry", now.AddSeconds(10));
Check(failure.Attempts == 1, "speech failures do not cause an automatic retry loop");
failedVoice.SetEnabled(false);
failedVoice.SetEnabled(true);
failure.Fail = false;
await failedVoice.UpdateAsync(routeIdentity, Turn(30), 0, "Recovered", now.AddSeconds(20));
Check(failure.Attempts == 2 && failedVoice.LastError is null, "explicitly enabling voice retries a recovered speech engine");

var serialized = new SerializedSpeech();
var serializedVoice = new NavigationVoiceGuidanceService(serialized);
serializedVoice.SetEnabled(true);
serializedVoice.SetActive(true);
Task[] updates = Enumerable.Range(0, 12).Select(index => serializedVoice.UpdateAsync(new object(),
    Turn(30), 0, $"Destination {index}", now.AddSeconds(index * 6))).ToArray();
await Task.WhenAll(updates).WaitAsync(TimeSpan.FromSeconds(5));
Check(serialized.MaximumConcurrent == 1, "rapid route changes never overlap physical speech requests");
Check(serialized.Completed.LastOrDefault() == "Destination 11", "only the latest queued destination completes its speech");

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await CameraOfflineMapDataService.LoadAsync(cancelled.Token);
    Check(false, "cancelled map waiter must stop");
}
catch (OperationCanceledException) { Check(true, "page cancellation stops its offline-map waiter"); }
var roads = await CameraOfflineMapDataService.LoadAsync();
Check(roads.Count > 100 && roads.All(line => line.Count >= 2 && line.All(point => point.IsValid)),
    "offline map context loads valid embedded road lines with no tile service");
var sameRoads = await CameraOfflineMapDataService.LoadAsync();
Check(ReferenceEquals(roads, sameRoads), "page cancellation preserves the shared embedded-map cache");
var graph = await NavigationDataBootstrap.GetRoadGraphAsync();
var edge = graph.Edges.First(edge => edge.LengthMeters > 10);
var remote = new ForbiddenOnlineRouter();
var offline = new HybridRoutingService(remote, NavigationDataBootstrap.GetRoadGraphAsync, () => false);
var offlineRoute = await offline.FindRouteAsync(edge.From.Coordinate, edge.To.Coordinate);
Check(remote.Calls == 0 && offlineRoute is { Points.Count: >= 2 },
    "navigation calculates a route without any online routing call");
Check(directions.Build(offlineRoute!, "Offline destination").Count >= 2,
    "an offline route supplies directions without AR tracking or network connectivity");
Check(offlineRoute!.Points.All(point => point.Coordinate.IsValid) && offlineRoute.Algorithm == "AStar",
    "offline guidance retains the existing A-star provider and valid geometry");

Console.WriteLine($"{passed} camera guidance checks passed.");

sealed class RecordingSpeech : INavigationSpeech
{
    public List<string> Messages { get; } = [];
    public Task SpeakAsync(string text, CancellationToken token) { token.ThrowIfCancellationRequested(); Messages.Add(text); return Task.CompletedTask; }
}
sealed class HeldSpeech : INavigationSpeech
{
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Cancellations;
    public async Task SpeakAsync(string text, CancellationToken token)
    {
        Started.TrySetResult();
        try { await Task.Delay(Timeout.Infinite, token); }
        catch (OperationCanceledException) { Interlocked.Increment(ref Cancellations); throw; }
    }
}
sealed class FailingSpeech : INavigationSpeech
{
    public int Attempts;
    public bool Fail = true;
    public Task SpeakAsync(string text, CancellationToken token)
    {
        Attempts++;
        if (Fail) throw new InvalidOperationException("No speech engine");
        return Task.CompletedTask;
    }
}
sealed class SerializedSpeech : INavigationSpeech
{
    private int concurrent;
    public int MaximumConcurrent;
    public List<string> Completed { get; } = [];
    public async Task SpeakAsync(string text, CancellationToken token)
    {
        int current = Interlocked.Increment(ref concurrent);
        MaximumConcurrent = Math.Max(MaximumConcurrent, current);
        try { await Task.Delay(40, token); Completed.Add(text); }
        finally { Interlocked.Decrement(ref concurrent); }
    }
}
sealed class ForbiddenOnlineRouter : IRoutingService
{
    public int Calls;
    public string AlgorithmName => "Online calls forbidden";
    public Task<RouteResult?> FindRouteAsync(GeoCoordinate origin, GeoCoordinate destination, CancellationToken cancellationToken = default)
    {
        Calls++;
        throw new InvalidOperationException("An offline check attempted online routing.");
    }
}
