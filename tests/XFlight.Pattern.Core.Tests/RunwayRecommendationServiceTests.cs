using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.RunwayRecommendations;
using XFlight.Pattern.Core.Weather;

namespace XFlight.Pattern.Core.Tests;

public sealed class RunwayRecommendationServiceTests
{
    private static readonly WeatherSourceIdentity Source = WeatherSourceIdentity.Manual;

    [Fact]
    public void Recommend_ReciprocalEnds_PrefersDirectHeadwind()
    {
        var airport = AirportWith(new("18", new Heading(180d)), new("36", new Heading(360d)));

        var result = RunwayRecommendationService.Recommend(airport, DirectionalWind(180d));

        Assert.Equal(RunwayRecommendationOutcome.RecommendedByWind, result.Outcome);
        Assert.Equal("18", result.RecommendedRunwayEnd!.Identifier);
        Assert.Equal(10d, result.RankedCandidates[0].HeadwindKnots, 10);
        Assert.Equal(10d, result.RankedCandidates[1].TailwindKnots, 10);
    }

    [Fact]
    public void Recommend_MultipleRunways_RanksBySignedLongitudinalComponent()
    {
        var airport = AirportWith(
            new("09", new Heading(90d)),
            new("18", new Heading(180d)),
            new("27", new Heading(270d)),
            new("36", new Heading(360d)));

        var result = RunwayRecommendationService.Recommend(airport, DirectionalWind(160d));

        Assert.Equal(new[] { "18", "09", "27", "36" }, result.RankedCandidates.Select(x => x.RunwayEnd.Identifier));
    }

    [Fact]
    public void Recommend_EqualWindGeometry_UsesIdentifierAsDeterministicTieBreak()
    {
        var airport = AirportWith(new("B", new Heading(90d)), new("A", new Heading(270d)));

        var result = RunwayRecommendationService.Recommend(airport, DirectionalWind(180d));

        Assert.Equal(RunwayRecommendationOutcome.RecommendedByDeterministicTieBreak, result.Outcome);
        Assert.Equal(new[] { "A", "B" }, result.RankedCandidates.Select(x => x.RunwayEnd.Identifier));
        Assert.Equal("A", result.RecommendedRunwayEnd!.Identifier);
    }

    [Fact]
    public void Recommend_CalmWind_ReturnsNoWindPreferredRunway()
    {
        AssertNoWindPreference(WindObservation.Calm(Source));
    }

    [Fact]
    public void Recommend_VariableWind_ReturnsNoWindPreferredRunway()
    {
        AssertNoWindPreference(WindObservation.Variable(8d, Source));
    }

    [Fact]
    public void Recommend_UnavailableWind_ReturnsNoWindPreferredRunway()
    {
        AssertNoWindPreference(WindObservation.Unavailable(Source));
    }

    [Fact]
    public void Recommend_UsesExactHeading_NotRunwayNumberTimesTen()
    {
        var airport = AirportWith(
            new("18", new Heading(170d)),
            new("36", new Heading(350d)));

        var result = RunwayRecommendationService.Recommend(airport, DirectionalWind(170d));

        Assert.Equal("18", result.RecommendedRunwayEnd!.Identifier);
        Assert.Equal(0d, result.RankedCandidates[0].AngularDifferenceDegrees);
        Assert.Equal(10d, result.RankedCandidates[0].HeadwindKnots);
    }

    [Fact]
    public void Recommend_RepeatedExecution_ProducesSameOrderingAndOutcome()
    {
        var airport = AirportWith(
            new("22", new Heading(218.4d)),
            new("04", new Heading(38.4d)),
            new("13", new Heading(128.7d)));
        var wind = DirectionalWind(205d);

        var first = RunwayRecommendationService.Recommend(airport, wind);

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var repeated = RunwayRecommendationService.Recommend(airport, wind);
            Assert.Equal(first.Outcome, repeated.Outcome);
            Assert.Equal(first.RecommendedRunwayEnd, repeated.RecommendedRunwayEnd);
            Assert.Equal(first.RankedCandidates, repeated.RankedCandidates);
        }
    }

    [Fact]
    public void Recommend_NoUsableEnds_ThrowsDefinedException()
    {
        var airport = AirportWith(new RunwayEnd("18", new Heading(180d), isUsable: false));

        Assert.Throws<InvalidOperationException>(() => RunwayRecommendationService.Recommend(airport, DirectionalWind(180d)));
    }

    private static void AssertNoWindPreference(WindObservation wind)
    {
        var result = RunwayRecommendationService.Recommend(
            AirportWith(new RunwayEnd("18", new Heading(180d))),
            wind);

        Assert.Equal(RunwayRecommendationOutcome.NoWindPreferredRunway, result.Outcome);
        Assert.Null(result.RecommendedRunwayEnd);
        Assert.Empty(result.RankedCandidates);
    }

    private static Airport AirportWith(params RunwayEnd[] ends) => new(new AirportIdentifier("LIRF"), ends);

    private static WindObservation DirectionalWind(double direction) =>
        WindObservation.Directional(new Heading(direction), 10d, Source);
}
