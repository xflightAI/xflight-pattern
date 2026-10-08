namespace XFlight.Pattern.Core.PhaseDetection;

public enum DetectionCycleOutcome { Accepted, Rejected, Interrupted }
public enum SampleRejectionReason { DuplicateSequence, ReversedSequence, ReversedSourceTime, EpochMismatch, ImplausibleRelocation }
public enum SampleInterruptionReason { SampleContinuityGap, PositionContinuityLost }
public enum TelemetryQualityIssue { MissingPosition, MissingAltitude, MissingGroundSpeed, MissingGroundTrack, MissingVerticalSpeed, MissingOnGround, ContinuityGap }
public enum PhaseCueKind { Entry, BaseTurn, Final, GoAround, Exit }

/// <summary>Ordinal gives stable order among events emitted for the same source sample.</summary>
public sealed record DetectionEventIdentity
{
    public DetectionEventIdentity(string algorithmId, string configurationId, string streamEpoch,
        ulong sampleSequence, uint ordinal)
    {
        Identity.Required(algorithmId, nameof(algorithmId));
        Identity.Required(configurationId, nameof(configurationId));
        Identity.Required(streamEpoch, nameof(streamEpoch));
        if (sampleSequence == 0) throw new ArgumentOutOfRangeException(nameof(sampleSequence));
        AlgorithmId = algorithmId; ConfigurationId = configurationId; StreamEpoch = streamEpoch;
        SampleSequence = sampleSequence; Ordinal = ordinal;
    }
    public string AlgorithmId { get; }
    public string ConfigurationId { get; }
    public string StreamEpoch { get; }
    public ulong SampleSequence { get; }
    public uint Ordinal { get; }
}

public sealed record PhaseTransitionEvent
{
    public PhaseTransitionEvent(DetectionEventIdentity identity, TimeSpan sourceTime,
        PatternPhase from, PatternPhase to, DetectionAttemptIdentity? attemptId)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (sourceTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceTime));
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to) || from == to) throw new ArgumentException("A transition requires distinct defined phases.");
        if (attemptId is not null && (attemptId.AlgorithmId != identity.AlgorithmId ||
            attemptId.ConfigurationId != identity.ConfigurationId || attemptId.StreamEpoch != identity.StreamEpoch ||
            attemptId.StartSampleSequence > identity.SampleSequence))
            throw new ArgumentException("Attempt identity must belong to the event stream.", nameof(attemptId));
        Identity = identity; SourceTime = sourceTime; From = from; To = to; AttemptId = attemptId;
    }
    public DetectionEventIdentity Identity { get; }
    public TimeSpan SourceTime { get; }
    public PatternPhase From { get; }
    public PatternPhase To { get; }
    public DetectionAttemptIdentity? AttemptId { get; }
}

public sealed record PhaseCueEvent
{
    /// <summary>A retrospective cue refers to the confirmed transition that made it eligible.</summary>
    public PhaseCueEvent(DetectionEventIdentity identity, TimeSpan sourceTime, PhaseCueKind kind, PhaseTransitionEvent confirmedTransition)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(confirmedTransition);
        if (sourceTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceTime));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        var eligiblePhase = kind switch
        {
            PhaseCueKind.Entry => PatternPhase.Entry,
            PhaseCueKind.BaseTurn => PatternPhase.Base,
            PhaseCueKind.Final => PatternPhase.Final,
            PhaseCueKind.GoAround => PatternPhase.GoAround,
            PhaseCueKind.Exit => PatternPhase.Exited,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (confirmedTransition.To != eligiblePhase || confirmedTransition.AttemptId is null ||
            confirmedTransition.Identity.AlgorithmId != identity.AlgorithmId ||
            confirmedTransition.Identity.ConfigurationId != identity.ConfigurationId ||
            confirmedTransition.Identity.StreamEpoch != identity.StreamEpoch ||
            confirmedTransition.Identity.SampleSequence != identity.SampleSequence ||
            confirmedTransition.Identity.Ordinal >= identity.Ordinal || confirmedTransition.SourceTime != sourceTime)
            throw new ArgumentException("Cue requires an earlier eligible confirmed transition in the same cycle.", nameof(confirmedTransition));
        Identity = identity; SourceTime = sourceTime; Kind = kind; ConfirmedTransition = confirmedTransition;
    }
    public DetectionEventIdentity Identity { get; }
    public TimeSpan SourceTime { get; }
    public PhaseCueKind Kind { get; }
    public PhaseTransitionEvent ConfirmedTransition { get; }
    public DetectionAttemptIdentity AttemptId => ConfirmedTransition.AttemptId!;
}

