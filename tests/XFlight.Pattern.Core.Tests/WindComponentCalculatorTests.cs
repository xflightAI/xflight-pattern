using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.RunwayRecommendations;
using XFlight.Pattern.Core.Weather;

namespace XFlight.Pattern.Core.Tests;

public sealed class WindComponentCalculatorTests
{
    private static readonly WeatherSourceIdentity Source = WeatherSourceIdentity.Manual;

    [Theory]
    [InlineData(180d, 180d, 10d, 0d, 0d, LongitudinalWind.Headwind)]
    [InlineData(180d, 360d, 0d, 10d, 0d, LongitudinalWind.Tailwind)]
    [InlineData(180d, 270d, 0d, 0d, 10d, LongitudinalWind.Neutral)]
    public void Evaluate_CardinalRelationships_ReturnsExpectedComponents(
        double runwayHeading,
        double windDirection,
        double expectedHeadwind,
        double expectedTailwind,
        double expectedCrosswind,
        LongitudinalWind expectedLongitudinalWind)
    {
        var result = Evaluate(runwayHeading, windDirection, 10d);

        Assert.Equal(expectedHeadwind, result.HeadwindKnots, 10);
        Assert.Equal(expectedTailwind, result.TailwindKnots, 10);
        Assert.Equal(expectedCrosswind, result.CrosswindKnots, 10);
        Assert.Equal(expectedLongitudinalWind, result.LongitudinalWind);
    }

    [Fact]
    public void Evaluate_FortyFiveDegreeOffset_UsesTrigonometricComponents()
    {
        var result = Evaluate(180d, 225d, 10d);
        var expected = 10d / Math.Sqrt(2d);

        Assert.Equal(45d, result.AngularDifferenceDegrees, 10);
        Assert.Equal(expected, result.HeadwindKnots, 10);
        Assert.Equal(expected, result.CrosswindKnots, 10);
        Assert.Equal(0d, result.TailwindKnots);
    }

    [Fact]
    public void Evaluate_AcrossNorth_UsesMinimumAngularDifference()
    {
        var result = Evaluate(350d, 10d, 10d);

        Assert.Equal(20d, result.AngularDifferenceDegrees, 10);
        Assert.Equal(10d * Math.Cos(20d * Math.PI / 180d), result.HeadwindKnots, 10);
    }

    [Fact]
    public void Evaluate_NonDirectionalWind_IsRejected()
    {
        var runway = new RunwayEnd("18", new Heading(180d));

        Assert.Throws<ArgumentException>(() => WindComponentCalculator.Evaluate(runway, WindObservation.Calm(Source)));
    }

    private static RunwayEndWindEvaluation Evaluate(double runwayHeading, double windDirection, double speed) =>
        WindComponentCalculator.Evaluate(
            new RunwayEnd("TEST", new Heading(runwayHeading)),
            WindObservation.Directional(new Heading(windDirection), speed, Source));
}
