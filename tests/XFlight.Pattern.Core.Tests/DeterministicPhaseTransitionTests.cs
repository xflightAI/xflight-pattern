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
            PatternPhase.Crosswind => (Geographic(Midpoint(geometry.Crosswind.Axis)), side < 0 ? 270 : 90),
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

    [Theory]
    [InlineData("candidate-gap")]
    [InlineData("above-candidate-gap")]
    [InlineData("sample-gap")]
    public void FirstQualifyingLiftoffAfterEstablishedGateRespectsSampleGap(string boundary)
    {
        var context = Context(Configuration());
        var policy = context.Configuration.Policy;
        var first = context.Advance(context.Start(),
            Sample(1, TimeSpan.Zero, Geographic(new(500, 0)), onGround: true));
        var gate = context.Advance(first.NextCursor,
            Sample(2, policy.GroundContactPersistence, Geographic(new(500, 0)), onGround: true));
        Assert.Equal(DetectionCycleOutcome.Accepted, gate.Result.Outcome);
        Assert.Equal(policy.GroundContactPersistence,
            gate.Result.State.RunwayContact!.LastSourceTime - gate.Result.State.RunwayContact.FirstSourceTime);

        var gap = boundary switch
        {
            "candidate-gap" => policy.MaximumCandidateGap,
            "above-candidate-gap" => policy.MaximumCandidateGap + TimeSpan.FromTicks(1),
            "sample-gap" => policy.MaximumSampleGap,
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };
        var liftoffTime = gate.Result.Snapshot.SourceTime + gap;
        var lift = context.Advance(gate.NextCursor,
            Sample(3, liftoffTime, Geographic(new(500, 0)),
                speed: policy.DepartureMinimumSpeedMetresPerSecond,
                track: 0, climb: policy.DepartureMinimumClimbMetresPerSecond));
        Assert.Equal(DetectionCycleOutcome.Accepted, lift.Result.Outcome);
        Assert.Equal(PatternPhase.Unknown, lift.Result.State.ConfirmedPhase);
        Assert.Equal(PatternPhase.Departure, lift.Result.State.Candidate?.Phase);
        Assert.Equal(liftoffTime, lift.Result.State.Candidate!.FirstSourceTime);
        Assert.Equal((uint)1, lift.Result.State.Candidate.SampleCount);
        Assert.Equal(gate.Result.State.RunwayContact, lift.Result.State.RunwayContact);
        Assert.Null(lift.Result.State.AttemptId);
        Assert.Empty(lift.Result.Transitions);
        Assert.Empty(lift.Result.Cues);

        var confirmation = context.Advance(lift.NextCursor,
            Sample(4, liftoffTime + policy.DeparturePersistence, Geographic(new(500, 0)),
                speed: policy.DepartureMinimumSpeedMetresPerSecond,
                track: 0, climb: policy.DepartureMinimumClimbMetresPerSecond));
        Assert.Equal(DetectionCycleOutcome.Accepted, confirmation.Result.Outcome);
        Assert.Equal(PatternPhase.Departure, confirmation.Result.State.ConfirmedPhase);
        var transition = Assert.Single(confirmation.Result.Transitions);
        Assert.Equal(PatternPhase.Unknown, transition.From);
        Assert.Equal(PatternPhase.Departure, transition.To);
    }

    [Fact]
    public void AcceleratingMovingRunwayRollCannotConfirmDepartureBeforeLiftoff()
    {
        var context = Context(Configuration());
        var policy = context.Configuration.Policy;
        var cursor = context.Start();
        var along = context.Configuration.RunwayContactEnvelope.MinimumAlongTrackMetres +
            context.Configuration.GeometryAuthority.RunwayReference.RunwayLength.Metres / 10;
        var priorSpeed = 0d;
        var cadence = TimeSpan.FromSeconds(1);
        PhaseDetectionResult? lastGround = null;

        for (var index = 0; index < 8; index++)
        {
            var speed = policy.DepartureMinimumSpeedMetresPerSecond * index / 4d;
            if (index > 0) along += (priorSpeed + speed) / 2 * cadence.TotalSeconds;
            var step = context.Advance(cursor,
                Sample((ulong)index + 1, cadence * index, Geographic(new(along, 0)),
                    speed: speed, track: 0, onGround: true,
                    climb: policy.DepartureMinimumClimbMetresPerSecond));
            Assert.Equal(DetectionCycleOutcome.Accepted, step.Result.Outcome);
            Assert.True(GeometryMovementEvidence.Evaluate(context.Configuration, step.Result.Snapshot,
                context.MinimumUsableGroundSpeedMetresPerSecond).InsideRunwayContactEnvelope);
            Assert.Equal(PatternPhase.Unknown, step.Result.State.ConfirmedPhase);
            Assert.Null(step.Result.State.Candidate);
            Assert.Null(step.Result.State.AttemptId);
            Assert.Empty(step.Result.Transitions);
            Assert.Empty(step.Result.Cues);
            cursor = step.NextCursor;
            lastGround = step.Result;
            priorSpeed = speed;
        }

        Assert.True(priorSpeed > policy.DepartureMinimumSpeedMetresPerSecond);
        Assert.True(priorSpeed > policy.GroundContactMaximumSpeedMetresPerSecond);
        Assert.True(lastGround!.State.RunwayContact!.LastSourceTime -
            lastGround.State.RunwayContact.FirstSourceTime >= policy.GroundContactPersistence);
        var lift = context.Advance(cursor,
            Sample(lastGround.Snapshot.SampleSequence + 1, lastGround.Snapshot.SourceTime + cadence,
                Geographic(new(along + priorSpeed * cadence.TotalSeconds, 0)),
                speed: priorSpeed, track: 0, onGround: false,
                climb: policy.DepartureMinimumClimbMetresPerSecond));
        Assert.Equal(DetectionCycleOutcome.Accepted, lift.Result.Outcome);
        Assert.Equal(PatternPhase.Unknown, lift.Result.State.ConfirmedPhase);
        Assert.Equal(PatternPhase.Departure, lift.Result.State.Candidate?.Phase);
        Assert.Empty(lift.Result.Transitions);
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
    public void CrosswindUnauthorizedTransitionsDoNotArmAnotherGate()
    {
        var context = Context(Configuration());
        var gate = DepartureGate(context);
        var lift = context.Advance(gate.NextCursor, DepartureSample(3, 4));
        var departure = context.Advance(lift.NextCursor, DepartureSample(4, 8));
        var candidate = At(context, departure.NextCursor, 5, TimeSpan.FromSeconds(9), PatternPhase.Crosswind);
        var crosswind = At(context, candidate.NextCursor, 6, TimeSpan.FromSeconds(11), PatternPhase.Crosswind);
        Assert.Equal(PatternPhase.Crosswind, crosswind.Result.State.ConfirmedPhase);
        var cursor = crosswind.NextCursor;
        var samples = new[]
        {
            DepartureSample(7, 12, point: new(-800, -400), track: 90),
            DepartureSample(8, 14, point: new(-300, 0)),
            DepartureSample(9, 15, onGround: true),
            DepartureSample(10, 18, onGround: true),
            DepartureSample(11, 19),
            DepartureSample(12, 23)
        };
        foreach (var sample in samples)
        {
            var step = context.Advance(cursor, sample);
            Assert.Equal(PatternPhase.Crosswind, step.Result.State.ConfirmedPhase);
            Assert.Equal(departure.Result.State.AttemptId, step.Result.State.AttemptId);
            Assert.Equal((uint)2, step.Result.State.TransitionCount);
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

    private static (PhaseDetectionResult Result, PhaseReplayCursor NextCursor) ConfirmDeparture(PhaseReplayContext context)
    {
        var gate = DepartureGate(context);
        var lift = context.Advance(gate.NextCursor, DepartureSample(3, 4));
        var confirmed = context.Advance(lift.NextCursor, DepartureSample(4, 8));
        Assert.Equal(PatternPhase.Departure, confirmed.Result.State.ConfirmedPhase);
        return confirmed;
    }

    private static TelemetrySnapshot CrosswindSample(PhaseReplayContext context, ulong sequence, double seconds,
        RunwayCoordinate? point = null, bool positionAvailable = true) =>
        Sample(sequence, TimeSpan.FromSeconds(seconds), positionAvailable
            ? point is { } coordinate ? Geographic(coordinate) : Point(context.Configuration, PatternPhase.Crosswind).Position
            : null, speed: 5, track: Point(context.Configuration, PatternPhase.Crosswind).Track);

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void CrosswindPersistenceIsInclusiveAndPreservesAttemptAndEventIdentity(TrafficPatternSide side)
    {
        var context = Context(Configuration(side));
        var departure = ConfirmDeparture(context);
        Assert.Null(departure.Result.State.Candidate); // Confirmation cannot also begin the successor.
        var first = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        Assert.Equal(PatternPhase.Departure, first.Result.State.ConfirmedPhase);
        Assert.Equal(PatternPhase.Crosswind, first.Result.State.Candidate!.Phase);
        Assert.Empty(first.Result.Transitions);
        var sameTime = context.Advance(first.NextCursor, CrosswindSample(context, 6, 9));
        Assert.Equal((uint)2, sameTime.Result.State.Candidate!.SampleCount);
        Assert.Empty(sameTime.Result.Transitions);
        var point = Point(context.Configuration, PatternPhase.Crosswind);
        var before = context.Advance(sameTime.NextCursor,
            Sample(7, TimeSpan.FromSeconds(11) - TimeSpan.FromTicks(1), point.Position, speed: 5, track: point.Track));
        Assert.Equal(PatternPhase.Departure, before.Result.State.ConfirmedPhase);
        Assert.Empty(before.Result.Transitions);
        var exact = context.Advance(before.NextCursor, CrosswindSample(context, 8, 11));
        Assert.Equal(PatternPhase.Crosswind, exact.Result.State.ConfirmedPhase);
        Assert.Null(exact.Result.State.Candidate);
        Assert.Null(exact.Result.State.RunwayContact);
        Assert.Equal(departure.Result.State.AttemptId, exact.Result.State.AttemptId);
        Assert.Equal((ulong)4, exact.Result.State.AttemptId!.StartSampleSequence);
        Assert.Equal((uint)2, exact.Result.State.TransitionCount);
        var transition = Assert.Single(exact.Result.Transitions);
        Assert.Equal(PatternPhase.Departure, transition.From);
        Assert.Equal(PatternPhase.Crosswind, transition.To);
        Assert.Equal(new DetectionEventIdentity("algorithm", "config", "epoch", 8, 0), transition.Identity);
        Assert.Equal(TimeSpan.FromSeconds(11), transition.SourceTime);
        Assert.Equal(departure.Result.State.AttemptId, transition.AttemptId);
        Assert.Empty(exact.Result.Cues);
        Assert.Equal(departure.Result.State.CueLatches, exact.Result.State.CueLatches);
        Assert.Contains(TelemetryQualityIssue.MissingAltitude, exact.Result.QualityIssues);
        Assert.Contains(TelemetryQualityIssue.MissingVerticalSpeed, exact.Result.QualityIssues);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-2d)]
    public void CrosswindDoesNotReuseDepartureSpeedOrClimbRequirements(double? climb)
    {
        var context = Context(Configuration());
        var departure = ConfirmDeparture(context);
        var point = Point(context.Configuration, PatternPhase.Crosswind);
        var first = context.Advance(departure.NextCursor,
            Sample(5, TimeSpan.FromSeconds(9), point.Position, speed: 5, track: point.Track, climb: climb));
        var confirmed = context.Advance(first.NextCursor,
            Sample(6, TimeSpan.FromSeconds(11), point.Position, speed: 5, track: point.Track, climb: climb));
        Assert.Equal(PatternPhase.Crosswind, confirmed.Result.State.ConfirmedPhase);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void CrosswindTargetEnterEvidenceCannotOverrideDepartureExitHysteresis(TrafficPatternSide side)
    {
        var context = Context(Configuration(side, enterTolerance: 40, exitTolerance: 60));
        var departure = ConfirmDeparture(context);
        var pending = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var geometry = context.Configuration.GeometryAuthority.Geometry!;
        var sign = side == TrafficPatternSide.Left ? -1 : 1;
        var join = new RunwayCoordinate(geometry.Crosswind.Axis.Start.AlongTrackMetres, sign * 65);
        var track = side == TrafficPatternSide.Left ? 305 : 55;
        var sample = Sample(6, TimeSpan.FromSeconds(10), Geographic(join), speed: 5, track: track);
        var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
        Assert.False(evidence.DepartureUpwind.InsideEnterBounds);
        Assert.True(evidence.DepartureUpwind.InsideExitBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinExitTolerance, evidence.DepartureUpwind.Direction.Status);
        Assert.True(evidence.Crosswind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.Crosswind.Direction.Status);
        var held = context.Advance(pending.NextCursor, sample);
        Assert.Equal(PatternPhase.Departure, held.Result.State.ConfirmedPhase);
        Assert.Null(held.Result.State.Candidate);
        Assert.Empty(held.Result.Transitions);
        // Leaving current exit bounds permits fresh target evidence; no lost interval is reused.
        var outside = new RunwayCoordinate(join.AlongTrackMetres, sign * 71);
        var fresh = context.Advance(held.NextCursor,
            Sample(7, TimeSpan.FromSeconds(11), Geographic(outside), speed: 5, track: track));
        Assert.Equal(TimeSpan.FromSeconds(11), fresh.Result.State.Candidate!.FirstSourceTime);
        var confirmed = context.Advance(fresh.NextCursor,
            Sample(8, TimeSpan.FromSeconds(13), Geographic(outside), speed: 5, track: track));
        Assert.Equal(PatternPhase.Crosswind, confirmed.Result.State.ConfirmedPhase);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void CrosswindOverlapWithUnsupportedDownwindResetsCandidate(TrafficPatternSide side)
    {
        var context = Context(Configuration(side, enterTolerance: 50, exitTolerance: 60));
        var departure = ConfirmDeparture(context);
        var pending = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var geometry = context.Configuration.GeometryAuthority.Geometry!;
        var sample = Sample(6, TimeSpan.FromSeconds(11), Geographic(geometry.Crosswind.Axis.End),
            speed: 5, track: side == TrafficPatternSide.Left ? 225 : 135);
        var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
        Assert.False(evidence.DepartureUpwind.InsideExitBounds);
        Assert.True(evidence.Crosswind.InsideEnterBounds);
        Assert.True(evidence.Downwind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.Crosswind.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.Downwind.Direction.Status);
        var held = context.Advance(pending.NextCursor, sample);
        Assert.Equal(PatternPhase.Departure, held.Result.State.ConfirmedPhase);
        Assert.Null(held.Result.State.Candidate);
        Assert.Empty(held.Result.Transitions);
    }

    [Theory]
    [InlineData("missing-speed")]
    [InlineData("missing-track")]
    [InlineData("missing-ground")]
    [InlineData("ground")]
    [InlineData("low-speed")]
    [InlineData("reverse-track")]
    [InlineData("runway-track")]
    [InlineData("exit-track")]
    [InlineData("wrong-side")]
    [InlineData("exit-bounds")]
    public void CrosswindInsufficientAcceptedEvidenceHoldsDepartureAndRestartsPersistence(string failure)
    {
        var context = Context(Configuration());
        var departure = ConfirmDeparture(context);
        var pending = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var geometry = context.Configuration.GeometryAuthority.Geometry!;
        var midpoint = Midpoint(geometry.Crosswind.Axis);
        var position = failure == "wrong-side" ? new RunwayCoordinate(midpoint.AlongTrackMetres, -midpoint.CrossTrackMetres)
            : failure == "exit-bounds" ? new RunwayCoordinate(midpoint.AlongTrackMetres + 65, midpoint.CrossTrackMetres)
            : midpoint;
        var track = failure == "missing-track" ? (double?)null : failure == "reverse-track" ? 90
            : failure == "runway-track" ? 0 : failure == "exit-track" ? 290 : 270;
        var sample = Sample(6, TimeSpan.FromSeconds(10), Geographic(position),
            speed: failure == "missing-speed" ? null : failure == "low-speed" ? 4.999 : 5,
            track: track, onGround: failure == "missing-ground" ? null : failure == "ground");
        if (failure == "exit-bounds")
        {
            var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
            Assert.False(evidence.Crosswind.InsideEnterBounds);
            Assert.True(evidence.Crosswind.InsideExitBounds);
        }
        var held = context.Advance(pending.NextCursor, sample);
        Assert.Equal(DetectionCycleOutcome.Accepted, held.Result.Outcome);
        Assert.Equal(PatternPhase.Departure, held.Result.State.ConfirmedPhase);
        Assert.Equal(departure.Result.State.AttemptId, held.Result.State.AttemptId);
        Assert.Null(held.Result.State.Candidate);
        Assert.Null(held.Result.State.RunwayContact);
        Assert.Empty(held.Result.Transitions);
        var fresh = context.Advance(held.NextCursor, CrosswindSample(context, 7, 11));
        Assert.Equal(TimeSpan.FromSeconds(11), fresh.Result.State.Candidate!.FirstSourceTime);
        Assert.Empty(fresh.Result.Transitions);
        var confirmed = context.Advance(fresh.NextCursor, CrosswindSample(context, 8, 13));
        Assert.Equal(PatternPhase.Crosswind, confirmed.Result.State.ConfirmedPhase);
    }

    [Fact]
    public void CrosswindCandidateGapAndSampleGapBoundariesRetainExistingSemantics()
    {
        var context = Context(Configuration(persistence: TimeSpan.FromSeconds(6)));
        var departure = ConfirmDeparture(context);
        var pending = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var exact = context.Advance(pending.NextCursor, CrosswindSample(context, 6, 13));
        Assert.Equal((uint)2, exact.Result.State.Candidate!.SampleCount);
        var point = Point(context.Configuration, PatternPhase.Crosswind);
        var beyond = context.Advance(pending.NextCursor,
            Sample(6, TimeSpan.FromSeconds(13) + TimeSpan.FromTicks(1), point.Position, speed: 5, track: point.Track));
        Assert.Equal((uint)1, beyond.Result.State.Candidate!.SampleCount);
        Assert.Equal(beyond.Result.Snapshot.SourceTime, beyond.Result.State.Candidate.FirstSourceTime);
        var confirmed = context.Advance(exact.NextCursor, CrosswindSample(context, 7, 15));
        Assert.Equal(PatternPhase.Crosswind, confirmed.Result.State.ConfirmedPhase);
        var maximum = context.Advance(pending.NextCursor, CrosswindSample(context, 6, 19));
        Assert.Equal(DetectionCycleOutcome.Accepted, maximum.Result.Outcome);
        Assert.Equal((uint)1, maximum.Result.State.Candidate!.SampleCount);
        var gap = context.Advance(pending.NextCursor,
            Sample(6, TimeSpan.FromSeconds(19) + TimeSpan.FromTicks(1), point.Position, speed: 5, track: point.Track));
        Assert.Equal(DetectionCycleOutcome.Interrupted, gap.Result.Outcome);
        Assert.Equal(PatternPhase.Departure, gap.Result.State.ConfirmedPhase);
        Assert.Null(gap.Result.State.Candidate);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("gap")]
    [InlineData("both")]
    public void CrosswindInterruptionsConsumeCursorAndResumeWithFreshCandidate(string failure)
    {
        var context = Context(Configuration());
        var departure = ConfirmDeparture(context);
        var pending = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var time = failure == "position" ? 10 : 20;
        var interrupted = context.Advance(pending.NextCursor,
            CrosswindSample(context, 6, time, positionAvailable: failure == "gap"));
        Assert.Equal(DetectionCycleOutcome.Interrupted, interrupted.Result.Outcome);
        Assert.Equal(failure == "position" ? SampleInterruptionReason.PositionContinuityLost :
            SampleInterruptionReason.SampleContinuityGap, interrupted.Result.InterruptionReason);
        Assert.Equal((ulong)6, interrupted.Result.State.LastSampleSequence);
        Assert.Equal(TimeSpan.FromSeconds(time), interrupted.Result.State.LastSourceTime);
        Assert.Equal(PatternPhase.Departure, interrupted.Result.State.ConfirmedPhase);
        Assert.Equal(departure.Result.State.AttemptId, interrupted.Result.State.AttemptId);
        Assert.Equal((uint)1, interrupted.Result.State.TransitionCount);
        Assert.Equal(pending.Result.State.LastAcceptedPosition, interrupted.Result.State.LastAcceptedPosition);
        Assert.Equal(PositionContinuity.Interrupted, interrupted.Result.State.PositionContinuity);
        Assert.Null(interrupted.Result.State.Candidate);
        Assert.Empty(interrupted.Result.Transitions);
        // Changed position at equal consumed time must not be compared across the interruption.
        var midpoint = Midpoint(context.Configuration.GeometryAuthority.Geometry!.Crosswind.Axis);
        var shifted = new RunwayCoordinate(midpoint.AlongTrackMetres + 20, midpoint.CrossTrackMetres);
        var fresh = context.Advance(interrupted.NextCursor, CrosswindSample(context, 7, time, point: shifted));
        Assert.Equal(DetectionCycleOutcome.Accepted, fresh.Result.Outcome);
        Assert.Equal((uint)1, fresh.Result.State.Candidate!.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(time), fresh.Result.State.Candidate.FirstSourceTime);
        Assert.Equal(PositionContinuity.Continuous, fresh.Result.State.PositionContinuity);
        var confirmed = context.Advance(fresh.NextCursor, CrosswindSample(context, 8, time + 2, point: shifted));
        Assert.Equal(PatternPhase.Crosswind, confirmed.Result.State.ConfirmedPhase);
    }

    [Fact]
    public void CrosswindRejectedSamplesPreservePendingEvidenceAndEventIdentity()
    {
        var context = Context(Configuration());
        var departure = ConfirmDeparture(context);
        var pending = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var point = Point(context.Configuration, PatternPhase.Crosswind);
        var cases = new[]
        {
            (Sample(5, TimeSpan.FromSeconds(8), null, epoch: "other"), SampleRejectionReason.EpochMismatch),
            (Sample(5, TimeSpan.FromSeconds(8), null), SampleRejectionReason.DuplicateSequence),
            (Sample(4, TimeSpan.FromSeconds(8), point.Position), SampleRejectionReason.ReversedSequence),
            (Sample(6, TimeSpan.FromSeconds(8), point.Position), SampleRejectionReason.ReversedSourceTime),
            (Sample(6, TimeSpan.FromSeconds(10), Geographic(new(100_000, 0))), SampleRejectionReason.ImplausibleRelocation),
            (CrosswindSample(context, 6, 9, point: new(
                context.Configuration.GeometryAuthority.Geometry!.Crosswind.Axis.Start.AlongTrackMetres + 1, -400)),
                SampleRejectionReason.ImplausibleRelocation)
        };
        foreach (var (sample, reason) in cases)
        {
            var rejected = context.Advance(pending.NextCursor, sample);
            Assert.Equal(DetectionCycleOutcome.Rejected, rejected.Result.Outcome);
            Assert.Equal(reason, rejected.Result.RejectionReason);
            var prior = pending.Result.State;
            Assert.Equal(new PhaseDetectionState(prior.AlgorithmId, prior.ConfigurationId, prior.StreamEpoch,
                prior.LastSampleSequence, prior.LastSourceTime, prior.ConfirmedPhase, prior.AttemptId, prior.Candidate,
                prior.TransitionCount, prior.RejectedSampleCount + 1, prior.CueLatches,
                prior.LastAcceptedPosition, prior.PositionContinuity), rejected.Result.State);
            Assert.Empty(rejected.Result.QualityIssues);
            Assert.Empty(rejected.Result.Transitions);
            Assert.Empty(rejected.Result.Cues);
            var resumed = context.Advance(rejected.NextCursor, CrosswindSample(context, 6, 11));
            Assert.Equal(PatternPhase.Crosswind, resumed.Result.State.ConfirmedPhase);
            Assert.Equal(new DetectionEventIdentity("algorithm", "config", "epoch", 6, 0),
                Assert.Single(resumed.Result.Transitions).Identity);
            Assert.Equal((uint)1, resumed.Result.State.RejectedSampleCount);
        }
    }

    [Fact]
    public void CrosswindCannotBeInitialSkippedOrEnteredFromInboundEntry()
    {
        var context = Context(Configuration());
        var initial = context.Advance(context.Start(), CrosswindSample(context, 1, 0));
        var initialAgain = context.Advance(initial.NextCursor, CrosswindSample(context, 2, 2));
        Assert.Equal(PatternPhase.Unknown, initialAgain.Result.State.ConfirmedPhase);
        Assert.Null(initialAgain.Result.State.Candidate);
        var departure = ConfirmDeparture(context);
        var downwind = At(context, departure.NextCursor, 5, TimeSpan.FromSeconds(9), PatternPhase.Downwind);
        var downwindAgain = At(context, downwind.NextCursor, 6, TimeSpan.FromSeconds(11), PatternPhase.Downwind);
        Assert.Equal(PatternPhase.Departure, downwindAgain.Result.State.ConfirmedPhase);
        Assert.Null(downwindAgain.Result.State.Candidate);
        Assert.Empty(downwindAgain.Result.Transitions);
        var entry = ConfirmThrough(context, PatternPhase.Entry);
        var unrelated = context.Advance(entry.NextCursor, CrosswindSample(context, 3, 3));
        var unrelatedAgain = context.Advance(unrelated.NextCursor, CrosswindSample(context, 4, 5));
        Assert.Equal(PatternPhase.Entry, unrelatedAgain.Result.State.ConfirmedPhase);
        Assert.Null(unrelatedAgain.Result.State.Candidate);
        Assert.Empty(unrelatedAgain.Result.Transitions);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void CrosswindReplayAndImmutableCursorBranchesAreDeterministic(TrafficPatternSide side)
    {
        var config = Configuration(side);
        var context = Context(config);
        var samples = new[]
        {
            DepartureSample(1, 0, onGround: true), DepartureSample(2, 3, onGround: true),
            DepartureSample(3, 4), DepartureSample(4, 8), CrosswindSample(context, 5, 9),
            CrosswindSample(context, 5, 9), CrosswindSample(context, 6, 10, positionAvailable: false),
            CrosswindSample(context, 7, 11), CrosswindSample(context, 8, 13), DepartureSample(9, 14)
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
        var first = Run(context);
        foreach (var again in new[] { Run(context), Run(Context(config)) })
            for (var i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].State, again[i].State);
                Assert.Equal(first[i].Outcome, again[i].Outcome);
                Assert.Equal(first[i].RejectionReason, again[i].RejectionReason);
                Assert.Equal(first[i].InterruptionReason, again[i].InterruptionReason);
                Assert.Equal(first[i].QualityIssues, again[i].QualityIssues);
                Assert.Equal(first[i].Transitions, again[i].Transitions);
                Assert.Equal(first[i].Cues, again[i].Cues);
            }
        Assert.Equal(PatternPhase.Crosswind, first[8].State.ConfirmedPhase);
        Assert.Equal((uint)2, first[8].State.TransitionCount);
        Assert.Equal(first[3].State.AttemptId, first[8].State.AttemptId);
        Assert.Equal(PatternPhase.Crosswind, first[9].State.ConfirmedPhase);
        var departure = ConfirmDeparture(context);
        var branchA = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var branchB = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        Assert.Equal(branchA.Result.State, branchB.Result.State);
    }

    private static (PhaseDetectionResult Result, PhaseReplayCursor NextCursor) ConfirmCrosswind(PhaseReplayContext context)
    {
        var departure = ConfirmDeparture(context);
        var step = context.Advance(departure.NextCursor, CrosswindSample(context, 5, 9));
        var elapsed = TimeSpan.Zero;
        ulong sequence = 5;
        while (elapsed < context.Configuration.Policy.CandidatePersistence)
        {
            var remaining = context.Configuration.Policy.CandidatePersistence - elapsed;
            elapsed += remaining < context.Configuration.Policy.MaximumCandidateGap
                ? remaining : context.Configuration.Policy.MaximumCandidateGap;
            step = At(context, step.NextCursor, ++sequence, TimeSpan.FromSeconds(9) + elapsed, PatternPhase.Crosswind);
        }
        Assert.Equal(PatternPhase.Crosswind, step.Result.State.ConfirmedPhase);
        return step;
    }

    private static TelemetrySnapshot DownwindSample(PhaseReplayContext context, ulong sequence, double seconds,
        RunwayCoordinate? point = null, bool positionAvailable = true) =>
        Sample(sequence, TimeSpan.FromSeconds(seconds), positionAvailable
            ? point is { } coordinate ? Geographic(coordinate) : Point(context.Configuration, PatternPhase.Downwind).Position
            : null, speed: 5, track: 180);

    [Theory]
    [InlineData(TrafficPatternSide.Left, false)]
    [InlineData(TrafficPatternSide.Right, false)]
    [InlineData(TrafficPatternSide.Left, true)]
    [InlineData(TrafficPatternSide.Right, true)]
    public void OutboundFullChainJoinsBaseAndFinalWithOneStableAttempt(TrafficPatternSide side, bool eastbound)
    {
        var context = Context(Configuration(side, eastbound: eastbound));
        var geometry = context.Configuration.GeometryAuthority.Geometry!;
        var sign = side == TrafficPatternSide.Left ? -1 : 1;
        GeographicCoordinate Position(RunwayCoordinate point) => eastbound
            ? new(-point.CrossTrackMetres / 110_574.2758, point.AlongTrackMetres / 111_319.4907)
            : Geographic(point);
        var firstGround = context.Advance(context.Start(), Sample(1, TimeSpan.Zero, Position(new(500, 0)), onGround: true));
        var ground = context.Advance(firstGround.NextCursor,
            Sample(2, TimeSpan.FromSeconds(3), Position(new(500, 0)), onGround: true));
        var cursor = ground.NextCursor;
        var previous = PatternPhase.Unknown;
        DetectionAttemptIdentity? attempt = null;
        uint transitions = 0;
        ulong sequence = 2;
        double time = 4;
        foreach (var target in new[] { PatternPhase.Departure, PatternPhase.Crosswind, PatternPhase.Downwind,
                     PatternPhase.Base, PatternPhase.Final })
        {
            var point = target switch
            {
                PatternPhase.Departure => new RunwayCoordinate(500, 0),
                PatternPhase.Crosswind => Midpoint(geometry.Crosswind.Axis),
                PatternPhase.Downwind => Midpoint(geometry.Downwind.Axis),
                PatternPhase.Base => new RunwayCoordinate(-800, sign * 400),
                PatternPhase.Final => new RunwayCoordinate(-300, 0),
                _ => throw new InvalidOperationException()
            };
            var northTrack = target switch
            {
                PatternPhase.Departure or PatternPhase.Final => 0,
                PatternPhase.Crosswind => sign < 0 ? 270 : 90,
                PatternPhase.Downwind => 180,
                PatternPhase.Base => sign < 0 ? 90 : 270,
                _ => throw new InvalidOperationException()
            };
            var track = (northTrack + (eastbound ? 90 : 0)) % 360;
            var candidate = context.Advance(cursor, Sample(++sequence, TimeSpan.FromSeconds(time),
                Position(point), track: track, climb: target == PatternPhase.Departure ? 2 : null));
            Assert.Equal(previous, candidate.Result.State.ConfirmedPhase);
            Assert.Equal(target, candidate.Result.State.Candidate?.Phase);
            Assert.Empty(candidate.Result.Transitions);
            time += target == PatternPhase.Departure ? 4 : 2;
            var confirmed = context.Advance(candidate.NextCursor, Sample(++sequence, TimeSpan.FromSeconds(time),
                Position(point), track: track, climb: target == PatternPhase.Departure ? 2 : null));
            Assert.Equal(DetectionCycleOutcome.Accepted, confirmed.Result.Outcome);
            Assert.Equal(target, confirmed.Result.State.ConfirmedPhase);
            Assert.Null(confirmed.Result.State.Candidate);
            Assert.Null(confirmed.Result.State.RunwayContact);
            Assert.Equal(++transitions, confirmed.Result.State.TransitionCount);
            attempt ??= confirmed.Result.State.AttemptId;
            Assert.Equal(new DetectionAttemptIdentity("algorithm", "config", "epoch", 4), attempt);
            Assert.Equal(attempt, confirmed.Result.State.AttemptId);
            var transition = Assert.Single(confirmed.Result.Transitions);
            Assert.Equal(previous, transition.From);
            Assert.Equal(target, transition.To);
            Assert.Equal(new DetectionEventIdentity("algorithm", "config", "epoch", sequence, 0), transition.Identity);
            Assert.Equal(TimeSpan.FromSeconds(time), transition.SourceTime);
            Assert.Equal(attempt, transition.AttemptId);
            Assert.Empty(confirmed.Result.Cues);
            Assert.Equal(new PhaseCueLatches(false, false, false, false, false), confirmed.Result.State.CueLatches);
            cursor = confirmed.NextCursor;
            previous = target;
            time++;
        }
        var afterFinal = context.Advance(cursor, Sample(++sequence, TimeSpan.FromSeconds(time),
            Position(Midpoint(geometry.Crosswind.Axis)), track: eastbound ? 0 : 270));
        Assert.Equal(PatternPhase.Final, afterFinal.Result.State.ConfirmedPhase);
        Assert.Empty(afterFinal.Result.Transitions);
        Assert.Equal((uint)5, afterFinal.Result.State.TransitionCount);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void DownwindPersistenceIsInclusiveAndEqualSourceTimeAddsNoDuration(TrafficPatternSide side)
    {
        var context = Context(Configuration(side));
        var crosswind = ConfirmCrosswind(context);
        var first = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        Assert.Equal(PatternPhase.Crosswind, first.Result.State.ConfirmedPhase);
        Assert.Equal(PatternPhase.Downwind, first.Result.State.Candidate?.Phase);
        var equal = context.Advance(first.NextCursor, DownwindSample(context, 8, 12));
        Assert.Equal((uint)2, equal.Result.State.Candidate!.SampleCount);
        Assert.Empty(equal.Result.Transitions);
        var before = context.Advance(equal.NextCursor,
            Sample(9, TimeSpan.FromSeconds(14) - TimeSpan.FromTicks(1),
                Point(context.Configuration, PatternPhase.Downwind).Position, speed: 5, track: 180));
        Assert.Equal(PatternPhase.Crosswind, before.Result.State.ConfirmedPhase);
        Assert.Empty(before.Result.Transitions);
        var exact = context.Advance(before.NextCursor, DownwindSample(context, 10, 14));
        Assert.Equal(PatternPhase.Downwind, exact.Result.State.ConfirmedPhase);
        Assert.Equal((uint)3, exact.Result.State.TransitionCount);
        Assert.Equal(crosswind.Result.State.AttemptId, exact.Result.State.AttemptId);
        Assert.Null(exact.Result.State.Candidate);
        var transition = Assert.Single(exact.Result.Transitions);
        Assert.Equal(PatternPhase.Crosswind, transition.From);
        Assert.Equal(PatternPhase.Downwind, transition.To);
        Assert.Equal(new DetectionEventIdentity("algorithm", "config", "epoch", 10, 0), transition.Identity);
        Assert.Equal(TimeSpan.FromSeconds(14), transition.SourceTime);
        Assert.Equal(crosswind.Result.State.AttemptId, transition.AttemptId);
        Assert.Empty(exact.Result.Cues);
        Assert.Contains(TelemetryQualityIssue.MissingVerticalSpeed, exact.Result.QualityIssues);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void DownwindEnterEvidenceCannotOverrideCrosswindExitHysteresis(TrafficPatternSide side)
    {
        var context = Context(Configuration(side, enterTolerance: 40, exitTolerance: 60));
        var crosswind = ConfirmCrosswind(context);
        var pending = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        var end = context.Configuration.GeometryAuthority.Geometry!.Crosswind.Axis.End;
        var track = side == TrafficPatternSide.Left ? 215 : 145;
        var inside = new RunwayCoordinate(end.AlongTrackMetres - 65, end.CrossTrackMetres);
        var sample = Sample(8, TimeSpan.FromSeconds(13), Geographic(inside), speed: 5, track: track);
        var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
        Assert.False(evidence.Crosswind.InsideEnterBounds);
        Assert.True(evidence.Crosswind.InsideExitBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinExitTolerance, evidence.Crosswind.Direction.Status);
        Assert.True(evidence.Downwind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.Downwind.Direction.Status);
        var held = context.Advance(pending.NextCursor, sample);
        Assert.Equal(PatternPhase.Crosswind, held.Result.State.ConfirmedPhase);
        Assert.Null(held.Result.State.Candidate);
        Assert.Empty(held.Result.Transitions);
        var outside = new RunwayCoordinate(end.AlongTrackMetres - 71, end.CrossTrackMetres);
        var fresh = context.Advance(held.NextCursor,
            Sample(9, TimeSpan.FromSeconds(14), Geographic(outside), speed: 5, track: track));
        Assert.Equal(TimeSpan.FromSeconds(14), fresh.Result.State.Candidate!.FirstSourceTime);
        var confirmed = context.Advance(fresh.NextCursor,
            Sample(10, TimeSpan.FromSeconds(16), Geographic(outside), speed: 5, track: track));
        Assert.Equal(PatternPhase.Downwind, confirmed.Result.State.ConfirmedPhase);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left, false)]
    [InlineData(TrafficPatternSide.Right, false)]
    [InlineData(TrafficPatternSide.Left, true)]
    [InlineData(TrafficPatternSide.Right, true)]
    public void DownwindCompetingEntryOrBaseEvidenceResetsCandidate(TrafficPatternSide side, bool baseOverlap)
    {
        var context = Context(Configuration(side, enterTolerance: 50, exitTolerance: 60));
        var crosswind = ConfirmCrosswind(context);
        var pending = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        var geometry = context.Configuration.GeometryAuthority.Geometry!;
        var sign = side == TrafficPatternSide.Left ? -1 : 1;
        var position = baseOverlap ? new RunwayCoordinate(-800, sign * 800) : geometry.EntryJoinTarget;
        var track = baseOverlap ? sign < 0 ? 135d : 225d : sign < 0 ? 157.5 : 202.5;
        var sample = Sample(8, TimeSpan.FromSeconds(14), Geographic(position), speed: 5, track: track);
        var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
        Assert.False(evidence.Crosswind.InsideExitBounds);
        Assert.True(evidence.Downwind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.Downwind.Direction.Status);
        var competing = baseOverlap ? evidence.Base : evidence.Entry;
        Assert.True(competing.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, competing.Direction.Status);
        var held = context.Advance(pending.NextCursor, sample);
        Assert.Equal(PatternPhase.Crosswind, held.Result.State.ConfirmedPhase);
        Assert.Null(held.Result.State.Candidate);
        Assert.Empty(held.Result.Transitions);
    }

    [Theory]
    [InlineData("speed")]
    [InlineData("track")]
    [InlineData("ground-status")]
    [InlineData("on-ground")]
    [InlineData("low-speed")]
    [InlineData("reverse-track")]
    [InlineData("crosswind-track")]
    [InlineData("exit-track")]
    [InlineData("wrong-side")]
    [InlineData("exit-bounds")]
    public void DownwindTransientOrInsufficientEvidenceHoldsCrosswindAndRestartsCandidate(string failure)
    {
        var context = Context(Configuration());
        var crosswind = ConfirmCrosswind(context);
        var pending = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        var midpoint = Midpoint(context.Configuration.GeometryAuthority.Geometry!.Downwind.Axis);
        var position = failure == "wrong-side" ? new RunwayCoordinate(midpoint.AlongTrackMetres, -midpoint.CrossTrackMetres)
            : failure == "exit-bounds" ? new RunwayCoordinate(midpoint.AlongTrackMetres, midpoint.CrossTrackMetres - 65)
            : midpoint;
        var sample = Sample(8, TimeSpan.FromSeconds(13), Geographic(position),
            speed: failure == "speed" ? null : failure == "low-speed" ? 4.999 : 5,
            track: failure == "track" ? null : failure == "reverse-track" ? 0 : failure == "crosswind-track" ? 270
                : failure == "exit-track" ? 200 : 180,
            onGround: failure == "ground-status" ? null : failure == "on-ground");
        if (failure == "exit-bounds")
        {
            var evidence = GeometryMovementEvidence.Evaluate(context.Configuration, sample, 5);
            Assert.False(evidence.Downwind.InsideEnterBounds);
            Assert.True(evidence.Downwind.InsideExitBounds);
        }
        var held = context.Advance(pending.NextCursor, sample);
        Assert.Equal(DetectionCycleOutcome.Accepted, held.Result.Outcome);
        Assert.Equal(PatternPhase.Crosswind, held.Result.State.ConfirmedPhase);
        Assert.Equal(crosswind.Result.State.AttemptId, held.Result.State.AttemptId);
        Assert.Null(held.Result.State.Candidate);
        Assert.Null(held.Result.State.RunwayContact);
        Assert.Empty(held.Result.Transitions);
        var fresh = context.Advance(held.NextCursor, DownwindSample(context, 9, 14));
        Assert.Equal(TimeSpan.FromSeconds(14), fresh.Result.State.Candidate!.FirstSourceTime);
        Assert.Empty(fresh.Result.Transitions);
        var confirmed = context.Advance(fresh.NextCursor, DownwindSample(context, 10, 16));
        Assert.Equal(PatternPhase.Downwind, confirmed.Result.State.ConfirmedPhase);
    }

    [Fact]
    public void DownwindCandidateAndSampleGapBoundariesMatchExistingRules()
    {
        var context = Context(Configuration(persistence: TimeSpan.FromSeconds(6)));
        var crosswind = ConfirmCrosswind(context);
        var sequence = crosswind.Result.Snapshot.SampleSequence + 1;
        var time = crosswind.Result.Snapshot.SourceTime.TotalSeconds + 1;
        var pending = context.Advance(crosswind.NextCursor, DownwindSample(context, sequence, time));
        var exact = context.Advance(pending.NextCursor, DownwindSample(context, sequence + 1, time + 4));
        Assert.Equal((uint)2, exact.Result.State.Candidate!.SampleCount);
        var point = Point(context.Configuration, PatternPhase.Downwind).Position;
        var beyond = context.Advance(pending.NextCursor, Sample(sequence + 1,
            TimeSpan.FromSeconds(time + 4) + TimeSpan.FromTicks(1), point, speed: 5, track: 180));
        Assert.Equal((uint)1, beyond.Result.State.Candidate!.SampleCount);
        Assert.Equal(beyond.Result.Snapshot.SourceTime, beyond.Result.State.Candidate.FirstSourceTime);
        var confirmed = context.Advance(exact.NextCursor, DownwindSample(context, sequence + 2, time + 6));
        Assert.Equal(PatternPhase.Downwind, confirmed.Result.State.ConfirmedPhase);
        var maximum = context.Advance(pending.NextCursor, DownwindSample(context, sequence + 1, time + 10));
        Assert.Equal(DetectionCycleOutcome.Accepted, maximum.Result.Outcome);
        Assert.Equal((uint)1, maximum.Result.State.Candidate!.SampleCount);
        var gap = context.Advance(pending.NextCursor, Sample(sequence + 1,
            TimeSpan.FromSeconds(time + 10) + TimeSpan.FromTicks(1), point, speed: 5, track: 180));
        Assert.Equal(DetectionCycleOutcome.Interrupted, gap.Result.Outcome);
        Assert.Equal(PatternPhase.Crosswind, gap.Result.State.ConfirmedPhase);
        Assert.Null(gap.Result.State.Candidate);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("gap")]
    [InlineData("both")]
    public void DownwindInterruptionClearsCandidateButRetainsPhaseAndAttempt(string failure)
    {
        var context = Context(Configuration());
        var crosswind = ConfirmCrosswind(context);
        var pending = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        var time = failure == "position" ? 13 : 23;
        var interrupted = context.Advance(pending.NextCursor,
            DownwindSample(context, 8, time, positionAvailable: failure == "gap"));
        Assert.Equal(DetectionCycleOutcome.Interrupted, interrupted.Result.Outcome);
        Assert.Equal(failure == "position" ? SampleInterruptionReason.PositionContinuityLost :
            SampleInterruptionReason.SampleContinuityGap, interrupted.Result.InterruptionReason);
        Assert.Equal(PatternPhase.Crosswind, interrupted.Result.State.ConfirmedPhase);
        Assert.Equal(crosswind.Result.State.AttemptId, interrupted.Result.State.AttemptId);
        Assert.Equal((uint)2, interrupted.Result.State.TransitionCount);
        Assert.Equal((ulong)8, interrupted.Result.State.LastSampleSequence);
        Assert.Equal(TimeSpan.FromSeconds(time), interrupted.Result.State.LastSourceTime);
        Assert.Equal(pending.Result.State.LastAcceptedPosition, interrupted.Result.State.LastAcceptedPosition);
        Assert.Equal(PositionContinuity.Interrupted, interrupted.Result.State.PositionContinuity);
        Assert.Null(interrupted.Result.State.Candidate);
        Assert.Empty(interrupted.Result.Transitions);
        var midpoint = Midpoint(context.Configuration.GeometryAuthority.Geometry!.Downwind.Axis);
        var shifted = new RunwayCoordinate(midpoint.AlongTrackMetres + 20, midpoint.CrossTrackMetres);
        var fresh = context.Advance(interrupted.NextCursor, DownwindSample(context, 9, time, point: shifted));
        Assert.Equal(DetectionCycleOutcome.Accepted, fresh.Result.Outcome);
        Assert.Equal((uint)1, fresh.Result.State.Candidate!.SampleCount);
        Assert.Equal(TimeSpan.FromSeconds(time), fresh.Result.State.Candidate.FirstSourceTime);
        Assert.Equal(PositionContinuity.Continuous, fresh.Result.State.PositionContinuity);
        var confirmed = context.Advance(fresh.NextCursor, DownwindSample(context, 10, time + 2, point: shifted));
        Assert.Equal(PatternPhase.Downwind, confirmed.Result.State.ConfirmedPhase);
    }

    [Fact]
    public void DownwindRejectedSamplesPreserveCandidateAndConfirmationIdentity()
    {
        var context = Context(Configuration());
        var crosswind = ConfirmCrosswind(context);
        var pending = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        var point = Point(context.Configuration, PatternPhase.Downwind).Position;
        var midpoint = Midpoint(context.Configuration.GeometryAuthority.Geometry!.Downwind.Axis);
        var cases = new[]
        {
            (Sample(7, TimeSpan.FromSeconds(11), null, epoch: "other"), SampleRejectionReason.EpochMismatch),
            (Sample(7, TimeSpan.FromSeconds(11), null), SampleRejectionReason.DuplicateSequence),
            (Sample(6, TimeSpan.FromSeconds(11), point), SampleRejectionReason.ReversedSequence),
            (Sample(8, TimeSpan.FromSeconds(11), point), SampleRejectionReason.ReversedSourceTime),
            (Sample(8, TimeSpan.FromSeconds(13), Geographic(new(100_000, 0))), SampleRejectionReason.ImplausibleRelocation),
            (DownwindSample(context, 8, 12, point: new(midpoint.AlongTrackMetres + 1, midpoint.CrossTrackMetres)),
                SampleRejectionReason.ImplausibleRelocation)
        };
        foreach (var (sample, reason) in cases)
        {
            var rejected = context.Advance(pending.NextCursor, sample);
            Assert.Equal(DetectionCycleOutcome.Rejected, rejected.Result.Outcome);
            Assert.Equal(reason, rejected.Result.RejectionReason);
            var prior = pending.Result.State;
            Assert.Equal(new PhaseDetectionState(prior.AlgorithmId, prior.ConfigurationId, prior.StreamEpoch,
                prior.LastSampleSequence, prior.LastSourceTime, prior.ConfirmedPhase, prior.AttemptId, prior.Candidate,
                prior.TransitionCount, prior.RejectedSampleCount + 1, prior.CueLatches,
                prior.LastAcceptedPosition, prior.PositionContinuity), rejected.Result.State);
            Assert.Empty(rejected.Result.QualityIssues);
            Assert.Empty(rejected.Result.Transitions);
            Assert.Empty(rejected.Result.Cues);
            var resumed = context.Advance(rejected.NextCursor, DownwindSample(context, 8, 14));
            Assert.Equal(PatternPhase.Downwind, resumed.Result.State.ConfirmedPhase);
            Assert.Equal(crosswind.Result.State.AttemptId, resumed.Result.State.AttemptId);
            Assert.Equal(new DetectionEventIdentity("algorithm", "config", "epoch", 8, 0),
                Assert.Single(resumed.Result.Transitions).Identity);
            Assert.Equal((uint)1, resumed.Result.State.RejectedSampleCount);
        }
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void DownwindIntegrationDoesNotAuthorizeInitialSkippedOrReverseTransitions(TrafficPatternSide side)
    {
        var context = Context(Configuration(side));
        var initial = context.Advance(context.Start(), DownwindSample(context, 1, 0));
        var initialAgain = context.Advance(initial.NextCursor, DownwindSample(context, 2, 2));
        Assert.Equal(PatternPhase.Unknown, initialAgain.Result.State.ConfirmedPhase);
        Assert.Null(initialAgain.Result.State.Candidate);
        var crosswind = ConfirmCrosswind(context);
        var pending = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        var downwind = context.Advance(pending.NextCursor, DownwindSample(context, 8, 14));
        var cursor = downwind.NextCursor;
        var samples = new[]
        {
            CrosswindSample(context, 9, 15), CrosswindSample(context, 10, 17),
            DepartureSample(11, 18), DepartureSample(12, 20),
            Sample(13, TimeSpan.FromSeconds(21), Point(context.Configuration, PatternPhase.Entry).Position,
                track: Point(context.Configuration, PatternPhase.Entry).Track),
            Sample(14, TimeSpan.FromSeconds(23), Point(context.Configuration, PatternPhase.Final).Position, track: 0)
        };
        foreach (var sample in samples)
        {
            var held = context.Advance(cursor, sample);
            Assert.Equal(PatternPhase.Downwind, held.Result.State.ConfirmedPhase);
            Assert.Equal(downwind.Result.State.AttemptId, held.Result.State.AttemptId);
            Assert.Equal((uint)3, held.Result.State.TransitionCount);
            Assert.Null(held.Result.State.Candidate);
            Assert.Empty(held.Result.Transitions);
            cursor = held.NextCursor;
        }
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void OutboundReplayThroughFinalIsDeterministicAcrossRejectionAndInterruption(TrafficPatternSide side)
    {
        var config = Configuration(side);
        var context = Context(config);
        var basePoint = Point(config, PatternPhase.Base);
        var finalPoint = Point(config, PatternPhase.Final);
        var samples = new[]
        {
            DepartureSample(1, 0, onGround: true), DepartureSample(2, 3, onGround: true),
            DepartureSample(3, 4), DepartureSample(4, 8), CrosswindSample(context, 5, 9), CrosswindSample(context, 6, 11),
            DownwindSample(context, 7, 12), DownwindSample(context, 7, 12),
            DownwindSample(context, 8, 13, positionAvailable: false),
            DownwindSample(context, 9, 14), DownwindSample(context, 10, 16),
            Sample(11, TimeSpan.FromSeconds(17), basePoint.Position, track: basePoint.Track),
            Sample(12, TimeSpan.FromSeconds(19), basePoint.Position, track: basePoint.Track),
            Sample(13, TimeSpan.FromSeconds(20), finalPoint.Position, track: finalPoint.Track),
            Sample(14, TimeSpan.FromSeconds(22), finalPoint.Position, track: finalPoint.Track)
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
        var first = Run(context);
        foreach (var again in new[] { Run(context), Run(Context(config)) })
            for (var i = 0; i < first.Count; i++)
            {
                Assert.Equal(first[i].State, again[i].State);
                Assert.Equal(first[i].Outcome, again[i].Outcome);
                Assert.Equal(first[i].RejectionReason, again[i].RejectionReason);
                Assert.Equal(first[i].InterruptionReason, again[i].InterruptionReason);
                Assert.Equal(first[i].QualityIssues, again[i].QualityIssues);
                Assert.Equal(first[i].Transitions, again[i].Transitions);
                Assert.Equal(first[i].Cues, again[i].Cues);
            }
        Assert.Equal(PatternPhase.Final, first[^1].State.ConfirmedPhase);
        Assert.Equal((uint)5, first[^1].State.TransitionCount);
        Assert.Equal((uint)1, first[^1].State.RejectedSampleCount);
        Assert.Equal(new DetectionAttemptIdentity("algorithm", "config", "epoch", 4), first[^1].State.AttemptId);
        Assert.Equal(new ulong[] { 4, 6, 10, 12, 14 }, first.SelectMany(result => result.Transitions)
            .Select(transition => transition.Identity.SampleSequence));
        Assert.All(first.SelectMany(result => result.Transitions), transition =>
        {
            Assert.Equal((uint)0, transition.Identity.Ordinal);
            Assert.Equal(first[^1].State.AttemptId, transition.AttemptId);
        });
        var crosswind = ConfirmCrosswind(context);
        var branchA = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        var branchB = context.Advance(crosswind.NextCursor, DownwindSample(context, 7, 12));
        Assert.Equal(branchA.Result.State, branchB.Result.State);
    }
}
