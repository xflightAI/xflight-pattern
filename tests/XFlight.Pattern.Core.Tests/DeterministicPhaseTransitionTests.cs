using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;
using XFlight.Pattern.Core.Geometry;
using XFlight.Pattern.Core.PhaseDetection;

namespace XFlight.Pattern.Core.Tests;

public sealed class DeterministicPhaseTransitionTests
{
    private static PhaseDetectionConfiguration Configuration(TrafficPatternSide side = TrafficPatternSide.Left,
        TimeSpan? persistence = null, double enterTolerance = 15, double exitTolerance = 25,
        double relocationSpeed = 10_000, double relocationDistance = 10_000)
    {
        var circuit = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(0)),
            TrafficPatternSideResolver.Resolve(published: side), PatternAltitudeResolution.Unresolved);
        var runway = new RunwayGeometryReference(new AirportIdentifier("TEST"), "01", new(0, 0), new(0.01, 0));
        var geometry = PatternGeometryResult.Generate(circuit, runway, PatternGeometryValidationTests.Parameters());
        var policy = new PhaseDetectionPolicy(persistence ?? TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(4), 10, 20, enterTolerance, exitTolerance,
            10, 20, 1, 30, relocationSpeed, relocationDistance);
        return new("algorithm", "config", geometry, policy, 25);
    }

    private static PhaseDetectionState Initial() =>
        new("algorithm", "config", "epoch", null, null, PatternPhase.Unknown, null, null,
            0, 0, new(false, false, false, false, false));

    private static GeographicCoordinate Geographic(RunwayCoordinate point) =>
        new(point.AlongTrackMetres / 110_574.2758, point.CrossTrackMetres / 111_319.4907);

    private static RunwayCoordinate Midpoint(HorizontalSegment axis) => new(
        (axis.Start.AlongTrackMetres + axis.End.AlongTrackMetres) / 2,
        (axis.Start.CrossTrackMetres + axis.End.CrossTrackMetres) / 2);

    private static (GeographicCoordinate Position, double Track) Point(PhaseDetectionConfiguration config,
        PatternPhase phase)
    {
        var geometry = config.GeometryAuthority.Geometry!;
        var side = Math.Sign(geometry.Downwind.Axis.Start.CrossTrackMetres);
        return phase switch
        {
            PatternPhase.Entry => (Geographic(Midpoint(geometry.Entry.Axis)), side < 0 ? 135 : 225),
            PatternPhase.Downwind => (Geographic(Midpoint(geometry.Downwind.Axis)), 180),
            PatternPhase.Base => (Geographic(new(-800, side * 400)), side < 0 ? 90 : 270),
            PatternPhase.Final => (Geographic(new(-300, 0)), 0),
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };
    }

    private static TelemetryObservation<double> Number(double? value) => value is { } v
        ? new(v, TelemetryValidity.Available, TelemetryProvenance.Measured)
        : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable);

    private static TelemetrySnapshot Sample(ulong sequence, TimeSpan time, GeographicCoordinate? position,
        double? speed = 25, double? track = 0, bool? onGround = false, string epoch = "epoch") =>
        new(epoch, sequence, time,
            position is { } p
                ? new(p, TelemetryValidity.Available, TelemetryProvenance.Measured)
                : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable),
            Number(null), Number(speed), Number(track), Number(null),
            onGround is { } ground
                ? new(ground, TelemetryValidity.Available, TelemetryProvenance.Measured)
                : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable));

    private static PhaseDetectionResult Step(PhaseDetectionConfiguration config, PhaseDetectionState state,
        TelemetrySnapshot snapshot, double minimumSpeed = 5) =>
        DeterministicPhaseTransition.Evaluate(config, state, snapshot, minimumSpeed);

    private static PhaseDetectionResult At(PhaseDetectionConfiguration config, PhaseDetectionState state,
        ulong sequence, TimeSpan time, PatternPhase target, bool? onGround = false)
    {
        var point = Point(config, target);
        return Step(config, state, Sample(sequence, time, point.Position, track: point.Track, onGround: onGround));
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void FourInboundTransitionsHaveOneEventStableAttemptAndNoCues(TrafficPatternSide side)
    {
        var config = Configuration(side);
        var state = Initial();
        var phases = new[] { PatternPhase.Entry, PatternPhase.Downwind, PatternPhase.Base, PatternPhase.Final };
        DetectionAttemptIdentity? attempt = null;
        ulong sequence = 0;
        foreach (var target in phases)
        {
            var firstTime = TimeSpan.FromSeconds((sequence / 2) * 3);
            var first = At(config, state, ++sequence, firstTime, target);
            Assert.Equal(state.ConfirmedPhase, first.State.ConfirmedPhase);
            Assert.Equal(target, first.State.Candidate?.Phase);
            Assert.Empty(first.Transitions);
            var confirmed = At(config, first.State, ++sequence, firstTime + TimeSpan.FromSeconds(2), target);
            Assert.Equal(DetectionCycleOutcome.Accepted, confirmed.Outcome);
            Assert.Equal(target, confirmed.State.ConfirmedPhase);
            Assert.Null(confirmed.State.Candidate);
            var transition = Assert.Single(confirmed.Transitions);
            Assert.Equal(state.ConfirmedPhase, transition.From);
            Assert.Equal(target, transition.To);
            Assert.Equal(sequence, transition.Identity.SampleSequence);
            Assert.Equal((uint)0, transition.Identity.Ordinal);
            Assert.Equal((uint)(sequence / 2), confirmed.State.TransitionCount);
            attempt ??= confirmed.State.AttemptId;
            Assert.Equal((ulong)2, attempt!.StartSampleSequence);
            Assert.Equal(attempt, transition.AttemptId);
            Assert.Equal(attempt, confirmed.State.AttemptId);
            Assert.Empty(confirmed.Cues);
            Assert.Equal(new PhaseCueLatches(false, false, false, false, false), confirmed.State.CueLatches);
            state = confirmed.State;
        }
    }

    [Fact]
    public void UnsupportedInitialAndReverseTransitionsHold()
    {
        var config = Configuration();
        var downwind = At(config, Initial(), 1, TimeSpan.Zero, PatternPhase.Downwind);
        Assert.Equal(PatternPhase.Unknown, downwind.State.ConfirmedPhase);
        Assert.Null(downwind.State.Candidate);
        var attempt = new DetectionAttemptIdentity("algorithm", "config", "epoch", 1);
        var final = new PhaseDetectionState("algorithm", "config", "epoch", 1, TimeSpan.Zero,
            PatternPhase.Final, attempt, null, 4, 0, new(false, false, false, false, false));
        var reverse = At(config, final, 2, TimeSpan.FromSeconds(1), PatternPhase.Base);
        Assert.Equal(PatternPhase.Final, reverse.State.ConfirmedPhase);
        Assert.Empty(reverse.Transitions);
        var departure = new PhaseDetectionState("algorithm", "config", "epoch", 1, TimeSpan.Zero,
            PatternPhase.Departure, null, null, 0, 0, new(false, false, false, false, false));
        var unsupported = At(config, departure, 2, TimeSpan.FromSeconds(1), PatternPhase.Downwind);
        Assert.Equal(PatternPhase.Departure, unsupported.State.ConfirmedPhase);
    }

    [Fact]
    public void PersistenceIsInclusiveAndEqualTimeAddsNoDuration()
    {
        var config = Configuration();
        var start = At(config, Initial(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var equalTime = At(config, start.State, 2, TimeSpan.Zero, PatternPhase.Entry);
        Assert.Equal((uint)2, equalTime.State.Candidate!.SampleCount);
        Assert.Empty(equalTime.Transitions);
        var before = At(config, equalTime.State, 3, TimeSpan.FromTicks(TimeSpan.FromSeconds(2).Ticks - 1), PatternPhase.Entry);
        Assert.Empty(before.Transitions);
        var exact = At(config, before.State, 4, TimeSpan.FromSeconds(2), PatternPhase.Entry);
        Assert.Equal(PatternPhase.Entry, exact.State.ConfirmedPhase);
        Assert.Single(exact.Transitions);
    }

    [Fact]
    public void CandidateGapEqualityContinuesAndExcessRestarts()
    {
        var config = Configuration(persistence: TimeSpan.FromSeconds(6));
        var start = At(config, Initial(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var exact = At(config, start.State, 2, TimeSpan.FromSeconds(4), PatternPhase.Entry);
        Assert.Equal((uint)2, exact.State.Candidate!.SampleCount);
        var beyond = At(config, start.State, 2, TimeSpan.FromTicks(TimeSpan.FromSeconds(4).Ticks + 1), PatternPhase.Entry);
        Assert.Equal((uint)1, beyond.State.Candidate!.SampleCount);
        Assert.Equal(beyond.Snapshot.SourceTime, beyond.State.Candidate.FirstSourceTime);
        var confirmed = At(config, exact.State, 3, TimeSpan.FromSeconds(8), PatternPhase.Entry);
        Assert.Single(confirmed.Transitions);
    }

    [Fact]
    public void SampleGapEqualityContinuesAndExcessInterrupts()
    {
        var config = Configuration(persistence: TimeSpan.FromSeconds(20));
        var start = At(config, Initial(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var exact = At(config, start.State, 2, TimeSpan.FromSeconds(10), PatternPhase.Entry);
        Assert.Equal(DetectionCycleOutcome.Accepted, exact.Outcome);
        var beyond = At(config, start.State, 2, TimeSpan.FromTicks(TimeSpan.FromSeconds(10).Ticks + 1), PatternPhase.Entry);
        Assert.Equal(DetectionCycleOutcome.Interrupted, beyond.Outcome);
        Assert.Equal(SampleInterruptionReason.SampleContinuityGap, beyond.InterruptionReason);
        Assert.Contains(TelemetryQualityIssue.ContinuityGap, beyond.QualityIssues);
        Assert.Null(beyond.State.Candidate);
        Assert.Equal(PositionContinuity.Interrupted, beyond.State.PositionContinuity);
        Assert.Equal(start.State.LastAcceptedPosition, beyond.State.LastAcceptedPosition);
        var recovered = At(config, beyond.State, 3, TimeSpan.FromSeconds(11), PatternPhase.Entry);
        Assert.Equal((uint)1, recovered.State.Candidate!.SampleCount);
    }

    [Fact]
    public void RequiredMissingObservationsResetCandidateAndPositionLossInterrupts()
    {
        var config = Configuration();
        var point = Point(config, PatternPhase.Entry);
        var start = At(config, Initial(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var samples = new[]
        {
            Sample(2, TimeSpan.FromSeconds(1), point.Position, speed: null, track: point.Track),
            Sample(2, TimeSpan.FromSeconds(1), point.Position, track: null),
            Sample(2, TimeSpan.FromSeconds(1), point.Position, track: point.Track, onGround: null),
            Sample(2, TimeSpan.FromSeconds(1), point.Position, track: point.Track, onGround: true)
        };
        foreach (var sample in samples)
        {
            var held = Step(config, start.State, sample);
            Assert.Equal(DetectionCycleOutcome.Accepted, held.Outcome);
            Assert.Null(held.State.Candidate);
            Assert.Empty(held.Transitions);
        }
        var missing = Step(config, start.State, Sample(2, TimeSpan.FromSeconds(1), null, track: point.Track));
        Assert.Equal(DetectionCycleOutcome.Interrupted, missing.Outcome);
        Assert.Equal(SampleInterruptionReason.PositionContinuityLost, missing.InterruptionReason);
        Assert.Contains(TelemetryQualityIssue.MissingPosition, missing.QualityIssues);
        Assert.Equal(start.State.LastAcceptedPosition, missing.State.LastAcceptedPosition);
        Assert.Equal(PositionContinuity.Interrupted, missing.State.PositionContinuity);
        var resumed = At(config, missing.State, 3, TimeSpan.FromSeconds(2), PatternPhase.Entry);
        Assert.Equal((uint)1, resumed.State.Candidate!.SampleCount);
    }

    [Fact]
    public void OverlappingBaseAndFinalEvidenceHoldsAndResets()
    {
        var config = Configuration(enterTolerance: 50, exitTolerance: 60);
        var attempt = new DetectionAttemptIdentity("algorithm", "config", "epoch", 1);
        var state = new PhaseDetectionState("algorithm", "config", "epoch", 1, TimeSpan.Zero,
            PatternPhase.Downwind, attempt, new(PatternPhase.Base, TimeSpan.Zero, TimeSpan.Zero, 1),
            2, 0, new(false, false, false, false, false));
        var result = Step(config, state, Sample(2, TimeSpan.FromSeconds(2), Geographic(new(-800, 0)), track: 45));
        Assert.Equal(PatternPhase.Downwind, result.State.ConfirmedPhase);
        Assert.Null(result.State.Candidate);
        Assert.Empty(result.Transitions);
    }

    [Fact]
    public void CurrentExitEvidenceRetainsPhaseEvenWhenSuccessorEnters()
    {
        var config = Configuration(exitTolerance: 50);
        var attempt = new DetectionAttemptIdentity("algorithm", "config", "epoch", 1);
        var state = new PhaseDetectionState("algorithm", "config", "epoch", 1, TimeSpan.Zero,
            PatternPhase.Entry, attempt, new(PatternPhase.Downwind, TimeSpan.Zero, TimeSpan.Zero, 1),
            1, 0, new(false, false, false, false, false));
        var join = config.GeometryAuthority.Geometry!.EntryJoinTarget;
        var result = Step(config, state, Sample(2, TimeSpan.FromSeconds(2), Geographic(join), track: 180));
        Assert.Equal(PatternPhase.Entry, result.State.ConfirmedPhase);
        Assert.Null(result.State.Candidate);
        Assert.Empty(result.Transitions);
    }

    [Fact]
    public void MinimumUsableSpeedIsExplicitInclusiveAndResetsCandidateBelowIt()
    {
        var config = Configuration();
        var point = Point(config, PatternPhase.Entry);
        var usable = Step(config, Initial(), Sample(1, TimeSpan.Zero, point.Position,
            speed: 5, track: point.Track), 5);
        Assert.Equal(PatternPhase.Entry, usable.State.Candidate?.Phase);
        var below = Step(config, usable.State, Sample(2, TimeSpan.FromSeconds(1), point.Position,
            speed: double.BitDecrement(5), track: point.Track), 5);
        Assert.Null(below.State.Candidate);
        Assert.Throws<ArgumentOutOfRangeException>(() => Step(config, Initial(),
            Sample(1, TimeSpan.Zero, point.Position, track: point.Track), 0));
    }

    [Fact]
    public void BaseTurnWindowAloneDoesNotConfirmBase()
    {
        var config = Configuration();
        var attempt = new DetectionAttemptIdentity("algorithm", "config", "epoch", 1);
        var state = new PhaseDetectionState("algorithm", "config", "epoch", 1, TimeSpan.Zero,
            PatternPhase.Downwind, attempt, null, 2, 0, new(false, false, false, false, false));
        var result = Step(config, state, Sample(2, TimeSpan.FromSeconds(2), Geographic(new(-800, -800)), track: 180));
        Assert.Equal(PatternPhase.Downwind, result.State.ConfirmedPhase);
        Assert.Null(result.State.Candidate);
    }

    [Fact]
    public void RejectionPrecedencePreservesHistoryAndCount()
    {
        var config = Configuration();
        var initial = At(config, Initial(), 2, TimeSpan.FromSeconds(2), PatternPhase.Entry).State;
        var point = Point(config, PatternPhase.Entry);
        var cases = new[]
        {
            (Sample(1, TimeSpan.Zero, point.Position, epoch: "other"), SampleRejectionReason.EpochMismatch),
            (Sample(2, TimeSpan.Zero, point.Position), SampleRejectionReason.DuplicateSequence),
            (Sample(1, TimeSpan.Zero, point.Position), SampleRejectionReason.ReversedSequence),
            (Sample(3, TimeSpan.FromSeconds(1), point.Position), SampleRejectionReason.ReversedSourceTime)
        };
        foreach (var (sample, reason) in cases)
        {
            var result = Step(config, initial, sample);
            Assert.Equal(DetectionCycleOutcome.Rejected, result.Outcome);
            Assert.Equal(reason, result.RejectionReason);
            Assert.Equal(initial.LastSampleSequence, result.State.LastSampleSequence);
            Assert.Equal(initial.Candidate, result.State.Candidate);
            Assert.Equal(initial.LastAcceptedPosition, result.State.LastAcceptedPosition);
            Assert.Equal((uint)1, result.State.RejectedSampleCount);
            Assert.Empty(result.Transitions);
        }
    }

    [Fact]
    public void RelocationUsesDistanceOrSpeedAndZeroTimeRule()
    {
        var a = new GeographicCoordinate(0, 0);
        var b = new GeographicCoordinate(0.0001, 0);
        var reference = Configuration().GeometryAuthority.RunwayReference;
        var pa = reference.ToRunway(a);
        var pb = reference.ToRunway(b);
        var distance = double.Hypot(pb.AlongTrackMetres - pa.AlongTrackMetres,
            pb.CrossTrackMetres - pa.CrossTrackMetres);
        var acceptedConfig = Configuration(relocationSpeed: distance, relocationDistance: distance);
        var initial = Step(acceptedConfig, Initial(), Sample(1, TimeSpan.Zero, a, onGround: true)).State;
        var exact = Step(acceptedConfig, initial, Sample(2, TimeSpan.FromSeconds(1), b, onGround: true));
        Assert.Equal(DetectionCycleOutcome.Accepted, exact.Outcome);
        var distanceFailure = Step(Configuration(relocationSpeed: 1_000, relocationDistance: double.BitDecrement(distance)),
            initial, Sample(2, TimeSpan.FromSeconds(1), b, onGround: true));
        Assert.Equal(SampleRejectionReason.ImplausibleRelocation, distanceFailure.RejectionReason);
        var speedFailure = Step(Configuration(relocationSpeed: double.BitDecrement(distance), relocationDistance: 1_000),
            initial, Sample(2, TimeSpan.FromSeconds(1), b, onGround: true));
        Assert.Equal(SampleRejectionReason.ImplausibleRelocation, speedFailure.RejectionReason);
        Assert.Equal(DetectionCycleOutcome.Accepted,
            Step(acceptedConfig, initial, Sample(2, TimeSpan.Zero, a, onGround: true)).Outcome);
        Assert.Equal(SampleRejectionReason.ImplausibleRelocation,
            Step(acceptedConfig, initial, Sample(2, TimeSpan.Zero, b, onGround: true)).RejectionReason);
    }

    [Fact]
    public void InterruptedAnchorIsNotUsedForNextRelocation()
    {
        var config = Configuration(relocationSpeed: 1, relocationDistance: 1);
        var a = new GeographicCoordinate(0, 0);
        var far = new GeographicCoordinate(0.01, 0.01);
        var start = Step(config, Initial(), Sample(1, TimeSpan.Zero, a, onGround: true));
        var gap = Step(config, start.State, Sample(2, TimeSpan.FromSeconds(11), far, onGround: true));
        Assert.Equal(DetectionCycleOutcome.Interrupted, gap.Outcome);
        var recovered = Step(config, gap.State, Sample(3, TimeSpan.FromSeconds(12), far, onGround: true));
        Assert.Equal(DetectionCycleOutcome.Accepted, recovered.Outcome);
        Assert.Equal(far, recovered.State.LastAcceptedPosition!.Position);
        Assert.Equal(PositionContinuity.Continuous, recovered.State.PositionContinuity);
    }

    [Fact]
    public void CheckedCountersCannotWrap()
    {
        var config = Configuration();
        var rejectedMax = new PhaseDetectionState("algorithm", "config", "epoch", null, null,
            PatternPhase.Unknown, null, null, 0, uint.MaxValue, new(false, false, false, false, false));
        Assert.Throws<OverflowException>(() => Step(config, rejectedMax,
            Sample(1, TimeSpan.Zero, null, epoch: "other")));

        var point = Point(config, PatternPhase.Entry);
        var anchor = new AcceptedPositionEvidence(point.Position, 1, TimeSpan.Zero);
        PhaseDetectionState Pending(uint transitionCount, uint samples) =>
            new("algorithm", "config", "epoch", 1, TimeSpan.Zero, PatternPhase.Unknown, null,
                new(PatternPhase.Entry, TimeSpan.Zero, TimeSpan.Zero, samples), transitionCount, 0,
                new(false, false, false, false, false), anchor, PositionContinuity.Continuous);
        Assert.Throws<OverflowException>(() => At(config, Pending(uint.MaxValue, 1), 2,
            TimeSpan.FromSeconds(2), PatternPhase.Entry));
        Assert.Throws<OverflowException>(() => At(config, Pending(0, uint.MaxValue), 2,
            TimeSpan.FromSeconds(1), PatternPhase.Entry));
    }

    [Fact]
    public void ReplayWithSameExplicitSpeedProducesEqualStatesAndEvents()
    {
        var config = Configuration();
        var point = Point(config, PatternPhase.Entry);
        var samples = new[]
        {
            Sample(1, TimeSpan.Zero, point.Position, track: point.Track),
            Sample(2, TimeSpan.FromSeconds(2), point.Position, track: point.Track),
            Sample(2, TimeSpan.FromSeconds(2), point.Position, track: point.Track),
            Sample(3, TimeSpan.FromSeconds(3), point.Position, track: point.Track)
        };
        List<PhaseDetectionResult> Run(double minimumSpeed)
        {
            var state = Initial();
            var results = new List<PhaseDetectionResult>();
            foreach (var sample in samples)
            {
                var result = Step(config, state, sample, minimumSpeed);
                results.Add(result);
                state = result.State;
            }
            return results;
        }
        var first = Run(5);
        var second = Run(5);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].State, second[i].State);
            Assert.Equal(first[i].Outcome, second[i].Outcome);
            Assert.Equal(first[i].RejectionReason, second[i].RejectionReason);
            Assert.Equal(first[i].QualityIssues, second[i].QualityIssues);
            Assert.Equal(first[i].Transitions, second[i].Transitions);
            Assert.Equal(first[i].Cues, second[i].Cues);
        }
        Assert.NotEqual(first[1].State, Run(30)[1].State);
    }
}
