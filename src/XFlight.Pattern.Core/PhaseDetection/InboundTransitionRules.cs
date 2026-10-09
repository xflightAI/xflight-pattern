namespace XFlight.Pattern.Core.PhaseDetection;

internal static class InboundTransitionRules
{
    public static PatternPhase? Successor(PatternPhase phase) => phase switch
    {
        PatternPhase.Unknown => PatternPhase.Entry,
        PatternPhase.Entry => PatternPhase.Downwind,
        PatternPhase.Downwind => PatternPhase.Base,
        PatternPhase.Base => PatternPhase.Final,
        _ => null
    };

    // Slice 1 adds only initial departure; Departure has no successor until the crosswind slice.
    public static bool IsSupportedSuccessor(PatternPhase current, PatternPhase target) =>
        target == Successor(current) || current == PatternPhase.Unknown && target == PatternPhase.Departure;

    public static MovementRegionEvidence Region(GeometryMovementEvidence evidence, PatternPhase phase) => phase switch
    {
        PatternPhase.Entry => evidence.Entry,
        PatternPhase.Departure => evidence.DepartureUpwind,
        PatternPhase.Crosswind => evidence.Crosswind,
        PatternPhase.Downwind => evidence.Downwind,
        PatternPhase.Base => evidence.Base,
        PatternPhase.Final => evidence.Final,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    public static bool Qualifies(MovementRegionEvidence region) =>
        region.InsideEnterBounds && region.Direction.Status == DirectionEvidenceStatus.WithinEnterTolerance;

    public static bool Retains(MovementRegionEvidence region) =>
        region.InsideExitBounds && region.Direction.Status is
            DirectionEvidenceStatus.WithinEnterTolerance or DirectionEvidenceStatus.WithinExitTolerance;

    public static bool Unambiguous(GeometryMovementEvidence evidence, PatternPhase current, PatternPhase target)
    {
        if (!Qualifies(Region(evidence, target))) return false;
        if (current != PatternPhase.Unknown && Retains(Region(evidence, current))) return false;

        foreach (var phase in new[] { PatternPhase.Entry, PatternPhase.Departure, PatternPhase.Crosswind,
                     PatternPhase.Downwind, PatternPhase.Base, PatternPhase.Final })
            if (phase != target && Qualifies(Region(evidence, phase))) return false;

        return true;
    }
}
