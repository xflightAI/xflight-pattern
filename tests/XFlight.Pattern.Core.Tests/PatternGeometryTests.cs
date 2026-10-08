using System.Globalization;
using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;
using XFlight.Pattern.Core.Geometry;

namespace XFlight.Pattern.Core.Tests;

public sealed class PatternGeometryTests
{
    [Theory]
    [InlineData(TrafficPatternSide.Left, -1d)]
    [InlineData(TrafficPatternSide.Right, 1d)]
    public void LegsAndAreas_HaveConfiguredBoundsAndTravelDirection(TrafficPatternSide side, double sign)
    {
        var geometry = Geometry(side);
        var length = geometry.RunwayReference.RunwayLength.Metres;
        var p = geometry.Parameters;
        var upwind = length + p.UpwindExtension.Metres;
        var cross = sign * p.DownwindOffset.Metres;
        Assert.Equal(new RunwayCoordinate(0, 0), geometry.RunwayCenterline.Start);
        Assert.Equal(new RunwayCoordinate(length, 0), geometry.RunwayCenterline.End);
        AssertSegment(geometry.Upwind.Axis, new(length, 0), new(upwind, 0));
        AssertSegment(geometry.Crosswind.Axis, new(upwind, 0), new(upwind, cross));
        AssertSegment(geometry.Downwind.Axis, new(upwind, cross), new(-1_000, cross));
        AssertSegment(geometry.Final.Axis, new(-1_500, 0), new(0, 0));

        var window = geometry.BaseTurnWindow;
        Assert.Equal(-p.FartherBaseTurnDistance.Metres, window.MinimumAlongTrackMetres);
        Assert.Equal(-p.NearerBaseTurnDistance.Metres, window.MaximumAlongTrackMetres);
        Assert.True(window.MaximumAlongTrackMetres < 0d);
        Assert.Equal(cross - 50, window.MinimumCrossTrackMetres);
        Assert.Equal(cross + 50, window.MaximumCrossTrackMetres);
        Assert.True(sign * window.MinimumCrossTrackMetres > 0d);
        Assert.True(sign * window.MaximumCrossTrackMetres > 0d);
        var baseArea = geometry.BaseArea;
        Assert.Equal(window.MinimumAlongTrackMetres, baseArea.MinimumAlongTrackMetres);
        Assert.Equal(window.MaximumAlongTrackMetres, baseArea.MaximumAlongTrackMetres);
        Assert.Equal(Math.Min(0, cross) - 50, baseArea.MinimumCrossTrackMetres);
        Assert.Equal(Math.Max(0, cross) + 50, baseArea.MaximumCrossTrackMetres);
        Assert.True(baseArea.MinimumCrossTrackMetres < 0d && baseArea.MaximumCrossTrackMetres > 0d);
        Assert.True(geometry.Final.Axis.Start.AlongTrackMetres <= baseArea.MinimumAlongTrackMetres);
        Assert.True(baseArea.MaximumAlongTrackMetres < geometry.Final.Axis.End.AlongTrackMetres);

        foreach (var corridor in Corridors(geometry))
            AssertRectangle(corridor, p.CorridorHalfWidth.Metres);
    }