public sealed record PhaseDetectionResult
{
    public PhaseDetectionResult(TelemetrySnapshot snapshot, PhaseDetectionState state,
        DetectionCycleOutcome outcome, SampleRejectionReason? rejectionReason,
        IEnumerable<TelemetryQualityIssue> qualityIssues,
        IEnumerable<PhaseTransitionEvent> transitions, IEnumerable<PhaseCueEvent> cues,
        SampleInterruptionReason? interruptionReason = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(qualityIssues);
        ArgumentNullException.ThrowIfNull(transitions);
        ArgumentNullException.ThrowIfNull(cues);
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if ((outcome == DetectionCycleOutcome.Rejected) != rejectionReason.HasValue ||
            (rejectionReason.HasValue && !Enum.IsDefined(rejectionReason.Value)) ||
            (outcome == DetectionCycleOutcome.Interrupted) != interruptionReason.HasValue ||
            (interruptionReason.HasValue && !Enum.IsDefined(interruptionReason.Value)))
            throw new ArgumentException("Cycle outcome and reason must agree.");
        if (snapshot.StreamEpoch != state.StreamEpoch &&
            rejectionReason != SampleRejectionReason.EpochMismatch)
            throw new ArgumentException("Snapshot and state epochs differ without an epoch-mismatch rejection.");
        if (outcome != DetectionCycleOutcome.Rejected &&
            (state.LastSampleSequence != snapshot.SampleSequence || state.LastSourceTime != snapshot.SourceTime))
            throw new ArgumentException("Consumed snapshot identity and state cursor must agree.", nameof(state));
        var issues = qualityIssues.ToArray();
        var phaseEvents = transitions.ToArray();
        var cueEvents = cues.ToArray();
        if (issues.Any(issue => !Enum.IsDefined(issue))) throw new ArgumentOutOfRangeException(nameof(qualityIssues));
        if (phaseEvents.Any(e => e is null) || cueEvents.Any(e => e is null)) throw new ArgumentException("Events cannot be null.");
        var identities = phaseEvents.Select(e => e.Identity).Concat(cueEvents.Select(e => e.Identity)).ToArray();
        if (identities.Any(id => id.StreamEpoch != snapshot.StreamEpoch || id.SampleSequence != snapshot.SampleSequence ||
            id.AlgorithmId != state.AlgorithmId || id.ConfigurationId != state.ConfigurationId) ||
            identities.Select(id => id.Ordinal).Distinct().Count() != identities.Length ||
            !identities.Select(id => id.Ordinal).SequenceEqual(identities.Select(id => id.Ordinal).Order()))
            throw new ArgumentException("Event identities must match this cycle and have unique ordered ordinals.");
        if (phaseEvents.Any(e => e.SourceTime != snapshot.SourceTime) || cueEvents.Any(e => e.SourceTime != snapshot.SourceTime))
            throw new ArgumentException("Event source time must match the sample.");
        for (var i = 1; i < phaseEvents.Length; i++)
            if (phaseEvents[i - 1].To != phaseEvents[i].From)
                throw new ArgumentException("Transitions must form a phase chain.", nameof(transitions));
        if (phaseEvents.Length > 0 && (phaseEvents[^1].To != state.ConfirmedPhase ||
            state.TransitionCount < phaseEvents.Length))
            throw new ArgumentException("Transitions must end at the confirmed phase and fit the cumulative count.", nameof(transitions));
        if (cueEvents.Any(c => !phaseEvents.Contains(c.ConfirmedTransition)))
            throw new ArgumentException("Every cue must reference a transition in this cycle.", nameof(cues));
        if (cueEvents.Any(c => c.AttemptId != state.AttemptId || !IsLatched(state.CueLatches, c.Kind)))
            throw new ArgumentException("Emitted cues require the matching active attempt and set latches.", nameof(cues));
        if (outcome != DetectionCycleOutcome.Accepted && identities.Length != 0)
            throw new ArgumentException("Rejected or interrupted cycles cannot emit events.");
        if (outcome == DetectionCycleOutcome.Accepted)
        {
            var positionAvailable = snapshot.Position.Validity == TelemetryValidity.Available;
            if (issues.Contains(TelemetryQualityIssue.MissingPosition) == positionAvailable)
                throw new ArgumentException("Position quality issue must reflect snapshot availability.", nameof(qualityIssues));
            if (positionAvailable)
            {
                var anchor = state.LastAcceptedPosition;
                if (state.PositionContinuity != PositionContinuity.Continuous || anchor is null ||
                    anchor.SampleSequence != snapshot.SampleSequence || anchor.SourceTime != snapshot.SourceTime ||
                    anchor.Position != snapshot.Position.Value)
                    throw new ArgumentException("Accepted valid position must become the current continuous anchor.", nameof(state));
            }
            else if (state.LastAcceptedPosition is not null &&
                (state.PositionContinuity != PositionContinuity.Interrupted ||
                 state.LastAcceptedPosition.SampleSequence >= snapshot.SampleSequence))
                throw new ArgumentException("Unavailable position must interrupt the retained position anchor.", nameof(state));
        }
        Snapshot = snapshot; State = state; Outcome = outcome; RejectionReason = rejectionReason; InterruptionReason = interruptionReason;
        QualityIssues = Array.AsReadOnly(issues); Transitions = Array.AsReadOnly(phaseEvents); Cues = Array.AsReadOnly(cueEvents);
    }
    public TelemetrySnapshot Snapshot { get; }
    public PhaseDetectionState State { get; }
    public DetectionCycleOutcome Outcome { get; }
    public SampleRejectionReason? RejectionReason { get; }
    public SampleInterruptionReason? InterruptionReason { get; }
    public IReadOnlyList<TelemetryQualityIssue> QualityIssues { get; }
    public IReadOnlyList<PhaseTransitionEvent> Transitions { get; }
    public IReadOnlyList<PhaseCueEvent> Cues { get; }

    private static bool IsLatched(PhaseCueLatches latches, PhaseCueKind kind) => kind switch
    {
        PhaseCueKind.Entry => latches.Entry,
        PhaseCueKind.BaseTurn => latches.BaseTurn,
        PhaseCueKind.Final => latches.Final,
        PhaseCueKind.GoAround => latches.GoAround,
        PhaseCueKind.Exit => latches.Exit,
        _ => false
    };
}
