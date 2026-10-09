namespace XFlight.Pattern.Core.PhaseDetection;

public sealed record CandidateEvidence
{
    public CandidateEvidence(PatternPhase phase, TimeSpan firstSourceTime, TimeSpan lastSourceTime, uint sampleCount)
    {
        if (!Enum.IsDefined(phase) || phase == PatternPhase.Unknown) throw new ArgumentOutOfRangeException(nameof(phase));
        if (firstSourceTime < TimeSpan.Zero || lastSourceTime < firstSourceTime) throw new ArgumentOutOfRangeException(nameof(lastSourceTime));
        if (sampleCount == 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        Phase = phase;
        FirstSourceTime = firstSourceTime;
        LastSourceTime = lastSourceTime;
        SampleCount = sampleCount;
    }
    public PatternPhase Phase { get; }
    public TimeSpan FirstSourceTime { get; }
    public TimeSpan LastSourceTime { get; }
    public uint SampleCount { get; }
}

public sealed record PhaseCueLatches
{
    public PhaseCueLatches(bool entry, bool baseTurn, bool final, bool goAround, bool exit)
    {
        Entry = entry; BaseTurn = baseTurn; Final = final; GoAround = goAround; Exit = exit;
    }
    public bool Entry { get; }
    public bool BaseTurn { get; }
    public bool Final { get; }
    public bool GoAround { get; }
    public bool Exit { get; }
}

/// <summary>An attempt is identified by the source sample that began it in a named configuration.</summary>
public sealed record DetectionAttemptIdentity
{
    public DetectionAttemptIdentity(string algorithmId, string configurationId, string streamEpoch, ulong startSampleSequence)
    {
        Identity.Required(algorithmId, nameof(algorithmId));
        Identity.Required(configurationId, nameof(configurationId));
        Identity.Required(streamEpoch, nameof(streamEpoch));
        if (startSampleSequence == 0) throw new ArgumentOutOfRangeException(nameof(startSampleSequence));
        AlgorithmId = algorithmId; ConfigurationId = configurationId; StreamEpoch = streamEpoch;
        StartSampleSequence = startSampleSequence;
    }
    public string AlgorithmId { get; }
    public string ConfigurationId { get; }
    public string StreamEpoch { get; }
    public ulong StartSampleSequence { get; }
}

/// <summary>The last accepted valid position. Rejected samples never replace this anchor.</summary>
public sealed record AcceptedPositionEvidence
{
    public AcceptedPositionEvidence(Geometry.GeographicCoordinate position, ulong sampleSequence, TimeSpan sourceTime)
    {
        ArgumentNullException.ThrowIfNull(position);
        if (sampleSequence == 0) throw new ArgumentOutOfRangeException(nameof(sampleSequence));
        if (sourceTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceTime));
        Position = position; SampleSequence = sampleSequence; SourceTime = sourceTime;
    }
    public Geometry.GeographicCoordinate Position { get; }
    public ulong SampleSequence { get; }
    public TimeSpan SourceTime { get; }
}

public enum PositionContinuity { NoAcceptedPosition, Continuous, Interrupted }

/// <summary>Observed runway contact; qualification is derived from the bound policy, not an age timeout.</summary>
public sealed record RunwayContactEvidence
{
    public RunwayContactEvidence(TimeSpan firstSourceTime, TimeSpan lastSourceTime,
        uint sampleCount, ulong lastSampleSequence)
    {
        if (firstSourceTime < TimeSpan.Zero || lastSourceTime < firstSourceTime)
            throw new ArgumentOutOfRangeException(nameof(lastSourceTime));
        if (sampleCount == 0) throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (lastSampleSequence == 0 || sampleCount > lastSampleSequence)
            throw new ArgumentOutOfRangeException(nameof(lastSampleSequence));
        if (sampleCount == 1 && firstSourceTime != lastSourceTime)
            throw new ArgumentException("One contact sample cannot span source time.");
        FirstSourceTime = firstSourceTime; LastSourceTime = lastSourceTime;
        SampleCount = sampleCount; LastSampleSequence = lastSampleSequence;
    }

    public TimeSpan FirstSourceTime { get; }
    public TimeSpan LastSourceTime { get; }
    public uint SampleCount { get; }
    public ulong LastSampleSequence { get; }
}

/// <summary>Replayable detector state. Null sample identity denotes an unstarted stream.</summary>
public sealed record PhaseDetectionState
{
    public PhaseDetectionState(string algorithmId, string configurationId, string streamEpoch,
        ulong? lastSampleSequence, TimeSpan? lastSourceTime, PatternPhase confirmedPhase,
        DetectionAttemptIdentity? attemptId, CandidateEvidence? candidate, uint transitionCount,
        uint rejectedSampleCount, PhaseCueLatches cueLatches,
        AcceptedPositionEvidence? lastAcceptedPosition = null,
        PositionContinuity positionContinuity = PositionContinuity.NoAcceptedPosition)
        : this(algorithmId, configurationId, streamEpoch, lastSampleSequence, lastSourceTime,
            confirmedPhase, attemptId, candidate, transitionCount, rejectedSampleCount, cueLatches,
            lastAcceptedPosition, positionContinuity, null)
    {
    }