    [Fact]
    public void LeftAndRight_AllSegmentsBoundsAndFootprintsAreExactMirrors()
    {
        var left = Geometry(TrafficPatternSide.Left);
        var right = Geometry(TrafficPatternSide.Right);
        Assert.Equal(left.RunwayCenterline, right.RunwayCenterline);
        var leftCorridors = Corridors(left);
        var rightCorridors = Corridors(right);
        for (var i = 0; i < leftCorridors.Length; i++)
        {
            var l = leftCorridors[i];
            var r = rightCorridors[i];
            Mirror(l.Axis.Start, r.Axis.Start);
            Mirror(l.Axis.End, r.Axis.End);
            Assert.Equal(l.Axis.Length, r.Axis.Length);
            Assert.Equal(l.HalfWidth, r.HalfWidth);
            // Reflection reverses winding, so plus/minus normal corners exchange.
            Mirror(l.StartPlus, r.StartMinus);
            Mirror(l.EndPlus, r.EndMinus);
            Mirror(l.EndMinus, r.EndPlus);
            Mirror(l.StartMinus, r.StartPlus);
        }
        MirrorArea(left.BaseTurnWindow, right.BaseTurnWindow);
        MirrorArea(left.BaseArea, right.BaseArea);
        Mirror(left.EntryJoinTarget, right.EntryJoinTarget);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left, -1d, 700d)]
    [InlineData(TrafficPatternSide.Right, 1d, 1_200d)]
    public void Entry_JoinsAbeamMidpointAt45DegreesFromOutside(TrafficPatternSide side, double sign, double entryLength)
    {
        var geometry = Geometry(side, PatternGeometryValidationTests.Parameters(entry: entryLength));
        var entry = geometry.Entry.Axis;
        Assert.Equal(geometry.EntryJoinTarget, entry.End);
        Assert.Equal(geometry.RunwayReference.RunwayLength.Metres / 2d, entry.End.AlongTrackMetres);
        Assert.Equal(sign * geometry.Parameters.DownwindOffset.Metres, entry.End.CrossTrackMetres);
        var dx = entry.End.AlongTrackMetres - entry.Start.AlongTrackMetres;
        var dy = entry.End.CrossTrackMetres - entry.Start.CrossTrackMetres;
        var magnitude = double.Hypot(dx, dy);
        var downwindDx = geometry.Downwind.Axis.End.AlongTrackMetres - geometry.Downwind.Axis.Start.AlongTrackMetres;
        var downwindDy = geometry.Downwind.Axis.End.CrossTrackMetres - geometry.Downwind.Axis.Start.CrossTrackMetres;
        var downwindMagnitude = double.Hypot(downwindDx, downwindDy);
        var cosine = (dx * downwindDx + dy * downwindDy) / (magnitude * downwindMagnitude);
        Close(Math.Sqrt(0.5d), cosine);
        Close(Math.PI / 4d, Math.Acos(cosine));
        Close(entryLength, magnitude);
        Assert.True(dx < 0d);
        Assert.True(sign * dy < 0d);
        Assert.True(Math.Abs(entry.Start.CrossTrackMetres) > geometry.Parameters.DownwindOffset.Metres + geometry.Parameters.CorridorHalfWidth.Metres);
    }

    [Fact]
    public void ReciprocalEnds_UseIndependentThresholdOriginsAndM2Sides()
    {
        var forward = Reference();
        var reciprocal = new RunwayGeometryReference(new AirportIdentifier("TEST"), "19", forward.OppositeThreshold, forward.LandingThreshold);
        var left = PatternGeometryResult.Generate(Circuit(TrafficPatternSide.Left, "01"), forward, PatternGeometryValidationTests.Parameters()).Geometry!;
        var right = PatternGeometryResult.Generate(Circuit(TrafficPatternSide.Right, "19"), reciprocal, PatternGeometryValidationTests.Parameters()).Geometry!;
        Assert.Equal(new RunwayCoordinate(0, 0), reciprocal.ToRunway(forward.OppositeThreshold));
        Assert.True(reciprocal.ToRunway(forward.LandingThreshold).AlongTrackMetres > 0d);
        Assert.True(left.Downwind.Axis.Start.CrossTrackMetres < 0d);
        Assert.True(right.Downwind.Axis.Start.CrossTrackMetres > 0d);
        Assert.Equal("19", right.Circuit.RunwayEnd.Identifier);
        Assert.NotEqual(left.RunwayReference.LandingThreshold, right.RunwayReference.LandingThreshold);
        Assert.NotEqual(left, right);
    }

    [Fact]
    public void UnknownSide_IsExplicitlyUnavailableAndPreservesSourceAuthority()
    {
        var circuit = Circuit(TrafficPatternSide.Unknown);
        var reference = Reference();
        var parameters = PatternGeometryValidationTests.Parameters();
        var result = PatternGeometryResult.Generate(circuit, reference, parameters);
        Assert.False(result.IsAvailable);
        Assert.Null(result.Geometry);
        Assert.Equal(PatternGeometryUnavailableReason.UnknownTrafficPatternSide, result.UnavailableReason);
        Assert.Same(circuit, result.Circuit);
        Assert.Same(reference, result.RunwayReference);
        Assert.Same(parameters, result.Parameters);
        Assert.Same(PatternAltitudeResolution.Unresolved, result.Circuit.PatternAltitude);
    }

    [Fact]
    public void IdentityMismatch_IsRejectedEvenWhenSideIsUnknown()
    {
        var parameters = PatternGeometryValidationTests.Parameters();
        foreach (var side in new[] { TrafficPatternSide.Left, TrafficPatternSide.Unknown })
        {
            Assert.Throws<ArgumentException>(() => PatternGeometryResult.Generate(Circuit(side, "02"), Reference(), parameters));
            var wrongAirport = new RunwayGeometryReference(new AirportIdentifier("OTHER"), "01", new(0, 0), new(0.01, 0));
            Assert.Throws<ArgumentException>(() => PatternGeometryResult.Generate(Circuit(side), wrongAirport, parameters));
        }
    }

    [Fact]
    public void AltitudeAndProvenance_AreRetainedWithoutVerticalGeometryOrDefaults()
    {
        foreach (var altitude in new[]
        {
            PatternAltitudeResolution.Unresolved,
            PatternAltitudeResolution.Published(new PatternAltitudeMsl(2_000)),
            PatternAltitudeResolution.TrainingDefault(new AirportElevationMsl(850))
        })
        {
            var circuit = Circuit(TrafficPatternSide.Left, altitude: altitude);
            var result = PatternGeometryResult.Generate(circuit, Reference(), PatternGeometryValidationTests.Parameters());
            Assert.True(result.IsAvailable);
            Assert.Null(result.UnavailableReason);
            Assert.Same(circuit, result.Geometry!.Circuit);
            Assert.Same(altitude, result.Geometry.Circuit.PatternAltitude);
            Assert.Same(circuit.TrafficPatternSide, result.Geometry.Circuit.TrafficPatternSide);
            Assert.Equal(TrafficPatternSideProvenance.Published, result.Geometry.Circuit.TrafficPatternSide.Provenance);
            if (!altitude.IsResolved)
                Assert.Null(result.Geometry.Circuit.PatternAltitude.TargetMsl);
        }
    }

    [Fact]
    public void SameInputs_ProduceEqualGeometryAndOrderingAcrossCultures()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            var first = Geometry(TrafficPatternSide.Left);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            var second = Geometry(TrafficPatternSide.Left);
            Assert.Equal(first, second);
            Assert.Equal(first.GetHashCode(), second.GetHashCode());
            Assert.Equal(Corridors(first), Corridors(second));
            Assert.Equal(PatternGeometryResult.Generate(first.Circuit, first.RunwayReference, first.Parameters),
                PatternGeometryResult.Generate(first.Circuit, first.RunwayReference, first.Parameters));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void StoredRunwayHeading_IsNotTheGeographicRotationAuthority()
    {
        var reference = Reference(); // Northward thresholds, intentionally unrelated to the heading.
        var circuit = new CircuitDefinition(new AirportIdentifier("TEST"), new RunwayEnd("01", new Heading(271)),
            TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Right), PatternAltitudeResolution.Unresolved);
        var geometry = PatternGeometryResult.Generate(circuit, reference, PatternGeometryValidationTests.Parameters()).Geometry!;
        Assert.Equal(geometry.RunwayReference.RunwayLength.Metres, geometry.RunwayCenterline.End.AlongTrackMetres);
        Close(0, reference.ForwardEast);
        Close(1, reference.ForwardNorth);
        Assert.Same(circuit.RunwayEnd, geometry.Circuit.RunwayEnd);
    }

    [Fact]
    public void Generation_RejectsNullAndUnrepresentableDerivedSegments()
    {
        var circuit = Circuit(TrafficPatternSide.Left);
        var reference = Reference();
        var parameters = PatternGeometryValidationTests.Parameters();
        Assert.Throws<ArgumentNullException>(() => PatternGeometryResult.Generate(null!, reference, parameters));
        Assert.Throws<ArgumentNullException>(() => PatternGeometryResult.Generate(circuit, null!, parameters));
        Assert.Throws<ArgumentNullException>(() => PatternGeometryResult.Generate(circuit, reference, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => PatternGeometryResult.Generate(circuit, reference,
            PatternGeometryValidationTests.Parameters(upwind: double.Epsilon)));
        Assert.Throws<ArgumentException>(() => PatternGeometryResult.Generate(circuit, reference,
            PatternGeometryValidationTests.Parameters(upwind: double.MaxValue)));
    }

    private static PatternGeometry Geometry(TrafficPatternSide side, PatternGeometryParameters? parameters = null) =>
        PatternGeometryResult.Generate(Circuit(side), Reference(), parameters ?? PatternGeometryValidationTests.Parameters()).Geometry!;

    private static CircuitDefinition Circuit(TrafficPatternSide side, string identifier = "01", PatternAltitudeResolution? altitude = null) =>
        new(new AirportIdentifier("TEST"), new RunwayEnd(identifier, new Heading(10)),
            side == TrafficPatternSide.Unknown ? TrafficPatternSideResolution.Unknown : TrafficPatternSideResolver.Resolve(published: side),
            altitude ?? PatternAltitudeResolution.Unresolved);

    private static RunwayGeometryReference Reference() => new(new AirportIdentifier("TEST"), " 01 ", new(0, 0), new(0.01, 0));
    private static LegCorridor[] Corridors(PatternGeometry geometry) =>
        [geometry.Upwind, geometry.Crosswind, geometry.Downwind, geometry.Final, geometry.Entry];

    private static void AssertSegment(HorizontalSegment segment, RunwayCoordinate start, RunwayCoordinate end)
    {
        Assert.Equal(start, segment.Start);
        Assert.Equal(end, segment.End);
    }

    private static void AssertRectangle(LegCorridor corridor, double halfWidth)
    {
        Assert.Equal(halfWidth, corridor.HalfWidth.Metres);
        var axis = corridor.Axis;
        var dx = axis.End.AlongTrackMetres - axis.Start.AlongTrackMetres;
        var dy = axis.End.CrossTrackMetres - axis.Start.CrossTrackMetres;
        foreach (var (point, endpoint) in new[] { (corridor.StartPlus, axis.Start), (corridor.StartMinus, axis.Start), (corridor.EndPlus, axis.End), (corridor.EndMinus, axis.End) })
        {
            var px = point.AlongTrackMetres - endpoint.AlongTrackMetres;
            var py = point.CrossTrackMetres - endpoint.CrossTrackMetres;
            Close(halfWidth, double.Hypot(px, py));
            Close(0, (dx * px + dy * py) / axis.Length.Metres);
        }
    }

    private static void Mirror(RunwayCoordinate left, RunwayCoordinate right)
    {
        Assert.Equal(left.AlongTrackMetres, right.AlongTrackMetres);
        Assert.Equal(-left.CrossTrackMetres, right.CrossTrackMetres);
    }

    private static void MirrorArea(HorizontalArea left, HorizontalArea right)
    {
        Assert.Equal(left.MinimumAlongTrackMetres, right.MinimumAlongTrackMetres);
        Assert.Equal(left.MaximumAlongTrackMetres, right.MaximumAlongTrackMetres);
        Assert.Equal(-left.MinimumCrossTrackMetres, right.MaximumCrossTrackMetres);
        Assert.Equal(-left.MaximumCrossTrackMetres, right.MinimumCrossTrackMetres);
    }

    // Sub-nanometre tolerance is sufficient for these metre/kilometre segment calculations.
    private static void Close(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0d, 1e-9d);
}
