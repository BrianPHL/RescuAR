using RescuAR.MAUI.Services.Navigation;

internal static class CameraViewHandoffChecks
{
    public static void Run(Action<bool, string> check)
    {
        var handoff = new CameraViewHandoff();
        check(!handoff.TryConsume(1, out _), "ordinary Camera entry has no unsolicited presentation request");
        int notified = 0; handoff.Requested += () => notified++;
        handoff.Request(6, CameraPresentationView.Map2D);
        check(handoff.TryConsume(6, out var view) && view == CameraPresentationView.Map2D && notified == 1, "an explicit 2D request reaches the intended trip and notifies an already-open Camera");
        check(!handoff.TryConsume(6, out _), "a consumed 2D request cannot force a later Camera visit back into 2D");
        handoff.Request(6, CameraPresentationView.Map2D);
        check(!handoff.TryConsume(7, out _) && !handoff.TryConsume(6, out _), "a changed or cleared trip invalidates the previous display request");
        long old = handoff.Request(7, CameraPresentationView.Map2D);
        handoff.Request(8, CameraPresentationView.Ar);
        handoff.Cancel(old);
        check(handoff.TryConsume(8, out view) && view == CameraPresentationView.Ar, "explicit AR entry wins over an older 2D request and survives cancellation of the old navigation");
        long cancelled = handoff.Request(9, CameraPresentationView.Map2D); handoff.Cancel(cancelled);
        check(!handoff.TryConsume(9, out _), "failed navigation leaves no pending display request");
        handoff.Request(0, CameraPresentationView.Map2D);
        check(handoff.TryConsume(0, out view) && view == CameraPresentationView.Map2D, "the directory map may be browsed without selecting a destination");
    }
}