    /// <summary>Additive contact evidence does not replace the existing constructor or certify replay history.</summary>
    public PhaseDetectionState(string algorithmId, string configurationId, string streamEpoch,
        ulong? lastSampleSequence, TimeSpan? lastSourceTime, PatternPhase confirmedPhase,
        DetectionAttemptIdentity? attemptId, CandidateEvidence? candidate, uint transitionCount,
        uint rejectedSampleCount, PhaseCueLatches cueLatches,
        AcceptedPositionEvidence? lastAcceptedPosition, PositionContinuity positionContinuity,
        RunwayContactEvidence? runwayContact)
    {
        Identity.Required(algorithmId, nameof(algorithmId));
        Identity.Required(configurationId, nameof(configurationId));
        Identity.Required(streamEpoch, nameof(streamEpoch));
        if ((lastSampleSequence is null) != (lastSourceTime is null) || lastSampleSequence == 0 || lastSourceTime < TimeSpan.Zero)
            throw new ArgumentException("Last sample identity and source time must be present together.");
        if (!Enum.IsDefined(confirmedPhase)) throw new ArgumentOutOfRangeException(nameof(confirmedPhase));
        if (attemptId is not null && (attemptId.AlgorithmId != algorithmId || attemptId.ConfigurationId != configurationId ||
            attemptId.StreamEpoch != streamEpoch || lastSampleSequence is null || attemptId.StartSampleSequence > lastSampleSequence))
            throw new ArgumentException("Attempt identity must belong to the processed stream and configuration.", nameof(attemptId));
        if (candidate is not null && (lastSourceTime is null || candidate.LastSourceTime > lastSourceTime))
            throw new ArgumentException("Candidate evidence must belong to processed source time.");
        ArgumentNullException.ThrowIfNull(cueLatches);
        if (attemptId is null && (cueLatches.Entry || cueLatches.BaseTurn || cueLatches.Final ||
            cueLatches.GoAround || cueLatches.Exit))
            throw new ArgumentException("Cue latches require an active attempt.", nameof(cueLatches));
        if (!Enum.IsDefined(positionContinuity)) throw new ArgumentOutOfRangeException(nameof(positionContinuity));
        if ((lastAcceptedPosition is null) != (positionContinuity == PositionContinuity.NoAcceptedPosition))
            throw new ArgumentException("Position evidence and continuity must agree.");
        if (lastAcceptedPosition is not null && (lastSampleSequence is null ||
            lastAcceptedPosition.SampleSequence > lastSampleSequence || lastAcceptedPosition.SourceTime > lastSourceTime))
            throw new ArgumentException("Accepted position must belong to processed source history.", nameof(lastAcceptedPosition));
        if (candidate?.Phase == PatternPhase.Departure && runwayContact is null)
            throw new ArgumentException("Departure candidate requires observed runway contact.", nameof(candidate));
        if (runwayContact is not null)
        {
            if (confirmedPhase != PatternPhase.Unknown || attemptId is not null || lastSampleSequence is null ||
                positionContinuity != PositionContinuity.Continuous ||
                lastAcceptedPosition!.SampleSequence != lastSampleSequence || lastAcceptedPosition.SourceTime != lastSourceTime ||
                runwayContact.LastSampleSequence > lastSampleSequence || runwayContact.LastSourceTime > lastSourceTime)
                throw new ArgumentException("Contact requires an Unknown phase and a current continuous anchor.", nameof(runwayContact));
            if (candidate is null)
            {
                if (runwayContact.LastSampleSequence != lastSampleSequence || runwayContact.LastSourceTime != lastSourceTime)
                    throw new ArgumentException("Ground contact must match the consumed cursor.", nameof(runwayContact));
            }
            else if (candidate.Phase != PatternPhase.Departure || candidate.LastSourceTime != lastSourceTime ||
                runwayContact.LastSampleSequence >= lastSampleSequence || runwayContact.LastSourceTime > candidate.FirstSourceTime)
                throw new ArgumentException("Airborne contact proof requires a later Departure candidate at the cursor.", nameof(candidate));
        }
        AlgorithmId = algorithmId; ConfigurationId = configurationId; StreamEpoch = streamEpoch;
        LastSampleSequence = lastSampleSequence; LastSourceTime = lastSourceTime;
        ConfirmedPhase = confirmedPhase; AttemptId = attemptId; Candidate = candidate;
        TransitionCount = transitionCount; RejectedSampleCount = rejectedSampleCount; CueLatches = cueLatches;
        LastAcceptedPosition = lastAcceptedPosition; PositionContinuity = positionContinuity;
        RunwayContact = runwayContact;
    }
    public string AlgorithmId { get; }
    public string ConfigurationId { get; }
    public string StreamEpoch { get; }
    public ulong? LastSampleSequence { get; }
    public TimeSpan? LastSourceTime { get; }
    public PatternPhase ConfirmedPhase { get; }
    public DetectionAttemptIdentity? AttemptId { get; }
    public CandidateEvidence? Candidate { get; }
    public uint TransitionCount { get; }
    public uint RejectedSampleCount { get; }
    public PhaseCueLatches CueLatches { get; }
    /// <summary>On accepted valid position replace this anchor and mark Continuous. On accepted invalid position or interruption retain an existing anchor and mark Interrupted; with no anchor remain NoAcceptedPosition. Rejected samples, including duplicates, preserve both fields unchanged. The first accepted valid position after interruption starts a new displacement interval and cannot be compared across the interruption.</summary>
    public AcceptedPositionEvidence? LastAcceptedPosition { get; }
    public PositionContinuity PositionContinuity { get; }
    /// <summary>Current ground interval, or qualified ground proof retained only for a pending Departure candidate.
    /// Rejection preserves it; interruption or accepted launch-evidence loss clears it.</summary>
    public RunwayContactEvidence? RunwayContact { get; }
}

internal static class Identity
{
    public static void Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Identity is required.", name);
    }
}
