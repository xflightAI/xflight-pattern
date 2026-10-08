using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Geometry;

namespace XFlight.Pattern.Core.Tests;

public sealed class GeographicGeometryTests
{
    [Fact]
    public void GeographicCoordinates_ValidateRangesAndCanonicalizeLongitudeBoundary()
    {
        foreach (var latitude in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -90.001d, 90.001d })
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeographicCoordinate(latitude, 0d));
        foreach (var longitude in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -180.001d, 180.001d })
            Assert.Throws<ArgumentOutOfRangeException>(() => new GeographicCoordinate(0d, longitude));
        Assert.Equal(new GeographicCoordinate(90d, -180d), new GeographicCoordinate(90d, 180d));
        Assert.Equal(-90d, new GeographicCoordinate(-90d, 0d).LatitudeDegrees);
    }

    [Fact]
    public void HorizontalCoordinates_RejectNonfiniteComponents()
    {
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocalEnuCoordinate(invalid, 0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LocalEnuCoordinate(0d, invalid));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunwayCoordinate(invalid, 0d));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RunwayCoordinate(0d, invalid));
        }
        Assert.Equal(new RunwayCoordinate(0d, 0d), default);
    }

    [Theory]
    [InlineData(0.01d, 0d, 1d, 0d)] // North: right is East.
    [InlineData(0d, 0.01d, 0d, -1d)] // East: right is South.
    [InlineData(-0.01d, 0d, -1d, 0d)] // South: right is West.
    [InlineData(0d, -0.01d, 0d, 1d)] // West: right is North.
    public void CardinalThresholdAxes_HaveRightPositiveCrossTrack(
        double latitude, double longitude, double rightEast, double rightNorth)
    {
        var runway = Reference(new(0d, 0d), new(latitude, longitude));
        Assert.Equal(new RunwayCoordinate(0d, 0d), runway.ToRunway(runway.LandingThreshold));
        var opposite = runway.ToRunway(runway.OppositeThreshold);
        Assert.True(opposite.AlongTrackMetres > 1_100d);
        Close(runway.RunwayLength.Metres, opposite.AlongTrackMetres);
        Close(0d, opposite.CrossTrackMetres);
        var right = runway.ToRunway(new LocalEnuCoordinate(rightEast * 100d, rightNorth * 100d));
        Close(0d, right.AlongTrackMetres);
        Close(100d, right.CrossTrackMetres);
        var left = runway.ToRunway(new LocalEnuCoordinate(-rightEast * 100d, -rightNorth * 100d));
        Close(-100d, left.CrossTrackMetres);
    }

    [Fact]
    public void RotatedRunway_UsesWgs84AndDeterministicDotProducts()
    {
        var runway = Reference(new(0d, 0d), new(0.01d, 0.02d));
        // Independent analytic equatorial ENU expectations from the WGS84 ellipsoid.
        const double a = 6_378_137d;
        const double inverseFlattening = 298.257223563d;
        var f = 1d / inverseFlattening;
        var e2 = f * (2d - f);
        var latitude = 0.01d * Math.PI / 180d;
        var longitude = 0.02d * Math.PI / 180d;
        var radius = a / Math.Sqrt(1d - e2 * Math.Pow(Math.Sin(latitude), 2d));
        var east = radius * Math.Cos(latitude) * Math.Sin(longitude);
        var north = radius * (1d - e2) * Math.Sin(latitude);
        var length = Math.Sqrt(east * east + north * north);
        Close(east, runway.LocalFrame.ToLocal(runway.OppositeThreshold).EastMetres);
        Close(north, runway.LocalFrame.ToLocal(runway.OppositeThreshold).NorthMetres);
        Close(length, runway.RunwayLength.Metres);
        var coordinate = new LocalEnuCoordinate(100d, 300d);
        var result = runway.ToRunway(coordinate);
        Close((100d * east + 300d * north) / length, result.AlongTrackMetres);
        Close((100d * north - 300d * east) / length, result.CrossTrackMetres);
        Assert.Equal(result, runway.ToRunway(coordinate));
        Close(0d, runway.ToRunway(runway.OppositeThreshold).CrossTrackMetres);
    }

    [Fact]
    public void MidlatitudeOrigin_ProjectsInItsOwnTangentPlane()
    {
        var origin = new GeographicCoordinate(45d, 12d);
        var frame = new LocalHorizontalFrame(origin);
        Assert.Equal(new LocalEnuCoordinate(0d, 0d), frame.ToLocal(origin));
        var east = frame.ToLocal(new(45d, 12.001d));
        var north = frame.ToLocal(new(45.001d, 12d));
        // WGS84 radii of curvature at 45 degrees, for these small angular displacements.
        Assert.InRange(east.EastMetres, 78.8468d, 78.8469d);
        Assert.InRange(north.NorthMetres, 111.1317d, 111.1319d);
        Assert.InRange(Math.Abs(north.EastMetres), 0d, 1e-9d);
    }

    [Fact]
    public void Antimeridian_UsesShortContinuousEcefSeparation()
    {
        var runway = Reference(new(0d, 179.999d), new(0d, -179.999d));
        Assert.InRange(runway.RunwayLength.Metres, 222.6389d, 222.6391d);
        Close(1d, runway.ForwardEast);
        Close(0d, runway.ForwardNorth);
    }

    [Fact]
    public void Thresholds_RejectCoincidentAndEffectivelyZeroHorizontalSeparation()
    {
        Assert.Throws<ArgumentException>(() => Reference(new(45d, 12d), new(45d, 12d)));
        Assert.Throws<ArgumentException>(() => Reference(new(0d, 0d), new(0d, 1e-10d)));
        Assert.Throws<ArgumentException>(() => Reference(new(90d, 0d), new(90d, 90d)));
        Assert.Throws<ArgumentException>(() => Reference(new(0d, 180d), new(0d, -180d)));
    }

    private static RunwayGeometryReference Reference(GeographicCoordinate landing, GeographicCoordinate opposite) =>
        new(new AirportIdentifier("TEST"), "01", landing, opposite);

    // Nanometre-scale numerical tolerance for analytic dot products at kilometre scale.
    private static void Close(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0d, 1e-9d);
}
