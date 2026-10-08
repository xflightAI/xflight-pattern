using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;
using XFlight.Pattern.Core.Geometry;
using XFlight.Pattern.Core.PhaseDetection;

namespace XFlight.Pattern.Core.Tests;

public sealed class GeometryMovementEvidenceTests
{
    private static PhaseDetectionConfiguration Configuration(TrafficPatternSide side)
    {
        var circuit = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(0)),
            TrafficPatternSideResolver.Resolve(published: side), PatternAltitudeResolution.Unresolved);
        var runway = new RunwayGeometryReference(new AirportIdentifier("TEST"), "01", new(0, 0), new(0.01, 0));
        var geometry = PatternGeometryResult.Generate(circuit, runway, PatternGeometryValidationTests.Parameters());
        var policy = new PhaseDetectionPolicy(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3),
            TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(8), 10, 20, 15, 25, 10, 20, 1, 30, 100, 500);
        return new("algorithm", "config", geometry, policy, 25);
    }

    private static GeographicCoordinate Geographic(RunwayCoordinate point) =>
        new(point.AlongTrackMetres / 110_574.2758, point.CrossTrackMetres / 111_319.4907);

    private static TelemetryObservation<double> Number(double? value) => value is { } v
        ? new(v, TelemetryValidity.Available, TelemetryProvenance.Measured)
        : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable);

    private static TelemetrySnapshot Snapshot(RunwayCoordinate? point, double? speed, double? track) =>
        new("epoch", 1, TimeSpan.Zero,
            point is { } p
                ? new(Geographic(p), TelemetryValidity.Available, TelemetryProvenance.Measured)
                : new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable),
            Number(null), Number(speed), Number(track), Number(null),
            new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable));

    private static GeometryMovementEvidence Evidence(PhaseDetectionConfiguration configuration,
        RunwayCoordinate? point, double? speed, double? track) =>
        GeometryMovementEvidence.Evaluate(configuration, Snapshot(point, speed, track), 5);

    [Fact]
    public void ClosedCorridorAndAreaBoundariesUseExplicitMargins()
    {
        var corridor = new LegCorridor(new(new(0, 0), new(100, 0)), HorizontalDistance.FromMetres(10));
        Assert.True(GeometryMovementEvidence.Contains(corridor, new(0, 10), 0));
        Assert.True(GeometryMovementEvidence.Contains(corridor, new(100, -10), 0));
        Assert.False(GeometryMovementEvidence.Contains(corridor, new(-0.001, 0), 0));
        Assert.True(GeometryMovementEvidence.Contains(corridor, new(-10, 0), 10));
        Assert.False(GeometryMovementEvidence.Contains(corridor, new(-10.001, 0), 10));
        var diagonal = new LegCorridor(new(new(0, 0), new(100, 100)), HorizontalDistance.FromMetres(10));
        Assert.True(GeometryMovementEvidence.Contains(diagonal, new(50, 50), 0));
        Assert.False(GeometryMovementEvidence.Contains(diagonal, new(50, 70), 0));

        var area = new HorizontalArea(-100, -50, -20, 20);
        Assert.True(GeometryMovementEvidence.Contains(area, new(-100, 20), 0));
        Assert.False(GeometryMovementEvidence.Contains(area, new(-100.001, 20), 0));
        Assert.True(GeometryMovementEvidence.Contains(area, new(-110, 20), 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryMovementEvidence.Contains(area, new(0, 0), double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => GeometryMovementEvidence.Contains(corridor, new(0, 0), -1));
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left, 270, 90, 135)]
    [InlineData(TrafficPatternSide.Right, 90, 270, 225)]
    public void MirroredLegsAndTrueTrackAgreeWithRunwayFrame(TrafficPatternSide side,
        double crosswindTrack, double baseTrack, double entryTrack)
    {
        var config = Configuration(side);
        var geometry = config.GeometryAuthority.Geometry!;
        var entry = Evidence(config, Midpoint(geometry.Entry.Axis), 25, entryTrack);
        Assert.True(entry.Entry.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, entry.Entry.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.OutsideExitTolerance, entry.Downwind.Direction.Status);

        var departure = Evidence(config, Midpoint(geometry.Upwind.Axis), 25, 0);
        Assert.True(departure.DepartureUpwind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, departure.DepartureUpwind.Direction.Status);
        var crosswind = Evidence(config, Midpoint(geometry.Crosswind.Axis), 25, crosswindTrack);
        Assert.True(crosswind.Crosswind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, crosswind.Crosswind.Direction.Status);
        var downwind = Evidence(config, Midpoint(geometry.Downwind.Axis), 25, 180);
        Assert.True(downwind.Downwind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, downwind.Downwind.Direction.Status);
        var @base = Evidence(config, new(-800, Math.Sign(geometry.Downwind.Axis.Start.CrossTrackMetres) * 400), 25, baseTrack);
        Assert.True(@base.Base.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, @base.Base.Direction.Status);
        var final = Evidence(config, Midpoint(geometry.Final.Axis), 25, 0);
        Assert.True(final.Final.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, final.Final.Direction.Status);
    }

    [Fact]
    public void BaseIntervalIsExactAndOverlapRemainsSeparateEvidence()
    {
        var config = Configuration(TrafficPatternSide.Left);
        var inOverlap = Evidence(config, new(-800, 0), 25, 0);
        Assert.True(inOverlap.Base.InsideEnterBounds);
        Assert.True(inOverlap.Final.InsideEnterBounds);
        Assert.True(inOverlap.InsideOuterExitEnvelope);
        Assert.Equal(DirectionEvidenceStatus.OutsideExitTolerance, inOverlap.Base.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, inOverlap.Final.Direction.Status);

        var outsideWindow = Evidence(config, new(-1000.1, -400), 25, 90);
        Assert.False(outsideWindow.Base.InsideEnterBounds);
        Assert.False(outsideWindow.InsideBaseTurnWindow);
        var insideWindow = Evidence(config, new(-800, -800), 25, 90);
        Assert.True(insideWindow.InsideBaseTurnWindow);
        Assert.True(insideWindow.Base.InsideEnterBounds);
        Assert.True(insideWindow.Downwind.InsideEnterBounds);
    }

    [Fact]
    public void BaseMembershipIncludesBothExactLongitudinalLimitsButNoExtensionBeyondThem()
    {
        var runway = new RunwayGeometryReference(new AirportIdentifier("TEST"), "01", new(0, 0), new(0.01, 0));
        var farPoint = new GeographicCoordinate(-0.01, 0);
        var nearPoint = new GeographicCoordinate(-0.006, 0);
        var farther = -runway.ToRunway(farPoint).AlongTrackMetres;
        var nearer = -runway.ToRunway(nearPoint).AlongTrackMetres;
        var circuit = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(0)),
            TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Left), PatternAltitudeResolution.Unresolved);
        var geometry = PatternGeometryResult.Generate(circuit, runway,
            PatternGeometryValidationTests.Parameters(nearer: nearer, farther: farther, final: farther + 500));
        var config = new PhaseDetectionConfiguration("algorithm", "exact-base", geometry,
            Configuration(TrafficPatternSide.Left).Policy, 25);

        GeometryMovementEvidence At(GeographicCoordinate point) => GeometryMovementEvidence.Evaluate(config,
            new TelemetrySnapshot("epoch", 1, TimeSpan.Zero,
                new(point, TelemetryValidity.Available, TelemetryProvenance.Measured),
                Number(null), Number(25), Number(90), Number(null),
                new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable)), 5);

        Assert.True(At(farPoint).Base.InsideEnterBounds);
        Assert.True(At(nearPoint).Base.InsideEnterBounds);
        Assert.False(At(new(-0.010000001, 0)).Base.InsideExitBounds);
        Assert.False(At(new(-0.005999999, 0)).Base.InsideExitBounds);

        var window = geometry.Geometry!.BaseTurnWindow;
        var windowCross = (window.MinimumCrossTrackMetres + window.MaximumCrossTrackMetres) / 2;
        Assert.True(GeometryMovementEvidence.Contains(window, new(-farther, windowCross), 0));
        Assert.True(GeometryMovementEvidence.Contains(window, new(-nearer, windowCross), 0));
        Assert.False(GeometryMovementEvidence.Contains(window,
            new(double.BitDecrement(-farther), windowCross), 0));
        Assert.False(GeometryMovementEvidence.Contains(window,
            new(double.BitIncrement(-nearer), windowCross), 0));
    }

    [Fact]
    public void ContactExitAndMissingPositionAreIndependent()
    {
        var config = Configuration(TrafficPatternSide.Right);
        var contact = Evidence(config, new(500, 0), 25, 0);
        Assert.True(contact.InsideRunwayContactEnvelope);
        Assert.True(contact.DepartureUpwind.InsideEnterBounds);
        Assert.True(contact.InsideOuterExitEnvelope);
        var distant = Evidence(config, new(10_000, 0), 25, 0);
        Assert.False(distant.InsideOuterExitEnvelope);
        Assert.False(distant.InsideRunwayContactEnvelope);
        var missing = Evidence(config, null, 25, 0);
        Assert.Null(missing.Position);
        Assert.False(missing.Entry.InsideExitBounds);
        Assert.False(missing.InsideOuterExitEnvelope);
    }

    [Fact]
    public void M4EnvelopesIncludeEachBoundaryAndExcludePointsJustBeyondIt()
    {
        var config = Configuration(TrafficPatternSide.Right);
        foreach (var area in new[] { config.RunwayContactEnvelope, config.OuterExitEnvelope })
        {
            var midAlong = (area.MinimumAlongTrackMetres + area.MaximumAlongTrackMetres) / 2;
            var midCross = (area.MinimumCrossTrackMetres + area.MaximumCrossTrackMetres) / 2;
            Assert.True(GeometryMovementEvidence.Contains(area, new(area.MinimumAlongTrackMetres, midCross), 0));
            Assert.True(GeometryMovementEvidence.Contains(area, new(area.MaximumAlongTrackMetres, midCross), 0));
            Assert.True(GeometryMovementEvidence.Contains(area, new(midAlong, area.MinimumCrossTrackMetres), 0));
            Assert.True(GeometryMovementEvidence.Contains(area, new(midAlong, area.MaximumCrossTrackMetres), 0));
            Assert.False(GeometryMovementEvidence.Contains(area,
                new(double.BitDecrement(area.MinimumAlongTrackMetres), midCross), 0));
            Assert.False(GeometryMovementEvidence.Contains(area,
                new(double.BitIncrement(area.MaximumAlongTrackMetres), midCross), 0));
            Assert.False(GeometryMovementEvidence.Contains(area,
                new(midAlong, double.BitDecrement(area.MinimumCrossTrackMetres)), 0));
            Assert.False(GeometryMovementEvidence.Contains(area,
                new(midAlong, double.BitIncrement(area.MaximumCrossTrackMetres)), 0));
        }
        var threshold = config.GeometryAuthority.RunwayReference.LandingThreshold;
        var thresholdSample = new TelemetrySnapshot("epoch", 1, TimeSpan.Zero,
            new(threshold, TelemetryValidity.Available, TelemetryProvenance.Measured),
            Number(null), Number(25), Number(0), Number(null),
            new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable));
        Assert.True(GeometryMovementEvidence.Evaluate(config, thresholdSample, 5).InsideRunwayContactEnvelope);
    }

    [Fact]
    public void DirectionNeedsUsableSpeedAndAvailableTrack()
    {
        var config = Configuration(TrafficPatternSide.Left);
        var point = new RunwayCoordinate(500, 0);
        Assert.Equal(DirectionEvidenceStatus.MissingGroundSpeed,
            Evidence(config, point, null, 0).DepartureUpwind.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.BelowMinimumGroundSpeed,
            Evidence(config, point, 4.999, 0).DepartureUpwind.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance,
            Evidence(config, point, 5, 0).DepartureUpwind.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.MissingGroundTrack,
            Evidence(config, point, 25, null).DepartureUpwind.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.WithinExitTolerance,
            Evidence(config, point, 25, 20).DepartureUpwind.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.OutsideExitTolerance,
            Evidence(config, point, 25, 30).DepartureUpwind.Direction.Status);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GeometryMovementEvidence.Evaluate(config, Snapshot(point, 25, 0), 0));
    }

    [Theory]
    [InlineData(14.999999, DirectionEvidenceStatus.WithinEnterTolerance)]
    [InlineData(15, DirectionEvidenceStatus.WithinEnterTolerance)]
    [InlineData(15.000001, DirectionEvidenceStatus.WithinExitTolerance)]
    [InlineData(15.000000000001, DirectionEvidenceStatus.WithinExitTolerance)]
    [InlineData(24.999999, DirectionEvidenceStatus.WithinExitTolerance)]
    [InlineData(25, DirectionEvidenceStatus.WithinExitTolerance)]
    [InlineData(25.000001, DirectionEvidenceStatus.OutsideExitTolerance)]
    [InlineData(25.000000000001, DirectionEvidenceStatus.OutsideExitTolerance)]
    [InlineData(180, DirectionEvidenceStatus.OutsideExitTolerance)]
    [InlineData(345, DirectionEvidenceStatus.WithinEnterTolerance)]
    [InlineData(335, DirectionEvidenceStatus.WithinExitTolerance)]
    public void DirectionTolerancesAreInclusiveAndWrapAroundTrueNorth(double track,
        DirectionEvidenceStatus expectedStatus)
    {
        var result = Evidence(Configuration(TrafficPatternSide.Left), new(500, 0), 25, track);
        Assert.Equal(expectedStatus, result.DepartureUpwind.Direction.Status);
        Assert.NotNull(result.DepartureUpwind.Direction.AngularErrorDegrees);
    }

    [Fact]
    public void InvalidAvailableTrackAndSpeedAreRejectedByM4B1Snapshot()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot(new(500, 0), 25, 360));
        Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot(new(500, 0), 25, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Snapshot(new(500, 0), -1, 0));
    }

    [Fact]
    public void DirectionUsesThresholdAxisInsteadOfStoredRunwayHeading()
    {
        var circuit = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(0)),
            TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Right), PatternAltitudeResolution.Unresolved);
        var runway = new RunwayGeometryReference(new AirportIdentifier("TEST"), "01", new(0, 0), new(0, 0.01));
        var northConfig = Configuration(TrafficPatternSide.Right);
        var eastGeometry = PatternGeometryResult.Generate(circuit, runway, PatternGeometryValidationTests.Parameters());
        var eastConfig = new PhaseDetectionConfiguration("algorithm", "east", eastGeometry, northConfig.Policy, 25);
        var sample = new TelemetrySnapshot("epoch", 1, TimeSpan.Zero,
            new(new GeographicCoordinate(0, 0.005), TelemetryValidity.Available, TelemetryProvenance.Measured),
            Number(null), Number(25), Number(90), Number(null),
            new(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable));
        var evidence = GeometryMovementEvidence.Evaluate(eastConfig, sample, 5);
        Assert.True(evidence.DepartureUpwind.InsideEnterBounds);
        Assert.Equal(DirectionEvidenceStatus.WithinEnterTolerance, evidence.DepartureUpwind.Direction.Status);
        var exitBoundary = new TelemetrySnapshot("epoch", 1, TimeSpan.Zero,
            sample.Position, sample.AltitudeMslMetres, sample.GroundSpeedMetresPerSecond,
            Number(115), sample.VerticalSpeedMetresPerSecond, sample.OnGround);
        Assert.Equal(DirectionEvidenceStatus.WithinExitTolerance,
            GeometryMovementEvidence.Evaluate(eastConfig, exitBoundary, 5).DepartureUpwind.Direction.Status);
        Assert.Equal(DirectionEvidenceStatus.OutsideExitTolerance,
            GeometryMovementEvidence.Evaluate(eastConfig, Snapshot(new(500, 0), 25, 0), 5)
                .DepartureUpwind.Direction.Status);
    }

    private static RunwayCoordinate Midpoint(HorizontalSegment axis) => new(
        (axis.Start.AlongTrackMetres + axis.End.AlongTrackMetres) / 2,
        (axis.Start.CrossTrackMetres + axis.End.CrossTrackMetres) / 2);
}
