using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;
using XFlight.Pattern.Core.Geometry;
using XFlight.Pattern.Core.PhaseDetection;

namespace XFlight.Pattern.Core.Tests;

public sealed class DeterministicPhaseTransitionTests
{
    private static PhaseDetectionConfiguration Configuration(TrafficPatternSide side = TrafficPatternSide.Left,
        TimeSpan? persistence = null, double enterTolerance = 15, double exitTolerance = 25,
        double relocationSpeed = 10_000, double relocationDistance = 10_000,
        TimeSpan? contactPersistence = null, TimeSpan? departurePersistence = null,
        double minimumClimb = 1, bool eastbound = false)
    {
        var circuit = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(0)),
            TrafficPatternSideResolver.Resolve(published: side), PatternAltitudeResolution.Unresolved);
        var runway = new RunwayGeometryReference(new AirportIdentifier("TEST"), "01", new(0, 0),
            eastbound ? new(0, 0.01) : new(0.01, 0));
        var geometry = PatternGeometryResult.Generate(circuit, runway, PatternGeometryValidationTests.Parameters());
        var policy = new PhaseDetectionPolicy(persistence ?? TimeSpan.FromSeconds(2), contactPersistence ?? TimeSpan.FromSeconds(3),
            departurePersistence ?? TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(4), 10, 20, enterTolerance, exitTolerance,
            10, 20, minimumClimb, 30, relocationSpeed, relocationDistance);
        return new("algorithm", "config", geometry, policy, 25);
    }

    private static PhaseReplayContext Context(PhaseDetectionConfiguration configuration, double minimumSpeed = 5) =>
        new(configuration, "epoch", minimumSpeed);

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
        double? speed = 25, double? track = 0, bool? onGround = false, string epoch = "epoch", double? climb = null) =>
        new(epoch, sequence, time,
            position is { } p
                ? new(p, TelemetryValidity.Available, TelemetryProvenance.Measured)
                : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable),
            Number(null), Number(speed), Number(track), Number(climb),
            onGround is { } ground
                ? new(ground, TelemetryValidity.Available, TelemetryProvenance.Measured)
                : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable));

    private static (PhaseDetectionResult Result, PhaseReplayCursor NextCursor) At(
        PhaseReplayContext context, PhaseReplayCursor cursor, ulong sequence, TimeSpan time,
        PatternPhase target, bool? onGround = false)
    {
        var point = Point(context.Configuration, target);
        return context.Advance(cursor, Sample(sequence, time, point.Position, track: point.Track,
            onGround: onGround));
    }

    private static (PhaseDetectionResult Result, PhaseReplayCursor NextCursor) ConfirmThrough(
        PhaseReplayContext context, PatternPhase target)
    {
        var cursor = context.Start();
        ulong sequence = 0;
        foreach (var phase in new[] { PatternPhase.Entry, PatternPhase.Downwind, PatternPhase.Base, PatternPhase.Final })
        {
            var firstTime = TimeSpan.FromSeconds((sequence / 2) * 3);
            var first = At(context, cursor, ++sequence, firstTime, phase);
            Assert.Equal(phase, first.Result.State.Candidate?.Phase);
            var confirmed = At(context, first.NextCursor, ++sequence,
                firstTime + TimeSpan.FromSeconds(2), phase);
            Assert.Equal(phase, confirmed.Result.State.ConfirmedPhase);
            if (phase == target) return confirmed;
            cursor = confirmed.NextCursor;
        }
        throw new ArgumentOutOfRangeException(nameof(target));
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void FourInboundTransitionsHaveOneEventStableAttemptAndNoCues(TrafficPatternSide side)
    {
        var config = Configuration(side);
        var context = Context(config);
        var cursor = context.Start();
        var priorPhase = PatternPhase.Unknown;
        DetectionAttemptIdentity? attempt = null;
        ulong sequence = 0;
        foreach (var target in new[] { PatternPhase.Entry, PatternPhase.Downwind, PatternPhase.Base, PatternPhase.Final })
        {
            var firstTime = TimeSpan.FromSeconds((sequence / 2) * 3);
            var first = At(context, cursor, ++sequence, firstTime, target);
            Assert.Equal(priorPhase, first.Result.State.ConfirmedPhase);
            Assert.Equal(target, first.Result.State.Candidate?.Phase);
            Assert.Empty(first.Result.Transitions);
            var confirmed = At(context, first.NextCursor, ++sequence,
                firstTime + TimeSpan.FromSeconds(2), target);
            Assert.Equal(DetectionCycleOutcome.Accepted, confirmed.Result.Outcome);
            Assert.Equal(target, confirmed.Result.State.ConfirmedPhase);
            Assert.Null(confirmed.Result.State.Candidate);
            var transition = Assert.Single(confirmed.Result.Transitions);
            Assert.Equal(priorPhase, transition.From);
            Assert.Equal(target, transition.To);
            Assert.Equal(sequence, transition.Identity.SampleSequence);
            Assert.Equal((uint)0, transition.Identity.Ordinal);
            Assert.Equal((uint)(sequence / 2), confirmed.Result.State.TransitionCount);
            attempt ??= confirmed.Result.State.AttemptId;
            Assert.Equal((ulong)2, attempt!.StartSampleSequence);
            Assert.Equal(attempt, transition.AttemptId);
            Assert.Equal(attempt, confirmed.Result.State.AttemptId);
            Assert.Empty(confirmed.Result.Cues);
            Assert.Equal(new PhaseCueLatches(false, false, false, false, false),
                confirmed.Result.State.CueLatches);
            cursor = confirmed.NextCursor;
            priorPhase = target;
        }
    }

    [Fact]
    public void UnsupportedInitialAndReverseTransitionsHold()
    {
        var context = Context(Configuration());
        var downwind = At(context, context.Start(), 1, TimeSpan.Zero, PatternPhase.Downwind);
        Assert.Equal(PatternPhase.Unknown, downwind.Result.State.ConfirmedPhase);
        Assert.Null(downwind.Result.State.Candidate);

        var final = ConfirmThrough(context, PatternPhase.Final);
        var reverse = At(context, final.NextCursor, 9, TimeSpan.FromSeconds(12), PatternPhase.Base);
        Assert.Equal(PatternPhase.Final, reverse.Result.State.ConfirmedPhase);
        Assert.Empty(reverse.Result.Transitions);
    }

    [Fact]
    public void PersistenceIsInclusiveAndEqualTimeAddsNoDuration()
    {
        var context = Context(Configuration());
        var start = At(context, context.Start(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var equalTime = At(context, start.NextCursor, 2, TimeSpan.Zero, PatternPhase.Entry);
        Assert.Equal((uint)2, equalTime.Result.State.Candidate!.SampleCount);
        Assert.Empty(equalTime.Result.Transitions);
        var before = At(context, equalTime.NextCursor, 3,
            TimeSpan.FromTicks(TimeSpan.FromSeconds(2).Ticks - 1), PatternPhase.Entry);
        Assert.Empty(before.Result.Transitions);
        var exact = At(context, before.NextCursor, 4, TimeSpan.FromSeconds(2), PatternPhase.Entry);
        Assert.Equal(PatternPhase.Entry, exact.Result.State.ConfirmedPhase);
        Assert.Single(exact.Result.Transitions);
    }

    [Fact]
    public void CandidateGapEqualityContinuesAndExcessRestarts()
    {
        var context = Context(Configuration(persistence: TimeSpan.FromSeconds(6)));
        var start = At(context, context.Start(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var exact = At(context, start.NextCursor, 2, TimeSpan.FromSeconds(4), PatternPhase.Entry);
        Assert.Equal((uint)2, exact.Result.State.Candidate!.SampleCount);
        var beyond = At(context, start.NextCursor, 2,
            TimeSpan.FromTicks(TimeSpan.FromSeconds(4).Ticks + 1), PatternPhase.Entry);
        Assert.Equal((uint)1, beyond.Result.State.Candidate!.SampleCount);
        Assert.Equal(beyond.Result.Snapshot.SourceTime, beyond.Result.State.Candidate.FirstSourceTime);
        var confirmed = At(context, exact.NextCursor, 3, TimeSpan.FromSeconds(8), PatternPhase.Entry);
        Assert.Single(confirmed.Result.Transitions);
    }

    [Fact]
    public void SampleGapEqualityContinuesAndExcessInterrupts()
    {
        var context = Context(Configuration(persistence: TimeSpan.FromSeconds(20)));
        var start = At(context, context.Start(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var exact = At(context, start.NextCursor, 2, TimeSpan.FromSeconds(10), PatternPhase.Entry);
        Assert.Equal(DetectionCycleOutcome.Accepted, exact.Result.Outcome);
        var beyond = At(context, start.NextCursor, 2,
            TimeSpan.FromTicks(TimeSpan.FromSeconds(10).Ticks + 1), PatternPhase.Entry);
        Assert.Equal(DetectionCycleOutcome.Interrupted, beyond.Result.Outcome);
        Assert.Equal(SampleInterruptionReason.SampleContinuityGap, beyond.Result.InterruptionReason);
        Assert.Contains(TelemetryQualityIssue.ContinuityGap, beyond.Result.QualityIssues);
        Assert.Null(beyond.Result.State.Candidate);
        Assert.Equal(PositionContinuity.Interrupted, beyond.Result.State.PositionContinuity);
        Assert.Equal(start.Result.State.LastAcceptedPosition, beyond.Result.State.LastAcceptedPosition);
        var recovered = At(context, beyond.NextCursor, 3, TimeSpan.FromSeconds(11), PatternPhase.Entry);
        Assert.Equal((uint)1, recovered.Result.State.Candidate!.SampleCount);
    }

    [Fact]
    public void RequiredMissingObservationsResetCandidateAndPositionLossInterrupts()
    {
        var context = Context(Configuration());
        var point = Point(context.Configuration, PatternPhase.Entry);
        var start = At(context, context.Start(), 1, TimeSpan.Zero, PatternPhase.Entry);
        var samples = new[]
        {
            Sample(2, TimeSpan.FromSeconds(1), point.Position, speed: null, track: point.Track),
            Sample(2, TimeSpan.FromSeconds(1), point.Position, track: null),
            Sample(2, TimeSpan.FromSeconds(1), point.Position, track: point.Track, onGround: null),
            Sample(2, TimeSpan.FromSeconds(1), point.Position, track: point.Track, onGround: true)
        };
        foreach (var sample in samples)
        {
            var held = context.Advance(start.NextCursor, sample);
            Assert.Equal(DetectionCycleOutcome.Accepted, held.Result.Outcome);
            Assert.Null(held.Result.State.Candidate);
            Assert.Empty(held.Result.Transitions);
        }
        var missing = context.Advance(start.NextCursor,
            Sample(2, TimeSpan.FromSeconds(1), null, track: point.Track));
        Assert.Equal(DetectionCycleOutcome.Interrupted, missing.Result.Outcome);
        Assert.Equal(SampleInterruptionReason.PositionContinuityLost, missing.Result.InterruptionReason);
        Assert.Contains(TelemetryQualityIssue.MissingPosition, missing.Result.QualityIssues);
        Assert.Equal(start.Result.State.LastAcceptedPosition, missing.Result.State.LastAcceptedPosition);
        Assert.Equal(PositionContinuity.Interrupted, missing.Result.State.PositionContinuity);
        var resumed = At(context, missing.NextCursor, 3, TimeSpan.FromSeconds(2), PatternPhase.Entry);
        Assert.Equal((uint)1, resumed.Result.State.Candidate!.SampleCount);
    }

    [Fact]
    public void OverlappingBaseAndFinalEvidenceHoldsAndResets()
    {
        var context = Context(Configuration(enterTolerance: 50, exitTolerance: 60));
        var downwind = ConfirmThrough(context, PatternPhase.Downwind);
        var pending = At(context, downwind.NextCursor, 5, TimeSpan.FromSeconds(6), PatternPhase.Base);
        Assert.Equal(PatternPhase.Base, pending.Result.State.Candidate?.Phase);
        var result = context.Advance(pending.NextCursor,
            Sample(6, TimeSpan.FromSeconds(8), Geographic(new(-800, 0)), track: 45));
        Assert.Equal(PatternPhase.Downwind, result.Result.State.ConfirmedPhase);
        Assert.Null(result.Result.State.Candidate);
        Assert.Empty(result.Result.Transitions);
    }

    [Fact]
    public void CurrentExitEvidenceRetainsPhaseEvenWhenSuccessorEnters()
    {
        var context = Context(Configuration(exitTolerance: 50));
        var entry = ConfirmThrough(context, PatternPhase.Entry);
        var pending = At(context, entry.NextCursor, 3, TimeSpan.FromSeconds(3), PatternPhase.Downwind);
        Assert.Equal(PatternPhase.Downwind, pending.Result.State.Candidate?.Phase);
        var join = context.Configuration.GeometryAuthority.Geometry!.EntryJoinTarget;
        var result = context.Advance(pending.NextCursor,
            Sample(4, TimeSpan.FromSeconds(5), Geographic(join), track: 180));
        Assert.Equal(PatternPhase.Entry, result.Result.State.ConfirmedPhase);
        Assert.Null(result.Result.State.Candidate);
        Assert.Empty(result.Result.Transitions);
    }

    [Fact]
    public void MinimumUsableSpeedIsExplicitInclusiveAndResetsCandidateBelowIt()
    {
        var config = Configuration();
        var context = Context(config);
        var point = Point(config, PatternPhase.Entry);
        var usable = context.Advance(context.Start(), Sample(1, TimeSpan.Zero, point.Position,
            speed: 5, track: point.Track));
        Assert.Equal(PatternPhase.Entry, usable.Result.State.Candidate?.Phase);
        var below = context.Advance(usable.NextCursor, Sample(2, TimeSpan.FromSeconds(1), point.Position,
            speed: double.BitDecrement(5), track: point.Track));
        Assert.Null(below.Result.State.Candidate);
        Assert.Throws<ArgumentOutOfRangeException>(() => Context(config, 0));
    }

    [Fact]
    public void BaseTurnWindowAloneDoesNotConfirmBase()
    {
        var context = Context(Configuration());
        var downwind = ConfirmThrough(context, PatternPhase.Downwind);
        var result = context.Advance(downwind.NextCursor,
            Sample(5, TimeSpan.FromSeconds(6), Geographic(new(-800, -800)), track: 180));
        Assert.Equal(PatternPhase.Downwind, result.Result.State.ConfirmedPhase);
        Assert.Null(result.Result.State.Candidate);
    }

    [Fact]
    public void RejectionPrecedencePreservesHistoryAndCount()
    {
        var context = Context(Configuration());
        var initial = At(context, context.Start(), 2, TimeSpan.FromSeconds(2), PatternPhase.Entry);
        var point = Point(context.Configuration, PatternPhase.Entry);
        var cases = new[]
        {
            (Sample(1, TimeSpan.Zero, point.Position, epoch: "other"), SampleRejectionReason.EpochMismatch),
            (Sample(2, TimeSpan.Zero, point.Position), SampleRejectionReason.DuplicateSequence),
            (Sample(1, TimeSpan.Zero, point.Position), SampleRejectionReason.ReversedSequence),
            (Sample(3, TimeSpan.FromSeconds(1), point.Position), SampleRejectionReason.ReversedSourceTime)
        };
        foreach (var (sample, reason) in cases)
        {
            var result = context.Advance(initial.NextCursor, sample);
            Assert.Equal(DetectionCycleOutcome.Rejected, result.Result.Outcome);
            Assert.Equal(reason, result.Result.RejectionReason);
            Assert.Equal(initial.Result.State.LastSampleSequence, result.Result.State.LastSampleSequence);
            Assert.Equal(initial.Result.State.Candidate, result.Result.State.Candidate);
            Assert.Equal(initial.Result.State.LastAcceptedPosition, result.Result.State.LastAcceptedPosition);
            Assert.Equal((uint)1, result.Result.State.RejectedSampleCount);
            Assert.Empty(result.Result.Transitions);
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
        var accepted = Context(Configuration(relocationSpeed: distance, relocationDistance: distance));
        var acceptedStart = accepted.Advance(accepted.Start(), Sample(1, TimeSpan.Zero, a, onGround: true));
        Assert.Equal(DetectionCycleOutcome.Accepted, accepted.Advance(acceptedStart.NextCursor,
            Sample(2, TimeSpan.FromSeconds(1), b, onGround: true)).Result.Outcome);

        PhaseDetectionResult Relocate(double speedLimit, double distanceLimit, GeographicCoordinate destination,
            TimeSpan time)
        {
            var context = Context(Configuration(relocationSpeed: speedLimit, relocationDistance: distanceLimit));
            var start = context.Advance(context.Start(), Sample(1, TimeSpan.Zero, a, onGround: true));
            return context.Advance(start.NextCursor, Sample(2, time, destination, onGround: true)).Result;
        }

        Assert.Equal(SampleRejectionReason.ImplausibleRelocation,
            Relocate(1_000, double.BitDecrement(distance), b, TimeSpan.FromSeconds(1)).RejectionReason);
        Assert.Equal(SampleRejectionReason.ImplausibleRelocation,
            Relocate(double.BitDecrement(distance), 1_000, b, TimeSpan.FromSeconds(1)).RejectionReason);
        Assert.Equal(DetectionCycleOutcome.Accepted,
            Relocate(distance, distance, a, TimeSpan.Zero).Outcome);
        Assert.Equal(SampleRejectionReason.ImplausibleRelocation,
            Relocate(distance, distance, b, TimeSpan.Zero).RejectionReason);
    }

    [Fact]
    public void InterruptedAnchorIsNotUsedForNextRelocation()
    {
        var context = Context(Configuration(relocationSpeed: 1, relocationDistance: 1));
        var a = new GeographicCoordinate(0, 0);
        var far = new GeographicCoordinate(0.01, 0.01);
        var start = context.Advance(context.Start(), Sample(1, TimeSpan.Zero, a, onGround: true));
        var gap = context.Advance(start.NextCursor, Sample(2, TimeSpan.FromSeconds(11), far, onGround: true));
        Assert.Equal(DetectionCycleOutcome.Interrupted, gap.Result.Outcome);
        var recovered = context.Advance(gap.NextCursor,
            Sample(3, TimeSpan.FromSeconds(12), far, onGround: true));
        Assert.Equal(DetectionCycleOutcome.Accepted, recovered.Result.Outcome);
        Assert.Equal(far, recovered.Result.State.LastAcceptedPosition!.Position);
        Assert.Equal(PositionContinuity.Continuous, recovered.Result.State.PositionContinuity);
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
            var context = Context(config, minimumSpeed);
            var cursor = context.Start();
            var results = new List<PhaseDetectionResult>();
            foreach (var sample in samples)
            {
                var step = context.Advance(cursor, sample);
                results.Add(step.Result);
                cursor = step.NextCursor;
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

    [Fact]
    public void TrustedOriginHasFixedInputsAndNoPublicStateInjectionPath()
    {
        var config = Configuration();
        var context = Context(config);
        var origin = context.Start();
        Assert.Same(config, context.Configuration);
        Assert.Equal("epoch", context.StreamEpoch);
        Assert.Equal(5, context.MinimumUsableGroundSpeedMetresPerSecond);

        Assert.Empty(typeof(PhaseReplayCursor).GetConstructors());
        Assert.DoesNotContain(typeof(PhaseReplayCursor).GetProperties(), property =>
            property.PropertyType == typeof(PhaseDetectionState));
        Assert.DoesNotContain(typeof(PhaseReplayContext).GetMethods(), method =>
            method.GetParameters().Any(parameter => parameter.ParameterType == typeof(PhaseDetectionState)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Context(config, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Context(config, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Context(config, double.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => new PhaseReplayContext(config, " ", 5));

        var first = context.Advance(origin,
            Sample(1, TimeSpan.Zero, new GeographicCoordinate(0, 0), onGround: true));
        Assert.Equal(PatternPhase.Unknown, first.Result.State.ConfirmedPhase);
        Assert.Null(first.Result.State.AttemptId);
        Assert.Null(first.Result.State.Candidate);
        Assert.Equal((uint)0, first.Result.State.TransitionCount);
        Assert.Equal((uint)0, first.Result.State.RejectedSampleCount);
        Assert.Equal(new PhaseCueLatches(false, false, false, false, false), first.Result.State.CueLatches);
    }

    [Fact]
    public void ContextBindsSpeedAndExactConfigurationDespiteReusedIds()
    {
        var config = Configuration();
        var alteredPolicy = Configuration(enterTolerance: 20);
        var low = new PhaseReplayContext(config, "epoch", 5);
        var high = new PhaseReplayContext(config, "epoch", 30);
        var altered = new PhaseReplayContext(alteredPolicy, "epoch", 5);
        var point = Point(config, PatternPhase.Entry);
        var sample = Sample(1, TimeSpan.Zero, point.Position, speed: 25, track: point.Track);

        var lowStep = low.Advance(low.Start(), sample);
        var highStep = high.Advance(high.Start(), sample);
        Assert.Equal(PatternPhase.Entry, lowStep.Result.State.Candidate?.Phase);
        Assert.Null(highStep.Result.State.Candidate);
        Assert.Same(config, low.Configuration);
        Assert.Same(alteredPolicy, altered.Configuration);
        Assert.Equal(config.ConfigurationId, alteredPolicy.ConfigurationId);
        Assert.NotEqual(config.Policy.TrackEnterToleranceDegrees,
            alteredPolicy.Policy.TrackEnterToleranceDegrees);
        Assert.Throws<ArgumentException>(() => high.Advance(lowStep.NextCursor, sample));
        Assert.Throws<ArgumentException>(() => altered.Advance(lowStep.NextCursor, sample));
        Assert.Throws<ArgumentException>(() => new PhaseReplayContext(config, "epoch", 5)
            .Advance(lowStep.NextCursor, sample));
    }

    [Fact]
    public void EveryOutcomeReturnsTrustedImmutableSuccessorAndPreservesRejectionCount()
    {
        var context = Context(Configuration());
        var origin = context.Start();
        var point = Point(context.Configuration, PatternPhase.Entry);
        var accepted = context.Advance(origin, Sample(1, TimeSpan.Zero, point.Position,
            track: point.Track));
        Assert.Equal(DetectionCycleOutcome.Accepted, accepted.Result.Outcome);

        var interrupted = context.Advance(accepted.NextCursor,
            Sample(2, TimeSpan.FromSeconds(1), null, track: point.Track));
        Assert.Equal(DetectionCycleOutcome.Interrupted, interrupted.Result.Outcome);
        Assert.Equal(PositionContinuity.Interrupted, interrupted.Result.State.PositionContinuity);

        var wrongEpoch = context.Advance(interrupted.NextCursor,
            Sample(3, TimeSpan.FromSeconds(2), point.Position, track: point.Track, epoch: "other"));
        Assert.Equal(DetectionCycleOutcome.Rejected, wrongEpoch.Result.Outcome);
        Assert.Equal(SampleRejectionReason.EpochMismatch, wrongEpoch.Result.RejectionReason);
        Assert.Equal((uint)1, wrongEpoch.Result.State.RejectedSampleCount);
        Assert.Equal(interrupted.Result.State.LastSampleSequence, wrongEpoch.Result.State.LastSampleSequence);

        var duplicate = context.Advance(wrongEpoch.NextCursor,
            Sample(2, TimeSpan.FromSeconds(1), point.Position, track: point.Track));
        Assert.Equal(SampleRejectionReason.DuplicateSequence, duplicate.Result.RejectionReason);
        Assert.Equal((uint)2, duplicate.Result.State.RejectedSampleCount);

        var resumed = context.Advance(duplicate.NextCursor,
            Sample(3, TimeSpan.FromSeconds(2), point.Position, track: point.Track));
        Assert.Equal(DetectionCycleOutcome.Accepted, resumed.Result.Outcome);
        Assert.Equal((uint)2, resumed.Result.State.RejectedSampleCount);
        Assert.Equal(PositionContinuity.Continuous, resumed.Result.State.PositionContinuity);
        foreach (var step in new[] { accepted, interrupted, wrongEpoch, duplicate, resumed })
        {
            Assert.Empty(step.Result.Cues);
            Assert.Equal(new PhaseCueLatches(false, false, false, false, false),
                step.Result.State.CueLatches);
        }
        var firstAgain = context.Advance(origin, Sample(1, TimeSpan.Zero, point.Position,
            track: point.Track));
        Assert.Equal(accepted.Result.State, firstAgain.Result.State);
    }

    [Fact]
    public void SameOriginAndSamplesProduceEqualResultsWithoutMutableCursorState()
    {
        var context = Context(Configuration());
        var point = Point(context.Configuration, PatternPhase.Entry);
        var samples = new[]
        {
            Sample(1, TimeSpan.Zero, point.Position, track: point.Track),
            Sample(2, TimeSpan.FromSeconds(2), point.Position, track: point.Track),
            Sample(2, TimeSpan.FromSeconds(2), point.Position, track: point.Track),
            Sample(3, TimeSpan.FromSeconds(3), null, track: point.Track)
        };
        List<PhaseDetectionResult> Run()
        {
            var cursor = context.Start();
            var results = new List<PhaseDetectionResult>();
            foreach (var sample in samples)
            {
                var step = context.Advance(cursor, sample);
                results.Add(step.Result);
                cursor = step.NextCursor;
            }
            return results;
        }

        var first = Run();
        var second = Run();
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].State, second[i].State);
            Assert.Equal(first[i].Outcome, second[i].Outcome);
            Assert.Equal(first[i].RejectionReason, second[i].RejectionReason);
            Assert.Equal(first[i].InterruptionReason, second[i].InterruptionReason);
            Assert.Equal(first[i].QualityIssues, second[i].QualityIssues);
            Assert.Equal(first[i].Transitions, second[i].Transitions);
            Assert.Equal(first[i].Cues, second[i].Cues);
        }

        var origin = context.Start();
        var branchA = context.Advance(origin, samples[0]);
        var branchB = context.Advance(origin, samples[0]);
        Assert.Equal(branchA.Result.State, branchB.Result.State);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void TrustedReplayPreservesFourInboundTransitionsWithoutCues(TrafficPatternSide side)
    {
        var context = Context(Configuration(side));
        var cursor = context.Start();
        var phases = new[] { PatternPhase.Entry, PatternPhase.Downwind, PatternPhase.Base, PatternPhase.Final };
        ulong sequence = 0;
        PhaseDetectionResult? lastResult = null;
        foreach (var target in phases)
        {
            var firstTime = TimeSpan.FromSeconds((sequence / 2) * 3);
            var first = At(context, cursor, ++sequence, firstTime, target);
            Assert.Equal(target, first.Result.State.Candidate?.Phase);
            var confirmed = At(context, first.NextCursor, ++sequence,
                firstTime + TimeSpan.FromSeconds(2), target);
            Assert.Equal(target, confirmed.Result.State.ConfirmedPhase);
            Assert.Single(confirmed.Result.Transitions);
            Assert.Empty(confirmed.Result.Cues);
            Assert.Equal(new PhaseCueLatches(false, false, false, false, false),
                confirmed.Result.State.CueLatches);
            cursor = confirmed.NextCursor;
            lastResult = confirmed.Result;
        }
        Assert.Equal((uint)4, lastResult!.State.TransitionCount);
    }

    private static TelemetrySnapshot DepartureSample(ulong sequence, double seconds, bool? onGround = false,
        double? speed = 25, double? track = 0, double? climb = 2,
        RunwayCoordinate? point = null, bool positionAvailable = true, string epoch = "epoch") =>
        Sample(sequence, TimeSpan.FromSeconds(seconds), positionAvailable ? Geographic(point ?? new(500, 0)) : null,
            speed, track, onGround, epoch, climb);

    private static (PhaseDetectionResult Result, PhaseReplayCursor NextCursor) DepartureGate(PhaseReplayContext context)
    {
        var first = context.Advance(context.Start(), DepartureSample(1, 0, onGround: true));
        return context.Advance(first.NextCursor, DepartureSample(2, 3, onGround: true));
    }

    [Fact]
    public void DepartureContactAndAirborneDurationsAreSeparateAndInclusive()
    {
        var context = Context(Configuration());
        var first = context.Advance(context.Start(), DepartureSample(1, 0, onGround: true));
        var equal = context.Advance(first.NextCursor, DepartureSample(2, 0, onGround: true));
        Assert.Equal((uint)2, equal.Result.State.RunwayContact!.SampleCount);
        var beforeTime = TimeSpan.FromSeconds(3) - TimeSpan.FromTicks(1);
        var before = context.Advance(equal.NextCursor,
            Sample(3, beforeTime, Geographic(new(500, 0)), onGround: true));
        var tooSoon = context.Advance(before.NextCursor, DepartureSample(4, 3));
        Assert.Null(tooSoon.Result.State.Candidate);
        Assert.Null(tooSoon.Result.State.RunwayContact);
        var exact = context.Advance(before.NextCursor, DepartureSample(4, 3, onGround: true));
        Assert.Equal(TimeSpan.FromSeconds(3), exact.Result.State.RunwayContact!.LastSourceTime);
        Assert.Null(exact.Result.State.AttemptId);
        Assert.Empty(exact.Result.Transitions);
        var lift = context.Advance(exact.NextCursor, DepartureSample(5, 3));
        Assert.Equal(PatternPhase.Departure, lift.Result.State.Candidate!.Phase);
        Assert.Equal(TimeSpan.FromSeconds(3), lift.Result.State.Candidate.FirstSourceTime);
        Assert.Equal(exact.Result.State.RunwayContact, lift.Result.State.RunwayContact);
        Assert.Empty(lift.Result.Transitions);
        var sameTime = context.Advance(lift.NextCursor, DepartureSample(6, 3));
        Assert.Equal((uint)2, sameTime.Result.State.Candidate!.SampleCount);
        var beforeDeparture = context.Advance(sameTime.NextCursor,
            Sample(7, TimeSpan.FromSeconds(7) - TimeSpan.FromTicks(1), Geographic(new(500, 0)), climb: 2));
        Assert.Empty(beforeDeparture.Result.Transitions);
        var confirmed = context.Advance(beforeDeparture.NextCursor, DepartureSample(8, 7));
        var transition = Assert.Single(confirmed.Result.Transitions);
        Assert.Equal(PatternPhase.Unknown, transition.From);
        Assert.Equal(PatternPhase.Departure, transition.To);
        Assert.Equal(new DetectionEventIdentity("algorithm", "config", "epoch", 8, 0), transition.Identity);
        Assert.Equal(TimeSpan.FromSeconds(7), transition.SourceTime);
        Assert.Equal(new DetectionAttemptIdentity("algorithm", "config", "epoch", 8), transition.AttemptId);
        Assert.Equal(transition.AttemptId, confirmed.Result.State.AttemptId);
        Assert.Equal((uint)1, confirmed.Result.State.TransitionCount);
        Assert.Null(confirmed.Result.State.RunwayContact);
        Assert.Null(confirmed.Result.State.Candidate);
        Assert.Empty(confirmed.Result.Cues);
        Assert.Equal(new PhaseCueLatches(false, false, false, false, false), confirmed.Result.State.CueLatches);
        Assert.Contains(TelemetryQualityIssue.MissingAltitude, confirmed.Result.QualityIssues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepartureGroundRollHasNoSpeedCapOrLifetimeTimeout(bool missingMeasurements)
    {
        var context = Context(Configuration());
        var cursor = context.Start();
        PhaseDetectionResult? last = null;
        for (ulong sequence = 1; sequence <= 12; sequence++)
        {
            var time = sequence == 1 ? 0 : 3 + (sequence - 2) * 10;
            var step = context.Advance(cursor, DepartureSample(sequence, time, onGround: true,
                speed: missingMeasurements ? null : 100, track: missingMeasurements ? null : 180,
                climb: missingMeasurements ? null : -2));
            Assert.Equal(DetectionCycleOutcome.Accepted, step.Result.Outcome);
            Assert.Equal(TimeSpan.Zero, step.Result.State.RunwayContact!.FirstSourceTime);
            Assert.Equal((uint)sequence, step.Result.State.RunwayContact.SampleCount);
            Assert.Null(step.Result.State.Candidate);
            Assert.Null(step.Result.State.AttemptId);
            Assert.Empty(step.Result.Transitions);
            cursor = step.NextCursor;
            last = step.Result;
        }
        var lift = context.Advance(cursor, DepartureSample(13, last!.Snapshot.SourceTime.TotalSeconds + 1));
        Assert.Equal(PatternPhase.Departure, lift.Result.State.Candidate?.Phase);
    }

    [Fact]
    public void DepartureContactGapRestartsOnlyBeforeGateQualification()
    {
        var context = Context(Configuration());
        var first = context.Advance(context.Start(), DepartureSample(1, 0, onGround: true));
        var equal = context.Advance(first.NextCursor, DepartureSample(2, 4, onGround: true));
        Assert.Equal((uint)2, equal.Result.State.RunwayContact!.SampleCount);
        var excess = context.Advance(first.NextCursor,
            Sample(2, TimeSpan.FromSeconds(4) + TimeSpan.FromTicks(1), Geographic(new(500, 0)), onGround: true));
        Assert.Equal((uint)1, excess.Result.State.RunwayContact!.SampleCount);
        Assert.Equal(excess.Result.Snapshot.SourceTime, excess.Result.State.RunwayContact.FirstSourceTime);
        var gate = DepartureGate(context);
        var maximumGap = context.Advance(gate.NextCursor, DepartureSample(3, 13, onGround: true));
        Assert.Equal(DetectionCycleOutcome.Accepted, maximumGap.Result.Outcome);
        Assert.Equal(TimeSpan.Zero, maximumGap.Result.State.RunwayContact!.FirstSourceTime);
        var beyond = context.Advance(gate.NextCursor,
            Sample(3, TimeSpan.FromSeconds(13) + TimeSpan.FromTicks(1), Geographic(new(500, 0)), onGround: true));
        Assert.Equal(DetectionCycleOutcome.Interrupted, beyond.Result.Outcome);
        Assert.Null(beyond.Result.State.RunwayContact);
    }

    [Theory]
    [InlineData(0, -1, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 0.001, true)]
    [InlineData(1, 0.999, false)]
    [InlineData(1, 1, true)]
    public void DepartureRequiresPositiveClimbAndConfiguredMinimum(double minimum, double climb, bool eligible)
    {
        var context = Context(Configuration(minimumClimb: minimum));
        var gate = DepartureGate(context);
        var lift = context.Advance(gate.NextCursor, DepartureSample(3, 4, climb: climb));
        Assert.Equal(eligible, lift.Result.State.Candidate?.Phase == PatternPhase.Departure);
        var next = context.Advance(lift.NextCursor, DepartureSample(4, 8, climb: climb));
        Assert.Equal(eligible ? PatternPhase.Departure : PatternPhase.Unknown, next.Result.State.ConfirmedPhase);
    }

    [Theory]
    [InlineData(5, 19.999, false)]
    [InlineData(5, 20, true)]
    [InlineData(30, 25, false)]
    [InlineData(30, 30, true)]
    public void DepartureSpeedRequiresBothPolicyMinimumAndUsableDirection(double minimumUsable, double speed, bool eligible)
    {
        var context = Context(Configuration(), minimumUsable);
        var gate = DepartureGate(context);
        var lift = context.Advance(gate.NextCursor, DepartureSample(3, 4, speed: speed));
        Assert.Equal(eligible, lift.Result.State.Candidate?.Phase == PatternPhase.Departure);
    }

    [Theory]
    [InlineData("speed")]
    [InlineData("track")]
    [InlineData("climb")]
    [InlineData("ground")]
    [InlineData("zero-climb")]
    [InlineData("exit-track")]
    [InlineData("reverse-track")]
    [InlineData("outside")]
    [InlineData("overlap")]
    public void DepartureFailedAirborneEvidenceBurnsLaunchAuthorization(string failure)
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        foreach (var pending in new[] { false, true })
        {
            var prior = pending ? context.Advance(gate.NextCursor, DepartureSample(3, 4)) : gate;
            var sequence = prior.Result.Snapshot.SampleSequence + 1;
            var failed = context.Advance(prior.NextCursor, DepartureSample(sequence, 5,
                speed: failure == "speed" ? null : 25,
                track: failure == "track" ? null : failure == "exit-track" ? 20 : failure == "reverse-track" ? 180 : 0,
                climb: failure == "climb" ? null : failure == "zero-climb" ? 0 : 2,
                onGround: failure == "ground" ? null : false,
                point: failure == "outside" ? new(500, 100) : failure == "overlap" ? new(0, 0) : new(500, 0)));
            Assert.Equal(DetectionCycleOutcome.Accepted, failed.Result.Outcome);
            Assert.Null(failed.Result.State.RunwayContact);
            Assert.Null(failed.Result.State.Candidate);
            var later = context.Advance(failed.NextCursor, DepartureSample(sequence + 1, 9));
            Assert.Equal(PatternPhase.Unknown, later.Result.State.ConfirmedPhase);
            Assert.Null(later.Result.State.Candidate);
            Assert.Empty(later.Result.Transitions);
        }
    }

    [Fact]
    public void DepartureAirborneOverflightAndIncompleteContactCannotCreateGate()
    {
        var context = Context(Configuration());
        var overflight = context.Advance(context.Start(), DepartureSample(1, 0));
        var later = context.Advance(overflight.NextCursor, DepartureSample(2, 4));
        Assert.Null(later.Result.State.RunwayContact);
        Assert.Null(later.Result.State.Candidate);
        Assert.Equal(PatternPhase.Unknown, later.Result.State.ConfirmedPhase);
        var singleGround = context.Advance(context.Start(), DepartureSample(1, 0, onGround: true));
        var lift = context.Advance(singleGround.NextCursor, DepartureSample(2, 1));
        var after = context.Advance(lift.NextCursor, DepartureSample(3, 5));
        Assert.Null(after.Result.State.RunwayContact);
        Assert.Null(after.Result.State.Candidate);
        Assert.Empty(after.Result.Transitions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepartureLossOfContactMembershipOrGroundStatusRequiresFreshPersistence(bool missingGround)
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        // Cross-track 40 is inside the wider departure corridor, but outside physical contact width 25.
        var lost = context.Advance(gate.NextCursor, DepartureSample(3, 4,
            onGround: missingGround ? null : true, point: missingGround ? new(500, 0) : new(500, 40)));
        Assert.True(GeometryMovementEvidence.Evaluate(context.Configuration, lost.Result.Snapshot, 5)
            .DepartureUpwind.InsideEnterBounds);
        Assert.Null(lost.Result.State.RunwayContact);
        var fresh = context.Advance(lost.NextCursor, DepartureSample(4, 5, onGround: true));
        Assert.Equal((uint)1, fresh.Result.State.RunwayContact!.SampleCount);
        var lift = context.Advance(fresh.NextCursor, DepartureSample(5, 6));
        Assert.Null(lift.Result.State.Candidate);
    }

    [Fact]
    public void DepartureReturnToGroundCannotReusePreLiftoffContact()
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        var lift = context.Advance(gate.NextCursor, DepartureSample(3, 4));
        var ground = context.Advance(lift.NextCursor, DepartureSample(4, 5, onGround: true));
        Assert.Null(ground.Result.State.Candidate);
        Assert.Equal(TimeSpan.FromSeconds(5), ground.Result.State.RunwayContact!.FirstSourceTime);
        Assert.Equal((uint)1, ground.Result.State.RunwayContact.SampleCount);
        var earlyLift = context.Advance(ground.NextCursor, DepartureSample(5, 6));
        Assert.Null(earlyLift.Result.State.Candidate);
        var requalified = context.Advance(ground.NextCursor, DepartureSample(5, 8, onGround: true));
        var validLift = context.Advance(requalified.NextCursor, DepartureSample(6, 9));
        Assert.Equal(PatternPhase.Departure, validLift.Result.State.Candidate?.Phase);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left, false)]
    [InlineData(TrafficPatternSide.Right, false)]
    [InlineData(TrafficPatternSide.Left, true)]
    [InlineData(TrafficPatternSide.Right, true)]
    public void DepartureUsesThresholdAxisAndAllowsLiftoffInUpwind(TrafficPatternSide side, bool eastbound)
    {
        var context = Context(Configuration(side, eastbound: eastbound));
        var groundPosition = eastbound ? new GeographicCoordinate(0, 0.005) : new(0.005, 0);
        var upwindPosition = eastbound ? new GeographicCoordinate(0, 0.012) : new(0.012, 0);
        var first = context.Advance(context.Start(), Sample(1, TimeSpan.Zero, groundPosition, onGround: true));
        var gate = context.Advance(first.NextCursor, Sample(2, TimeSpan.FromSeconds(3), groundPosition, onGround: true));
        var sample = Sample(3, TimeSpan.FromSeconds(4), upwindPosition, track: eastbound ? 90 : 0, climb: 2);
        var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
        Assert.False(evidence.InsideRunwayContactEnvelope);
        Assert.True(evidence.DepartureUpwind.InsideEnterBounds);
        var lift = context.Advance(gate.NextCursor, sample);
        var confirmed = context.Advance(lift.NextCursor,
            Sample(4, TimeSpan.FromSeconds(8), upwindPosition, track: eastbound ? 90 : 0, climb: 2));
        Assert.Equal(PatternPhase.Departure, confirmed.Result.State.ConfirmedPhase);
    }

    [Fact]
    public void DepartureCandidateGapRestartsPersistenceWithoutCreatingNewContactProof()
    {
        var context = Context(Configuration(departurePersistence: TimeSpan.FromSeconds(8)));
        var gate = DepartureGate(context);
        var lift = context.Advance(gate.NextCursor, DepartureSample(3, 4));
        var exact = context.Advance(lift.NextCursor, DepartureSample(4, 8));
        Assert.Equal((uint)2, exact.Result.State.Candidate!.SampleCount);
        var beyond = context.Advance(lift.NextCursor,
            Sample(4, TimeSpan.FromSeconds(8) + TimeSpan.FromTicks(1), Geographic(new(500, 0)), climb: 2));
        Assert.Equal((uint)1, beyond.Result.State.Candidate!.SampleCount);
        Assert.Equal(beyond.Result.Snapshot.SourceTime, beyond.Result.State.Candidate.FirstSourceTime);
        Assert.Equal(gate.Result.State.RunwayContact, beyond.Result.State.RunwayContact);
        var confirmed = context.Advance(exact.NextCursor, DepartureSample(5, 12));
        Assert.Equal(PatternPhase.Departure, confirmed.Result.State.ConfirmedPhase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepartureRejectedSamplesPreserveGroundAndAirborneEvidence(bool airborne)
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        var prior = airborne ? context.Advance(gate.NextCursor, DepartureSample(3, 4)) : gate;
        var sequence = prior.Result.Snapshot.SampleSequence;
        var time = prior.Result.Snapshot.SourceTime.TotalSeconds;
        var cases = new[]
        {
            (DepartureSample(sequence, time - 1, epoch: "other"), SampleRejectionReason.EpochMismatch),
            (DepartureSample(sequence, time - 1, positionAvailable: false), SampleRejectionReason.DuplicateSequence),
            (DepartureSample(sequence - 1, time - 1), SampleRejectionReason.ReversedSequence),
            (DepartureSample(sequence + 1, time - 1), SampleRejectionReason.ReversedSourceTime),
            (DepartureSample(sequence + 1, time + 1, point: new(100_000, 0)), SampleRejectionReason.ImplausibleRelocation),
            (DepartureSample(sequence + 1, time, point: new(501, 0)), SampleRejectionReason.ImplausibleRelocation)
        };
        foreach (var (sample, reason) in cases)
        {
            var rejected = context.Advance(prior.NextCursor, sample);
            Assert.Equal(DetectionCycleOutcome.Rejected, rejected.Result.Outcome);
            Assert.Equal(reason, rejected.Result.RejectionReason);
            Assert.Equal(prior.Result.State.RunwayContact, rejected.Result.State.RunwayContact);
            Assert.Equal(prior.Result.State.Candidate, rejected.Result.State.Candidate);
            Assert.Equal(prior.Result.State.LastSampleSequence, rejected.Result.State.LastSampleSequence);
            Assert.Equal(prior.Result.State.LastSourceTime, rejected.Result.State.LastSourceTime);
            Assert.Equal(prior.Result.State.LastAcceptedPosition, rejected.Result.State.LastAcceptedPosition);
            Assert.Equal(prior.Result.State.PositionContinuity, rejected.Result.State.PositionContinuity);
            Assert.Equal(prior.Result.State.AttemptId, rejected.Result.State.AttemptId);
            Assert.Equal(prior.Result.State.TransitionCount, rejected.Result.State.TransitionCount);
            Assert.Equal(prior.Result.State.CueLatches, rejected.Result.State.CueLatches);
            Assert.Equal((uint)1, rejected.Result.State.RejectedSampleCount);
            Assert.Empty(rejected.Result.QualityIssues);
            Assert.Empty(rejected.Result.Transitions);
            var resumed = context.Advance(rejected.NextCursor, DepartureSample(sequence + 1, time + 1));
            Assert.Equal(PatternPhase.Departure, resumed.Result.State.Candidate?.Phase);
        }
    }

    [Theory]
    [InlineData(false, "position")]
    [InlineData(true, "position")]
    [InlineData(false, "gap")]
    [InlineData(true, "gap")]
    [InlineData(false, "both")]
    [InlineData(true, "both")]
    public void DepartureInterruptionsClearLaunchProofAndRecoveryStartsFresh(bool airborne, string failure)
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        var prior = airborne ? context.Advance(gate.NextCursor, DepartureSample(3, 4)) : gate;
        var sequence = prior.Result.Snapshot.SampleSequence + 1;
        var time = prior.Result.Snapshot.SourceTime.TotalSeconds + (failure == "position" ? 1 : 11);
        var interrupted = context.Advance(prior.NextCursor, DepartureSample(sequence, time,
            positionAvailable: failure == "gap"));
        Assert.Equal(DetectionCycleOutcome.Interrupted, interrupted.Result.Outcome);
        Assert.Equal(failure == "position" ? SampleInterruptionReason.PositionContinuityLost :
            SampleInterruptionReason.SampleContinuityGap, interrupted.Result.InterruptionReason);
        Assert.Null(interrupted.Result.State.RunwayContact);
        Assert.Null(interrupted.Result.State.Candidate);
        Assert.Equal(sequence, interrupted.Result.State.LastSampleSequence);
        Assert.Equal(prior.Result.State.LastAcceptedPosition, interrupted.Result.State.LastAcceptedPosition);
        Assert.Equal(PositionContinuity.Interrupted, interrupted.Result.State.PositionContinuity);
        Assert.Empty(interrupted.Result.Transitions);
        var air = context.Advance(interrupted.NextCursor, DepartureSample(sequence + 1, time + 1));
        Assert.Null(air.Result.State.Candidate);
        var fresh = context.Advance(interrupted.NextCursor,
            DepartureSample(sequence + 1, time + 1, onGround: true, point: new(900, 0)));
        Assert.Equal(DetectionCycleOutcome.Accepted, fresh.Result.Outcome);
        Assert.Equal((uint)1, fresh.Result.State.RunwayContact!.SampleCount);
    }

    [Fact]
    public void DepartureAmbiguousRegionsCannotBeIgnoredEvenWhenUnsupported()
    {
        var context = Context(Configuration(enterTolerance: 100, exitTolerance: 110));
        var gate = DepartureGate(context);
        var geometry = context.Configuration.GeometryAuthority.Geometry!;
        var sample = DepartureSample(3, 4, point: geometry.Crosswind.Axis.Start);
        var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
        Assert.True(evidence.DepartureUpwind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.DepartureUpwind.Direction.Status);
        Assert.True(evidence.Crosswind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.Crosswind.Direction.Status);
        var held = context.Advance(gate.NextCursor, sample);
        Assert.Null(held.Result.State.RunwayContact);
        Assert.Null(held.Result.State.Candidate);
        Assert.Equal(PatternPhase.Unknown, held.Result.State.ConfirmedPhase);
    }

    [Fact]
    public void DepartureLaunchLossStillAllowsExistingInboundEntry()
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        var entry = At(context, gate.NextCursor, 3, TimeSpan.FromSeconds(4), PatternPhase.Entry);
        Assert.Null(entry.Result.State.RunwayContact);
        Assert.Equal(PatternPhase.Entry, entry.Result.State.Candidate?.Phase);
        var confirmed = At(context, entry.NextCursor, 4, TimeSpan.FromSeconds(6), PatternPhase.Entry);
        Assert.Equal(PatternPhase.Entry, confirmed.Result.State.ConfirmedPhase);
    }

    [Fact]
    public void DepartureRemainsTerminalInThisSliceAndDoesNotArmAnotherGate()
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        var lift = context.Advance(gate.NextCursor, DepartureSample(3, 4));
        var departure = context.Advance(lift.NextCursor, DepartureSample(4, 8));
        var cursor = departure.NextCursor;
        var geometry = context.Configuration.GeometryAuthority.Geometry!;
        var samples = new[]
        {
            DepartureSample(5, 9, point: Midpoint(geometry.Crosswind.Axis), track: 270),
            DepartureSample(6, 11, point: Midpoint(geometry.Crosswind.Axis), track: 270),
            DepartureSample(7, 12, point: Midpoint(geometry.Downwind.Axis), track: 180),
            DepartureSample(8, 14, point: Midpoint(geometry.Downwind.Axis), track: 180),
            DepartureSample(9, 15, onGround: true),
            DepartureSample(10, 18, onGround: true)
        };
        foreach (var sample in samples)
        {
            var step = context.Advance(cursor, sample);
            Assert.Equal(PatternPhase.Departure, step.Result.State.ConfirmedPhase);
            Assert.Equal(departure.Result.State.AttemptId, step.Result.State.AttemptId);
            Assert.Equal((uint)1, step.Result.State.TransitionCount);
            Assert.Null(step.Result.State.Candidate);
            Assert.Null(step.Result.State.RunwayContact);
            Assert.Empty(step.Result.Transitions);
            cursor = step.NextCursor;
        }
    }

    [Fact]
    public void DepartureReplayIsDeterministicAndContextResetCannotTransferGate()
    {
        var config = Configuration();
        var context = Context(config);
        var samples = new[]
        {
            DepartureSample(1, 0, onGround: true), DepartureSample(2, 3, onGround: true),
            DepartureSample(3, 4), DepartureSample(3, 4), DepartureSample(4, 8),
            DepartureSample(5, 9, positionAvailable: false), DepartureSample(6, 10)
        };
        List<PhaseDetectionResult> Run(PhaseReplayContext owner)
        {
            var cursor = owner.Start();
            var results = new List<PhaseDetectionResult>();
            foreach (var sample in samples)
            {
                var step = owner.Advance(cursor, sample);
                results.Add(step.Result);
                cursor = step.NextCursor;
            }
            return results;
        }
        var a = Run(context);
        foreach (var b in new[] { Run(context), Run(Context(config)) })
            for (var i = 0; i < a.Count; i++)
            {
                Assert.Equal(a[i].State, b[i].State);
                Assert.Equal(a[i].Outcome, b[i].Outcome);
                Assert.Equal(a[i].RejectionReason, b[i].RejectionReason);
                Assert.Equal(a[i].InterruptionReason, b[i].InterruptionReason);
                Assert.Equal(a[i].QualityIssues, b[i].QualityIssues);
                Assert.Equal(a[i].Transitions, b[i].Transitions);
                Assert.Equal(a[i].Cues, b[i].Cues);
            }
        Assert.Equal(PatternPhase.Departure, a[4].State.ConfirmedPhase);
        var gate = DepartureGate(context);
        var reset = Context(config);
        Assert.Throws<ArgumentException>(() => reset.Advance(gate.NextCursor, DepartureSample(3, 4)));
        var fresh = reset.Advance(reset.Start(), DepartureSample(3, 4));
        Assert.Null(fresh.Result.State.RunwayContact);
        Assert.Null(fresh.Result.State.Candidate);
        var branchA = context.Advance(gate.NextCursor, DepartureSample(3, 4));
        var branchB = context.Advance(gate.NextCursor, DepartureSample(3, 4));
        Assert.Equal(branchA.Result.State, branchB.Result.State);
    }
}
