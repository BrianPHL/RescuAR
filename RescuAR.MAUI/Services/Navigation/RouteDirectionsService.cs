using RescuAR.Navigation.Guidance;
using RescuAR.Navigation.Models;

namespace RescuAR.MAUI.Services.Navigation;

public sealed record RouteDirectionStep(
    double AtProgressMeters,
    PedestrianTurnGuidanceService.TurnInstruction Instruction,
    string InstructionText,
    string SubtitleText,
    string DistanceText,
    string IconSource);

/// <summary>Presentation of the canonical route using dev's turn classifier.</summary>
public sealed class RouteDirectionsService
{
    private readonly PedestrianTurnGuidanceService guidance = new();

    public IReadOnlyList<RouteDirectionStep> Build(RouteResult route, string destinationName)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route.Points.Count < 2 || route.Points.Any(point => !point.Coordinate.IsValid ||
                !double.IsFinite(point.DistanceFromStartMeters))) return [];

        double start = route.Points[0].DistanceFromStartMeters;
        double end = route.Points[^1].DistanceFromStartMeters;
        if (end <= start || !double.IsFinite(route.TotalDistanceMeters)) return [];
        for (int index = 1; index < route.Points.Count; index++)
            if (route.Points[index].DistanceFromStartMeters < route.Points[index - 1].DistanceFromStartMeters) return [];
        List<(double Progress, PedestrianTurnGuidanceService.TurnInstruction Instruction)> turns = [];
        // Sample the retained geometry, rather than treating every polyline vertex
        // as a maneuver. Dense curves and repeated points use the same classifier
        // as live guidance. Directions are estimates, not OSRM street metadata.
        for (double cursor = start; cursor < end - 6; cursor += 5)
        {
            var turn = guidance.Evaluate(route, cursor);
            if (!turn.IsAvailable || !IsTurn(turn.Instruction) ||
                !double.IsFinite(turn.DistanceToTurnMeters)) continue;
            double at = cursor + turn.DistanceToTurnMeters;
            if (turns.Count > 0 && at - turns[^1].Progress < 15) continue;
            turns.Add((at, turn.Instruction));
            cursor = at + 5;
        }

        List<RouteDirectionStep> steps = [];
        double firstLeg = (turns.Count > 0 ? turns[0].Progress : end) - start;
        steps.Add(new(start, PedestrianTurnGuidanceService.TurnInstruction.Continue,
            "Follow the mapped route", "From the route start; check access from your location.",
            $"{Math.Max(0, firstLeg):0} m", Icon(PedestrianTurnGuidanceService.TurnInstruction.Continue)));
        for (int index = 0; index < turns.Count; index++)
        {
            var turn = turns[index];
            double next = index + 1 < turns.Count ? turns[index + 1].Progress : end;
            steps.Add(new(turn.Progress, turn.Instruction, Text(turn.Instruction),
                "Continue along the mapped route after this turn.",
                $"{Math.Max(0, next - turn.Progress):0} m", Icon(turn.Instruction)));
        }
        string name = string.IsNullOrWhiteSpace(destinationName) ? "the selected destination" : destinationName;
        steps.Add(new(end, PedestrianTurnGuidanceService.TurnInstruction.Arrive,
            $"Destination ahead: {name}", "Follow posted signs; arrival requires location confirmation.",
            "", Icon(PedestrianTurnGuidanceService.TurnInstruction.Arrive)));
        return steps;
    }

    public static bool IsTurn(PedestrianTurnGuidanceService.TurnInstruction instruction) => instruction is
        PedestrianTurnGuidanceService.TurnInstruction.SlightLeft or
        PedestrianTurnGuidanceService.TurnInstruction.Left or
        PedestrianTurnGuidanceService.TurnInstruction.SharpLeft or
        PedestrianTurnGuidanceService.TurnInstruction.SlightRight or
        PedestrianTurnGuidanceService.TurnInstruction.Right or
        PedestrianTurnGuidanceService.TurnInstruction.SharpRight or
        PedestrianTurnGuidanceService.TurnInstruction.UTurn;

    public static string Text(PedestrianTurnGuidanceService.TurnInstruction instruction) => instruction switch
    {
        PedestrianTurnGuidanceService.TurnInstruction.SlightLeft => "Bear left",
        PedestrianTurnGuidanceService.TurnInstruction.Left => "Turn left",
        PedestrianTurnGuidanceService.TurnInstruction.SharpLeft => "Sharp left",
        PedestrianTurnGuidanceService.TurnInstruction.SlightRight => "Bear right",
        PedestrianTurnGuidanceService.TurnInstruction.Right => "Turn right",
        PedestrianTurnGuidanceService.TurnInstruction.SharpRight => "Sharp right",
        PedestrianTurnGuidanceService.TurnInstruction.UTurn => "Follow the route back",
        PedestrianTurnGuidanceService.TurnInstruction.Arrive => "Destination ahead",
        _ => "Continue along the mapped route"
    };

    public static string Icon(PedestrianTurnGuidanceService.TurnInstruction instruction) => instruction switch
    {
        PedestrianTurnGuidanceService.TurnInstruction.SlightLeft => "lucide_arrow_up_left_teal.png",
        PedestrianTurnGuidanceService.TurnInstruction.Left => "lucide_corner_up_left_teal.png",
        PedestrianTurnGuidanceService.TurnInstruction.SharpLeft => "lucide_corner_up_left_teal.png",
        PedestrianTurnGuidanceService.TurnInstruction.SlightRight => "lucide_arrow_up_right_teal.png",
        PedestrianTurnGuidanceService.TurnInstruction.Right => "lucide_corner_up_right_teal.png",
        PedestrianTurnGuidanceService.TurnInstruction.SharpRight => "lucide_corner_up_right_teal.png",
        PedestrianTurnGuidanceService.TurnInstruction.UTurn => "lucide_undo_2_green.png",
        PedestrianTurnGuidanceService.TurnInstruction.Arrive => "lucide_circle_check_big_teal.png",
        _ => "lucide_arrow_up_teal.png"
    };
}
