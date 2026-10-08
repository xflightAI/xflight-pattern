namespace XFlight.Pattern.Core.PhaseDetection;

/// <summary>Fixed inputs for one origin-only replay lineage.</summary>
public sealed class PhaseReplayContext
{
    public PhaseReplayContext(PhaseDetectionConfiguration configuration, string streamEpoch,
        double minimumUsableGroundSpeedMetresPerSecond)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        Identity.Required(streamEpoch, nameof(streamEpoch));
        if (!double.IsFinite(minimumUsableGroundSpeedMetresPerSecond) ||
            minimumUsableGroundSpeedMetresPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumUsableGroundSpeedMetresPerSecond));

        Configuration = configuration;
        StreamEpoch = streamEpoch;
        MinimumUsableGroundSpeedMetresPerSecond = minimumUsableGroundSpeedMetresPerSecond;
    }

    public PhaseDetectionConfiguration Configuration { get; }
    public string StreamEpoch { get; }
    public double MinimumUsableGroundSpeedMetresPerSecond { get; }

    /// <summary>Starts with no consumed samples, evidence, transitions, rejections, or cue latches.</summary>
    public PhaseReplayCursor Start() => new(this,
        new PhaseDetectionState(Configuration.AlgorithmId, Configuration.ConfigurationId, StreamEpoch,
            null, null, PatternPhase.Unknown, null, null, 0, 0,
            new PhaseCueLatches(false, false, false, false, false)));

    /// <summary>Advances only a cursor produced by this context. Every outcome returns a trusted successor.</summary>
    public (PhaseDetectionResult Result, PhaseReplayCursor NextCursor) Advance(
        PhaseReplayCursor cursor, TelemetrySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ReferenceEquals(cursor.Owner, this))
            throw new ArgumentException("Replay cursor belongs to a different context.", nameof(cursor));

        var result = DeterministicPhaseTransition.Evaluate(Configuration, cursor.State, snapshot,
            MinimumUsableGroundSpeedMetresPerSecond);
        return (result, new PhaseReplayCursor(this, result.State));
    }
}

/// <summary>Opaque, immutable proof of an origin or successor within one context instance.</summary>
public sealed class PhaseReplayCursor
{
    internal PhaseReplayCursor(PhaseReplayContext owner, PhaseDetectionState state)
    {
        Owner = owner;
        State = state;
    }

    internal PhaseReplayContext Owner { get; }
    internal PhaseDetectionState State { get; }
}
