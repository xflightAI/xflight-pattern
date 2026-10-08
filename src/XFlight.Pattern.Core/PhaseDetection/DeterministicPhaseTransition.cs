using XFlight.Pattern.Core.Geometry;

namespace XFlight.Pattern.Core.PhaseDetection;

/// <summary>Pure single-snapshot calculation. PhaseReplayContext owns its certified replay provenance.</summary>
internal static class DeterministicPhaseTransition
{
    /// <summary>Calculates from a locally checked state; PhaseReplayContext certifies its history.</summary>
    internal static PhaseDetectionResult Evaluate(PhaseDetectionConfiguration configuration,
        PhaseDetectionState previousState, TelemetrySnapshot snapshot,
        double minimumUsableGroundSpeedMetresPerSecond)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(previousState);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!double.IsFinite(minimumUsableGroundSpeedMetresPerSecond) ||
            minimumUsableGroundSpeedMetresPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumUsableGroundSpeedMetresPerSecond));
        ValidateCheckpoint(configuration, previousState);

        if (snapshot.StreamEpoch != previousState.StreamEpoch)
            return Rejected(previousState, snapshot, SampleRejectionReason.EpochMismatch);
        if (previousState.LastSampleSequence is { } sequence)
        {
            if (snapshot.SampleSequence == sequence)
                return Rejected(previousState, snapshot, SampleRejectionReason.DuplicateSequence);
            if (snapshot.SampleSequence < sequence)
                return Rejected(previousState, snapshot, SampleRejectionReason.ReversedSequence);
            if (snapshot.SourceTime < previousState.LastSourceTime)
                return Rejected(previousState, snapshot, SampleRejectionReason.ReversedSourceTime);
        }

        var issues = QualityIssues(snapshot);
        if (previousState.LastSourceTime is { } priorTime &&
            snapshot.SourceTime - priorTime > configuration.Policy.MaximumSampleGap)
        {
            issues.Add(TelemetryQualityIssue.ContinuityGap);
            return Interrupted(previousState, snapshot, issues, SampleInterruptionReason.SampleContinuityGap);
        }
        if (snapshot.Position.Validity == TelemetryValidity.Unavailable)
            return Interrupted(previousState, snapshot, issues, SampleInterruptionReason.PositionContinuityLost);

        if (IsImplausibleRelocation(configuration, previousState, snapshot))
            return Rejected(previousState, snapshot, SampleRejectionReason.ImplausibleRelocation);

        CandidateEvidence? candidate = null;
        var phase = previousState.ConfirmedPhase;
        var attempt = previousState.AttemptId;
        var transitionCount = previousState.TransitionCount;
        PhaseTransitionEvent[] transitions = [];
        var target = InboundTransitionRules.Successor(phase);
        if (target is { } next && snapshot.OnGround.Value == false)
        {
            var evidence = GeometryMovementEvidence.Evaluate(configuration, snapshot,
                minimumUsableGroundSpeedMetresPerSecond);
            if (InboundTransitionRules.Unambiguous(evidence, phase, next))
            {
                var old = previousState.Candidate;
                candidate = old is not null && old.Phase == next &&
                    snapshot.SourceTime - old.LastSourceTime <= configuration.Policy.MaximumCandidateGap
                    ? new CandidateEvidence(next, old.FirstSourceTime, snapshot.SourceTime,
                        checked(old.SampleCount + 1))
                    : new CandidateEvidence(next, snapshot.SourceTime, snapshot.SourceTime, 1);
                if (snapshot.SourceTime - candidate.FirstSourceTime >= configuration.Policy.CandidatePersistence)
                {
                    transitionCount = checked(transitionCount + 1);
                    if (phase == PatternPhase.Unknown)
                        attempt = new DetectionAttemptIdentity(configuration.AlgorithmId,
                            configuration.ConfigurationId, snapshot.StreamEpoch, snapshot.SampleSequence);
                    transitions = [new PhaseTransitionEvent(
                        new DetectionEventIdentity(configuration.AlgorithmId, configuration.ConfigurationId,
                            snapshot.StreamEpoch, snapshot.SampleSequence, 0),
                        snapshot.SourceTime, phase, next, attempt)];
                    phase = next;
                    candidate = null;
                }
            }
        }

        var position = new AcceptedPositionEvidence(snapshot.Position.Value!, snapshot.SampleSequence, snapshot.SourceTime);
        var state = Copy(previousState, snapshot.SampleSequence, snapshot.SourceTime, phase, attempt,
            candidate, transitionCount, previousState.RejectedSampleCount, position, PositionContinuity.Continuous);
        return new PhaseDetectionResult(snapshot, state, DetectionCycleOutcome.Accepted, null,
            issues, transitions, Array.Empty<PhaseCueEvent>());
    }

    private static void ValidateCheckpoint(PhaseDetectionConfiguration configuration, PhaseDetectionState state)
    {
        if (state.AlgorithmId != configuration.AlgorithmId || state.ConfigurationId != configuration.ConfigurationId)
            throw new ArgumentException("Checkpoint and configuration identities differ.", nameof(state));
        if (state.CueLatches.Entry || state.CueLatches.BaseTurn || state.CueLatches.Final ||
            state.CueLatches.GoAround || state.CueLatches.Exit)
            throw new ArgumentException("This slice requires clear cue latches.", nameof(state));
        if (state.LastSampleSequence is null && (state.ConfirmedPhase != PatternPhase.Unknown ||
            state.AttemptId is not null || state.Candidate is not null || state.TransitionCount != 0 ||
            state.LastAcceptedPosition is not null))
            throw new ArgumentException("Unstarted checkpoint is inconsistent.", nameof(state));
        if (state.ConfirmedPhase == PatternPhase.Unknown && state.AttemptId is not null)
            throw new ArgumentException("Unknown phase cannot have an inbound attempt.", nameof(state));
        if (state.ConfirmedPhase is PatternPhase.Entry or PatternPhase.Downwind or PatternPhase.Base or PatternPhase.Final &&
            state.AttemptId is null)
            throw new ArgumentException("Confirmed inbound phase requires an attempt.", nameof(state));
        if (state.Candidate is { } candidate && (candidate.Phase != InboundTransitionRules.Successor(state.ConfirmedPhase) ||
            candidate.LastSourceTime != state.LastSourceTime))
            throw new ArgumentException("Candidate must be the current inbound successor at the cursor.", nameof(state));
        if (state.PositionContinuity == PositionContinuity.Continuous &&
            (state.LastAcceptedPosition is not { } anchor ||
             anchor.SampleSequence != state.LastSampleSequence || anchor.SourceTime != state.LastSourceTime))
            throw new ArgumentException("Continuous anchor must match the consumed cursor.", nameof(state));
    }

    private static bool IsImplausibleRelocation(PhaseDetectionConfiguration configuration,
        PhaseDetectionState previousState, TelemetrySnapshot snapshot)
    {
        if (previousState.PositionContinuity != PositionContinuity.Continuous) return false;
        var runway = configuration.GeometryAuthority.RunwayReference;
        var before = runway.ToRunway(previousState.LastAcceptedPosition!.Position);
        var after = runway.ToRunway(snapshot.Position.Value!);
        var distance = double.Hypot(after.AlongTrackMetres - before.AlongTrackMetres,
            after.CrossTrackMetres - before.CrossTrackMetres);
        var elapsed = (snapshot.SourceTime - previousState.LastSourceTime!.Value).TotalSeconds;
        return distance > configuration.Policy.MaximumRelocationDistanceMetres ||
            (elapsed == 0 ? distance > 0 : distance / elapsed > configuration.Policy.MaximumRelocationSpeedMetresPerSecond);
    }

    private static PhaseDetectionResult Rejected(PhaseDetectionState previousState, TelemetrySnapshot snapshot,
        SampleRejectionReason reason)
    {
        var state = Copy(previousState, previousState.LastSampleSequence, previousState.LastSourceTime,
            previousState.ConfirmedPhase, previousState.AttemptId, previousState.Candidate,
            previousState.TransitionCount, checked(previousState.RejectedSampleCount + 1),
            previousState.LastAcceptedPosition, previousState.PositionContinuity);
        return new PhaseDetectionResult(snapshot, state, DetectionCycleOutcome.Rejected, reason,
            Array.Empty<TelemetryQualityIssue>(), Array.Empty<PhaseTransitionEvent>(), Array.Empty<PhaseCueEvent>());
    }

    private static PhaseDetectionResult Interrupted(PhaseDetectionState previousState, TelemetrySnapshot snapshot,
        List<TelemetryQualityIssue> issues, SampleInterruptionReason reason)
    {
        var state = Copy(previousState, snapshot.SampleSequence, snapshot.SourceTime,
            previousState.ConfirmedPhase, previousState.AttemptId, null, previousState.TransitionCount,
            previousState.RejectedSampleCount, previousState.LastAcceptedPosition,
            previousState.LastAcceptedPosition is null
                ? PositionContinuity.NoAcceptedPosition : PositionContinuity.Interrupted);
        return new PhaseDetectionResult(snapshot, state, DetectionCycleOutcome.Interrupted, null,
            issues, Array.Empty<PhaseTransitionEvent>(), Array.Empty<PhaseCueEvent>(), reason);
    }

    private static PhaseDetectionState Copy(PhaseDetectionState previousState, ulong? sequence,
        TimeSpan? sourceTime, PatternPhase phase, DetectionAttemptIdentity? attempt, CandidateEvidence? candidate,
        uint transitionCount, uint rejectedCount, AcceptedPositionEvidence? position, PositionContinuity continuity) =>
        new(previousState.AlgorithmId, previousState.ConfigurationId, previousState.StreamEpoch,
            sequence, sourceTime, phase, attempt, candidate, transitionCount, rejectedCount,
            previousState.CueLatches, position, continuity);

    private static List<TelemetryQualityIssue> QualityIssues(TelemetrySnapshot snapshot)
    {
        var issues = new List<TelemetryQualityIssue>();
        if (snapshot.Position.Validity == TelemetryValidity.Unavailable) issues.Add(TelemetryQualityIssue.MissingPosition);
        if (snapshot.AltitudeMslMetres.Validity == TelemetryValidity.Unavailable) issues.Add(TelemetryQualityIssue.MissingAltitude);
        if (snapshot.GroundSpeedMetresPerSecond.Validity == TelemetryValidity.Unavailable) issues.Add(TelemetryQualityIssue.MissingGroundSpeed);
        if (snapshot.TrueGroundTrackDegrees.Validity == TelemetryValidity.Unavailable) issues.Add(TelemetryQualityIssue.MissingGroundTrack);
        if (snapshot.VerticalSpeedMetresPerSecond.Validity == TelemetryValidity.Unavailable) issues.Add(TelemetryQualityIssue.MissingVerticalSpeed);
        if (snapshot.OnGround.Validity == TelemetryValidity.Unavailable) issues.Add(TelemetryQualityIssue.MissingOnGround);
        return issues;
    }
}
