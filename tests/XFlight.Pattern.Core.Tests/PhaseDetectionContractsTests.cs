using XFlight.Pattern.Core.Geometry;
using XFlight.Pattern.Core.PhaseDetection;
using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;

namespace XFlight.Pattern.Core.Tests;

public class PhaseDetectionContractsTests
{
    private static TelemetryObservation<double> Number(double? value) => value is null
        ? new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable)
        : new(value, TelemetryValidity.Available, TelemetryProvenance.Measured);

    private static TelemetrySnapshot Sample(ulong sequence = 1, int sourceSeconds = 5, bool positionAvailable = true) =>
        new("epoch-1", sequence, TimeSpan.FromSeconds(sourceSeconds),
        positionAvailable
            ? new(new GeographicCoordinate(45, 9), TelemetryValidity.Available, TelemetryProvenance.Measured)
            : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable),
        Number(100), Number(25), Number(90), Number(2),
        new(true, TelemetryValidity.Available, TelemetryProvenance.Measured));

    private static PhaseDetectionPolicy Policy() => new(
        TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(8),
        10, 20, 15, 25, 10, 20, 1, 30, 100, 500);

    private static PhaseDetectionState State() => new("algorithm-v1", "config-a", "epoch-1", 1,
        TimeSpan.FromSeconds(5), PatternPhase.Unknown, null, null, 0, 0,
        new(false, false, false, false, false),
        new(new GeographicCoordinate(45, 9), 1, TimeSpan.FromSeconds(5)), PositionContinuity.Continuous);

    [Fact]
    public void SnapshotPreservesUnitsIdentityAndProvenance()
    {
        var sample = Sample();
        Assert.Equal("epoch-1", sample.StreamEpoch);
        Assert.Equal((ulong)1, sample.SampleSequence);
        Assert.Equal(TimeSpan.FromSeconds(5), sample.SourceTime);
        Assert.Equal(100, sample.AltitudeMslMetres.Value);
        Assert.Equal(25, sample.GroundSpeedMetresPerSecond.Value);
        Assert.Equal(90, sample.TrueGroundTrackDegrees.Value);
        Assert.Equal(TelemetryProvenance.Measured, sample.Position.Provenance);
    }

    [Fact]
    public void UnavailableObservationHasNoValueOrClaimedProvenance()
    {
        var unavailable = Number(null);
        Assert.Null(unavailable.Value);
        Assert.Throws<ArgumentException>(() => new TelemetryObservation<double>(null, TelemetryValidity.Available, TelemetryProvenance.Measured));
        Assert.Throws<ArgumentException>(() => new TelemetryPositionObservation(null, TelemetryValidity.Unavailable, TelemetryProvenance.Measured));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryObservation<bool>(true, (TelemetryValidity)99, TelemetryProvenance.Measured));
    }

    [Theory]
    [InlineData(double.NaN, 25, 90)]
    [InlineData(100, -1, 90)]
    [InlineData(100, 25, 360)]
    [InlineData(100, 25, double.PositiveInfinity)]
    public void SnapshotRejectsInvalidMeasurements(double altitude, double speed, double track)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetrySnapshot("epoch", 1, TimeSpan.Zero,
            new(new GeographicCoordinate(0, 0), TelemetryValidity.Available, TelemetryProvenance.Measured),
            Number(altitude), Number(speed), Number(track), Number(0),
            new(false, TelemetryValidity.Available, TelemetryProvenance.Measured)));
    }

    [Fact]
    public void SnapshotRejectsInvalidSampleIdentityAndTime()
    {
        var s = Sample();
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetrySnapshot(s.StreamEpoch, 0, s.SourceTime,
            s.Position, s.AltitudeMslMetres, s.GroundSpeedMetresPerSecond, s.TrueGroundTrackDegrees, s.VerticalSpeedMetresPerSecond, s.OnGround));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetrySnapshot(s.StreamEpoch, 1, TimeSpan.FromTicks(-1),
            s.Position, s.AltitudeMslMetres, s.GroundSpeedMetresPerSecond, s.TrueGroundTrackDegrees, s.VerticalSpeedMetresPerSecond, s.OnGround));
    }

    [Fact]
    public void PolicyHasExplicitValidatedThresholds()
    {
        var p = Policy();
        Assert.Equal(500, p.MaximumRelocationDistanceMetres);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionPolicy(p.CandidatePersistence, p.GroundContactPersistence,
            p.DeparturePersistence, p.ExitPersistence, p.MaximumSampleGap, TimeSpan.FromSeconds(11),
            p.CorridorEnterMarginMetres, p.CorridorExitMarginMetres, p.TrackEnterToleranceDegrees,
            p.TrackExitToleranceDegrees, p.GroundContactMaximumSpeedMetresPerSecond,
            p.DepartureMinimumSpeedMetresPerSecond, p.DepartureMinimumClimbMetresPerSecond,
            p.ExitMarginMetres, p.MaximumRelocationSpeedMetresPerSecond, p.MaximumRelocationDistanceMetres));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhaseDetectionPolicy(p.CandidatePersistence,
            p.GroundContactPersistence, p.DeparturePersistence, p.ExitPersistence, p.MaximumSampleGap,
            p.MaximumCandidateGap, double.NaN, p.CorridorExitMarginMetres,
            p.TrackEnterToleranceDegrees, p.TrackExitToleranceDegrees, p.GroundContactMaximumSpeedMetresPerSecond,
            p.DepartureMinimumSpeedMetresPerSecond, p.DepartureMinimumClimbMetresPerSecond,
            p.ExitMarginMetres, p.MaximumRelocationSpeedMetresPerSecond, p.MaximumRelocationDistanceMetres));
    }

    [Fact]
    public void PhaseAndStateRejectInvalidEvidence()
    {
        Assert.Equal(10, Enum.GetValues<PatternPhase>().Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateEvidence((PatternPhase)99, TimeSpan.Zero, TimeSpan.Zero, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateEvidence(PatternPhase.Final, TimeSpan.Zero, TimeSpan.Zero, 0));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionState("a", "c", "e", 1, null,
            PatternPhase.Unknown, null, null, 0, 0, new(false, false, false, false, false)));
    }

    [Fact]
    public void EventIdentityAndResultEnforceStableOrder()
    {
        var s = Sample();
        var attempt = new DetectionAttemptIdentity("algorithm-v1", "config-a", "epoch-1", 1);
        var state = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            s.SourceTime, PatternPhase.Entry, attempt, null, 1, 0,
            new(true, false, false, false, false),
            new(s.Position.Value!, s.SampleSequence, s.SourceTime), PositionContinuity.Continuous);
        var id0 = new DetectionEventIdentity(state.AlgorithmId, state.ConfigurationId, s.StreamEpoch, s.SampleSequence, 0);
        var id1 = new DetectionEventIdentity(state.AlgorithmId, state.ConfigurationId, s.StreamEpoch, s.SampleSequence, 1);
        Assert.Equal(id0, new DetectionEventIdentity("algorithm-v1", "config-a", "epoch-1", 1, 0));
        var transition = new PhaseTransitionEvent(id0, s.SourceTime, PatternPhase.Unknown, PatternPhase.Entry, attempt);
        var cue = new PhaseCueEvent(id1, s.SourceTime, PhaseCueKind.Entry, transition);
        var result = new PhaseDetectionResult(s, state, DetectionCycleOutcome.Accepted, null, [], [transition], [cue]);
        Assert.Single(result.Transitions);
        Assert.Single(result.Cues);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(s, state, DetectionCycleOutcome.Accepted, null,
            [], [new PhaseTransitionEvent(id1, s.SourceTime, PatternPhase.Unknown, PatternPhase.Entry, null)],
            [cue]));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(s, state, DetectionCycleOutcome.Rejected,
            SampleRejectionReason.DuplicateSequence, [], [transition], []));
    }

    [Fact]
    public void ResultCopiesEventCollections()
    {
        var s = Sample(); var state = State();
        var source = new List<TelemetryQualityIssue> { TelemetryQualityIssue.MissingAltitude };
        var result = new PhaseDetectionResult(s, state, DetectionCycleOutcome.Accepted, null, source, [], []);
        source.Clear();
        Assert.Single(result.QualityIssues);
        Assert.Throws<NotSupportedException>(() => ((IList<TelemetryQualityIssue>)result.QualityIssues).Clear());
    }

    [Fact]
    public void EpochMismatchCanBeReportedAsRejectedCycle()
    {
        var s = Sample();
        var state = new PhaseDetectionState("algorithm-v1", "config-a", "other-epoch", null, null,
            PatternPhase.Unknown, null, null, 0, 1, new(false, false, false, false, false));
        var result = new PhaseDetectionResult(s, state, DetectionCycleOutcome.Rejected,
            SampleRejectionReason.EpochMismatch, [], [], []);
        Assert.Equal(SampleRejectionReason.EpochMismatch, result.RejectionReason);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(s, state, DetectionCycleOutcome.Accepted,
            null, [], [], []));
    }

    [Fact]
    public void AttemptIdentityIsTiedToConfigurationAndStartSample()
    {
        var attempt = new DetectionAttemptIdentity("algorithm-v1", "config-a", "epoch-1", 1);
        Assert.Equal(attempt, new DetectionAttemptIdentity("algorithm-v1", "config-a", "epoch-1", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DetectionAttemptIdentity("a", "c", "e", 0));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionState("algorithm-v1", "config-b", "epoch-1", 1,
            TimeSpan.FromSeconds(5), PatternPhase.Entry, attempt, null, 0, 0,
            new(false, false, false, false, false)));
    }

    [Fact]
    public void AcceptedPositionAnchorAndContinuityAreValidated()
    {
        var position = new GeographicCoordinate(45, 9);
        var anchor = new AcceptedPositionEvidence(position, 1, TimeSpan.FromSeconds(5));
        var latches = new PhaseCueLatches(false, false, false, false, false);
        var continuous = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            TimeSpan.FromSeconds(5), PatternPhase.Unknown, null, null, 0, 0, latches,
            anchor, PositionContinuity.Continuous);
        var interrupted = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 2,
            TimeSpan.FromSeconds(6), PatternPhase.Unknown, null, null, 0, 0, latches,
            anchor, PositionContinuity.Interrupted);
        Assert.Equal(anchor, interrupted.LastAcceptedPosition);
        Assert.Equal(PositionContinuity.Continuous, continuous.PositionContinuity);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionState("a", "c", "e", 1,
            TimeSpan.FromSeconds(5), PatternPhase.Unknown, null, null, 0, 0, latches,
            anchor, PositionContinuity.NoAcceptedPosition));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionState("a", "c", "e", 1,
            TimeSpan.FromSeconds(5), PatternPhase.Unknown, null, null, 0, 0, latches,
            new AcceptedPositionEvidence(position, 2, TimeSpan.FromSeconds(5)), PositionContinuity.Continuous));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AcceptedPositionEvidence(position, 0, TimeSpan.Zero));
    }

    [Fact]
    public void InvalidRejectedAndDuplicateCyclesRepresentPreservedPositionHistory()
    {
        var sample = Sample();
        var unavailableSample = Sample(2, 6, positionAvailable: false);
        var anchor = new AcceptedPositionEvidence(sample.Position.Value!, 1, sample.SourceTime);
        var latches = new PhaseCueLatches(false, false, false, false, false);
        var invalidPositionState = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 2,
            TimeSpan.FromSeconds(6), PatternPhase.Unknown, null, null, 0, 0, latches,
            anchor, PositionContinuity.Interrupted);
        var invalidPosition = new PhaseDetectionResult(unavailableSample, invalidPositionState,
            DetectionCycleOutcome.Accepted, null, [TelemetryQualityIssue.MissingPosition], [], []);
        Assert.Same(anchor, invalidPosition.State.LastAcceptedPosition);
        var duplicateState = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            sample.SourceTime, PatternPhase.Unknown, null, null, 0, 1, latches,
            anchor, PositionContinuity.Continuous);
        var duplicate = new PhaseDetectionResult(sample, duplicateState,
            DetectionCycleOutcome.Rejected, SampleRejectionReason.DuplicateSequence, [], [], []);
        Assert.Same(anchor, duplicate.State.LastAcceptedPosition);
        Assert.Equal(PositionContinuity.Continuous, duplicate.State.PositionContinuity);
        Assert.Equal(SampleRejectionReason.DuplicateSequence, duplicate.RejectionReason);
    }

    [Fact]
    public void ExitCandidateCanPersistAtExactSourceBoundary()
    {
        var first = TimeSpan.FromSeconds(5);
        var candidate = new CandidateEvidence(PatternPhase.Exited, first, first + Policy().ExitPersistence, 2);
        Assert.Equal(Policy().ExitPersistence, candidate.LastSourceTime - candidate.FirstSourceTime);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateEvidence(PatternPhase.Unknown, first, first, 1));
    }

    [Fact]
    public void ConfigurationBindsM3AndDerivesSeparateClosedEnvelopes()
    {
        var circuit = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(10)),
            TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Left), PatternAltitudeResolution.Unresolved);
        var runway = new RunwayGeometryReference(new AirportIdentifier("TEST"), "01", new(0, 0), new(0.01, 0));
        var m3 = PatternGeometryResult.Generate(circuit, runway, PatternGeometryValidationTests.Parameters());
        var config = new PhaseDetectionConfiguration("algorithm-v1", "config-a", m3, Policy(), 25);
        Assert.Same(m3, config.GeometryAuthority);
        Assert.Equal(0, config.RunwayContactEnvelope.MinimumAlongTrackMetres);
        Assert.Equal(runway.RunwayLength.Metres, config.RunwayContactEnvelope.MaximumAlongTrackMetres);
        Assert.Equal(-25, config.RunwayContactEnvelope.MinimumCrossTrackMetres);
        Assert.Equal(25, config.RunwayContactEnvelope.MaximumCrossTrackMetres);
        Assert.Equal(-1_500 - Policy().ExitMarginMetres, config.OuterExitEnvelope.MinimumAlongTrackMetres);
        Assert.Equal(runway.RunwayLength.Metres + 500 + 50 + Policy().ExitMarginMetres,
            config.OuterExitEnvelope.MaximumAlongTrackMetres);
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhaseDetectionConfiguration("a", "c", m3, Policy(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PhaseDetectionConfiguration("a", "c", m3, Policy(), double.NaN));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionConfiguration(" ", "c", m3, Policy(), 25));
        var unknown = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(10)),
            TrafficPatternSideResolution.Unknown, PatternAltitudeResolution.Unresolved);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionConfiguration("a", "c",
            PatternGeometryResult.Generate(unknown, runway, PatternGeometryValidationTests.Parameters()), Policy(), 25));
    }

    [Fact]
    public void InterruptedCycleHasReasonAndCannotEmitEvents()
    {
        var sample = Sample(); var state = State();
        var interrupted = new PhaseDetectionResult(sample, state, DetectionCycleOutcome.Interrupted, null,
            [TelemetryQualityIssue.ContinuityGap], [], [], SampleInterruptionReason.SampleContinuityGap);
        Assert.Equal(SampleInterruptionReason.SampleContinuityGap, interrupted.InterruptionReason);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, state, DetectionCycleOutcome.Interrupted,
            null, [], [], []));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, state, DetectionCycleOutcome.Accepted,
            null, [], [], [], SampleInterruptionReason.PositionContinuityLost));
    }

    [Fact]
    public void CueRequiresEligibleConfirmedTransitionAndStableAttemptIdentity()
    {
        var sample = Sample(); var state = State();
        var attempt = new DetectionAttemptIdentity("algorithm-v1", "config-a", "epoch-1", 1);
        var transitionId = new DetectionEventIdentity("algorithm-v1", "config-a", "epoch-1", 1, 0);
        var cueId = new DetectionEventIdentity("algorithm-v1", "config-a", "epoch-1", 1, 1);
        var transition = new PhaseTransitionEvent(transitionId, sample.SourceTime, PatternPhase.Unknown, PatternPhase.Entry, attempt);
        var cue = new PhaseCueEvent(cueId, sample.SourceTime, PhaseCueKind.Entry, transition);
        Assert.Equal(attempt, cue.AttemptId);
        Assert.Equal(transitionId, cue.ConfirmedTransition.Identity);
        Assert.Throws<ArgumentException>(() => new PhaseCueEvent(cueId, sample.SourceTime, PhaseCueKind.Exit, transition));
        Assert.Throws<ArgumentException>(() => new PhaseCueEvent(transitionId, sample.SourceTime, PhaseCueKind.Entry, transition));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, state, DetectionCycleOutcome.Accepted,
            null, [], [], [cue]));
        Assert.Throws<ArgumentException>(() => new PhaseCueEvent(cueId, sample.SourceTime, PhaseCueKind.Entry,
            new PhaseTransitionEvent(transitionId, sample.SourceTime, PatternPhase.Unknown, PatternPhase.Entry, null)));
    }

    [Fact]
    public void AcceptedTransitionChainMustEndAtConfirmedPhase()
    {
        var sample = Sample();
        var state = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            sample.SourceTime, PatternPhase.Final, null, null, 2, 0,
            new(false, false, false, false, false),
            new(sample.Position.Value!, 1, sample.SourceTime), PositionContinuity.Continuous);
        var id0 = new DetectionEventIdentity("algorithm-v1", "config-a", "epoch-1", 1, 0);
        var id1 = new DetectionEventIdentity("algorithm-v1", "config-a", "epoch-1", 1, 1);
        var entry = new PhaseTransitionEvent(id0, sample.SourceTime, PatternPhase.Unknown, PatternPhase.Entry, null);
        var broken = new PhaseTransitionEvent(id1, sample.SourceTime, PatternPhase.Downwind, PatternPhase.Final, null);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, state,
            DetectionCycleOutcome.Accepted, null, [], [entry, broken], []));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, state,
            DetectionCycleOutcome.Accepted, null, [], [entry], []));
    }

    [Fact]
    public void EmittedCueRequiresResultAttemptAndSetLatch()
    {
        var sample = Sample();
        var eventAttempt = new DetectionAttemptIdentity("algorithm-v1", "config-a", "epoch-1", 1);
        var otherAttempt = new DetectionAttemptIdentity("algorithm-v1", "config-a", "epoch-1", 2);
        var transition = new PhaseTransitionEvent(new("algorithm-v1", "config-a", "epoch-1", 1, 0),
            sample.SourceTime, PatternPhase.Unknown, PatternPhase.Entry, eventAttempt);
        var cue = new PhaseCueEvent(new("algorithm-v1", "config-a", "epoch-1", 1, 1),
            sample.SourceTime, PhaseCueKind.Entry, transition);
        // A second attempt cannot start after the current sample; use sample 2 for this mismatch.
        var secondSample = Sample(2, 6);
        var secondTransition = new PhaseTransitionEvent(new("algorithm-v1", "config-a", "epoch-1", 2, 0),
            secondSample.SourceTime, PatternPhase.Unknown, PatternPhase.Entry, eventAttempt);
        var secondCue = new PhaseCueEvent(new("algorithm-v1", "config-a", "epoch-1", 2, 1),
            secondSample.SourceTime, PhaseCueKind.Entry, secondTransition);
        var wrongAttemptState = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 2,
            secondSample.SourceTime, PatternPhase.Entry, otherAttempt, null, 1, 0,
            new(true, false, false, false, false),
            new(secondSample.Position.Value!, 2, secondSample.SourceTime), PositionContinuity.Continuous);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(secondSample, wrongAttemptState,
            DetectionCycleOutcome.Accepted, null, [], [secondTransition], [secondCue]));
        var unsetLatchState = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            sample.SourceTime, PatternPhase.Entry, eventAttempt, null, 1, 0,
            new(false, false, false, false, false),
            new(sample.Position.Value!, 1, sample.SourceTime), PositionContinuity.Continuous);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, unsetLatchState,
            DetectionCycleOutcome.Accepted, null, [], [transition], [cue]));
    }

    [Fact]
    public void ConsumedSnapshotCursorMustMatchAcceptedAndInterruptedState()
    {
        var sample = Sample();
        foreach (var state in new[]
        {
            new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 2, sample.SourceTime,
                PatternPhase.Unknown, null, null, 0, 0, new(false, false, false, false, false),
                new(sample.Position.Value!, 1, sample.SourceTime), PositionContinuity.Continuous),
            new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1, TimeSpan.FromSeconds(6),
                PatternPhase.Unknown, null, null, 0, 0, new(false, false, false, false, false),
                new(sample.Position.Value!, 1, sample.SourceTime), PositionContinuity.Continuous)
        })
        {
            Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, state,
                DetectionCycleOutcome.Accepted, null, [], [], []));
            Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, state,
                DetectionCycleOutcome.Interrupted, null, [TelemetryQualityIssue.ContinuityGap], [], [],
                SampleInterruptionReason.SampleContinuityGap));
        }
    }

    [Fact]
    public void AcceptedPositionMustMatchSnapshotAndUnavailablePositionLosesContinuity()
    {
        var sample = Sample();
        var wrongAnchor = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            sample.SourceTime, PatternPhase.Unknown, null, null, 0, 0,
            new(false, false, false, false, false),
            new(new GeographicCoordinate(46, 9), 1, sample.SourceTime), PositionContinuity.Continuous);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(sample, wrongAnchor,
            DetectionCycleOutcome.Accepted, null, [], [], []));
        var unavailable = Sample(2, 6, positionAvailable: false);
        var staleContinuity = new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 2,
            unavailable.SourceTime, PatternPhase.Unknown, null, null, 0, 0,
            new(false, false, false, false, false),
            new(sample.Position.Value!, 1, sample.SourceTime), PositionContinuity.Continuous);
        Assert.Throws<ArgumentException>(() => new PhaseDetectionResult(unavailable, staleContinuity,
            DetectionCycleOutcome.Accepted, null, [TelemetryQualityIssue.MissingPosition], [], []));
    }

    [Fact]
    public void CueLatchesRequireActiveAttempt()
    {
        Assert.Throws<ArgumentException>(() => new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            TimeSpan.FromSeconds(5), PatternPhase.Entry, null, null, 1, 0,
            new(true, false, false, false, false)));
    }

    [Fact]
    public void RunwayContactEvidenceIsImmutableAndRejectsInvalidIntervals()
    {
        var evidence = new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.FromSeconds(3), 2, 2);
        Assert.Equal(evidence, new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.FromSeconds(3), 2, 2));
        Assert.All(typeof(RunwayContactEvidence).GetProperties(), property => Assert.Null(property.SetMethod));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunwayContactEvidence(TimeSpan.FromTicks(-1), TimeSpan.Zero, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunwayContactEvidence(TimeSpan.FromSeconds(3), TimeSpan.Zero, 2, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.Zero, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.Zero, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.Zero, 3, 2));
        Assert.Throws<ArgumentException>(() => new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.FromSeconds(3), 1, 2));
        Assert.Equal(TimeSpan.Zero, new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.Zero, 2, 2).LastSourceTime);
    }

    private static PhaseDetectionState RunwayContactState(RunwayContactEvidence? contact,
        CandidateEvidence? candidate = null, PatternPhase phase = PatternPhase.Unknown,
        ulong? sequence = 2, double? seconds = 3, PositionContinuity continuity = PositionContinuity.Continuous,
        DetectionAttemptIdentity? attempt = null, ulong? anchorSequence = null, double? anchorSeconds = null) =>
        new("a", "c", "e", sequence, seconds is { } time ? TimeSpan.FromSeconds(time) : null,
            phase, attempt, candidate, 0, 0, new(false, false, false, false, false),
            sequence is { } seq ? new(new GeographicCoordinate(45, 9), anchorSequence ?? seq,
                TimeSpan.FromSeconds(anchorSeconds ?? seconds!.Value)) : null,
            continuity, contact);

    [Fact]
    public void RunwayContactStatePreservesExistingConstructorAndValueEquality()
    {
        Assert.NotNull(typeof(PhaseDetectionState).GetConstructor([
            typeof(string), typeof(string), typeof(string), typeof(ulong?), typeof(TimeSpan?),
            typeof(PatternPhase), typeof(DetectionAttemptIdentity), typeof(CandidateEvidence),
            typeof(uint), typeof(uint), typeof(PhaseCueLatches), typeof(AcceptedPositionEvidence), typeof(PositionContinuity)]));
        var oldState = State();
        Assert.Null(oldState.RunwayContact);
        Assert.Equal(oldState, new PhaseDetectionState(oldState.AlgorithmId, oldState.ConfigurationId,
            oldState.StreamEpoch, oldState.LastSampleSequence, oldState.LastSourceTime, oldState.ConfirmedPhase,
            oldState.AttemptId, oldState.Candidate, oldState.TransitionCount, oldState.RejectedSampleCount,
            oldState.CueLatches, oldState.LastAcceptedPosition, oldState.PositionContinuity, null));
        var contact = new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.FromSeconds(3), 2, 2);
        var ground = RunwayContactState(contact);
        Assert.Equal(ground, RunwayContactState(new(TimeSpan.Zero, TimeSpan.FromSeconds(3), 2, 2)));
        Assert.NotEqual(ground, RunwayContactState(null));
        Assert.NotEqual(ground, RunwayContactState(new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), 2, 2)));
        var airborne = RunwayContactState(contact,
            new(PatternPhase.Departure, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), 2), sequence: 4, seconds: 5);
        Assert.Equal(contact, airborne.RunwayContact);
        Assert.Equal(PatternPhase.Departure, airborne.Candidate!.Phase);
        // Source-time equality at liftoff is valid when the ground sample sequence is strictly earlier.
        Assert.NotNull(RunwayContactState(contact,
            new(PatternPhase.Departure, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), 1), sequence: 3).RunwayContact);
    }

    [Theory]
    [InlineData("origin")]
    [InlineData("phase")]
    [InlineData("attempt")]
    [InlineData("entry-candidate")]
    [InlineData("interrupted")]
    [InlineData("future-sequence")]
    [InlineData("future-time")]
    [InlineData("stale-ground-sequence")]
    [InlineData("stale-ground-time")]
    [InlineData("airborne-sequence")]
    [InlineData("ground-after-liftoff")]
    [InlineData("stale-candidate")]
    [InlineData("stale-anchor-sequence")]
    [InlineData("stale-anchor-time")]
    public void RunwayContactStateRejectsContradictoryEvidence(string contradiction)
    {
        var contact = new RunwayContactEvidence(TimeSpan.Zero, TimeSpan.FromSeconds(3), 2, 2);
        var departure = new CandidateEvidence(PatternPhase.Departure, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), 2);
        Assert.Throws<ArgumentException>(() => contradiction switch
        {
            "origin" => RunwayContactState(contact, sequence: null, seconds: null, continuity: PositionContinuity.NoAcceptedPosition),
            "phase" => RunwayContactState(contact, phase: PatternPhase.Departure),
            "attempt" => RunwayContactState(contact, attempt: new("a", "c", "e", 1)),
            "entry-candidate" => RunwayContactState(contact, new(PatternPhase.Entry, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), 1)),
            "interrupted" => RunwayContactState(contact, continuity: PositionContinuity.Interrupted),
            "future-sequence" => RunwayContactState(new(TimeSpan.Zero, TimeSpan.FromSeconds(3), 2, 3)),
            "future-time" => RunwayContactState(new(TimeSpan.Zero, TimeSpan.FromSeconds(4), 2, 2)),
            "stale-ground-sequence" => RunwayContactState(contact, sequence: 3),
            "stale-ground-time" => RunwayContactState(contact, seconds: 4),
            "airborne-sequence" => RunwayContactState(contact, departure, seconds: 5),
            "ground-after-liftoff" => RunwayContactState(new(TimeSpan.Zero, TimeSpan.FromSeconds(5), 2, 2), departure, sequence: 3, seconds: 5),
            "stale-candidate" => RunwayContactState(contact, departure, sequence: 4, seconds: 6),
            "stale-anchor-sequence" => RunwayContactState(contact, anchorSequence: 1),
            "stale-anchor-time" => RunwayContactState(contact, anchorSeconds: 2),
            _ => throw new InvalidOperationException()
        });
    }

    [Fact]
    public void DepartureCandidateRequiresContactEvenThroughExistingStateConstructor()
    {
        var candidate = new CandidateEvidence(PatternPhase.Departure, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), 1);
        Assert.Throws<ArgumentException>(() => RunwayContactState(null, candidate, sequence: 3, seconds: 5));
        Assert.Throws<ArgumentException>(() => new PhaseDetectionState("algorithm-v1", "config-a", "epoch-1", 1,
            TimeSpan.FromSeconds(5), PatternPhase.Unknown, null, candidate, 0, 0,
            new(false, false, false, false, false), State().LastAcceptedPosition, PositionContinuity.Continuous));
    }

    // TODO (future detector transition tests): previousState is required. The first From matches the prior phase;
    // TransitionCount changes by exactly the emitted transitions; rejected results preserve prior
    // phase, candidate, cursor, position and latches while incrementing rejection count; new attempts
    // reset latches; a cue changes its attempt latch from false to true exactly once; replay validates
    // checkpoint provenance and every previousState -> snapshot -> result successor.
}
