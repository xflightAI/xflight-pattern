using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Weather;

namespace XFlight.Pattern.Core.Tests;

public sealed class DomainValidationTests
{
    [Theory]
    [InlineData(-0.01d)]
    [InlineData(360.01d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Heading_InvalidValue_IsRejected(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Heading(value));
    }

    [Fact]
    public void Heading_ThreeSixty_IsCanonicalNorth()
    {
        Assert.Equal(0d, new Heading(360d).Degrees);
    }

    [Fact]
    public void WindObservation_NegativeSpeed_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindObservation.Directional(new Heading(180d), -1d, WeatherSourceIdentity.Manual));
    }

    [Fact]
    public void WindObservation_GustBelowSustainedSpeed_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            WindObservation.Directional(new Heading(180d), 10d, WeatherSourceIdentity.Manual, gustKnots: 9d));
    }

    [Fact]
    public void Airport_DuplicateRunwayEndIdentifier_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Airport(
            new AirportIdentifier("LIRF"),
            [new RunwayEnd("18", new Heading(180d)), new RunwayEnd("18", new Heading(181d))]));
    }

    [Fact]
    public void WindObservation_PreservesSourceGustAndTimestamp()
    {
        var timestamp = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

        var wind = WindObservation.Directional(new Heading(175d), 12d, WeatherSourceIdentity.ActiveSky, 18d, timestamp);

        Assert.Equal(WeatherSourceIdentity.ActiveSky, wind.Source);
        Assert.Equal(18d, wind.GustKnots);
        Assert.Equal(timestamp, wind.ObservedAt);
    }
}
