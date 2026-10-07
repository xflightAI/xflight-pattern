using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;
using XFlight.Pattern.Core.RunwayRecommendations;
using XFlight.Pattern.Core.Weather;

namespace XFlight.Pattern.Core.Tests;

public sealed class TrafficPatternSideTests
{
    [Fact]
    public void ReciprocalRunwayEnds_RetainIndependentSides()
    {
        var airport = new AirportIdentifier("LIRF");
        var runway16 = new RunwayEnd("16", new Heading(160d));
        var runway34 = new RunwayEnd("34", new Heading(340d));

        var circuit16 = Circuit(airport, runway16, TrafficPatternSide.Left);
        var circuit34 = Circuit(airport, runway34, TrafficPatternSide.Right);

        Assert.Equal(TrafficPatternSide.Left, circuit16.TrafficPatternSide.Side);
        Assert.Equal(TrafficPatternSide.Right, circuit34.TrafficPatternSide.Side);
        Assert.NotEqual(circuit16.RunwayEnd, circuit34.RunwayEnd);
    }

    [Fact]
    public void Resolve_NoKnownCandidate_RemainsUnknown()
    {
        var result = TrafficPatternSideResolver.Resolve();

        Assert.Equal(TrafficPatternSide.Unknown, result.Side);
        Assert.Equal(TrafficPatternSideProvenance.Unknown, result.Provenance);
    }

    [Fact]
    public void WindRecommendation_DoesNotAlterRunwayEndSide()
    {
        var runway16 = new RunwayEnd("16", new Heading(160d));
        var runway34 = new RunwayEnd("34", new Heading(340d));
        var airport = new Airport(new AirportIdentifier("LIRF"), [runway16, runway34]);
        var side = TrafficPatternSideResolver.Resolve(userSelected: TrafficPatternSide.Right);

        var recommendation = RunwayRecommendationService.Recommend(
            airport,
            WindObservation.Directional(new Heading(160d), 10d, WeatherSourceIdentity.Manual));

        Assert.Equal(runway16, recommendation.RecommendedRunwayEnd);
        Assert.Equal(TrafficPatternSide.Right, side.Side);
        Assert.Equal(TrafficPatternSideProvenance.UserSelected, side.Provenance);
    }

    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void Resolve_UserSelection_RetainsUserSelectedProvenance(TrafficPatternSide selected)
    {
        var result = TrafficPatternSideResolver.Resolve(userSelected: selected);

        Assert.Equal(selected, result.Side);
        Assert.Equal(TrafficPatternSideProvenance.UserSelected, result.Provenance);
    }

    [Fact]
    public void Resolve_PublishedAndVerifiedValues_RetainTheirProvenance()
    {
        var published = TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Left);
        var verified = TrafficPatternSideResolver.Resolve(xFlightVerified: TrafficPatternSide.Right);

        Assert.Equal(TrafficPatternSideProvenance.Published, published.Provenance);
        Assert.Equal(TrafficPatternSideProvenance.XFlightVerified, verified.Provenance);
    }

    [Fact]
    public void Resolution_InvalidSideProvenanceCombinations_AreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new TrafficPatternSideResolution(TrafficPatternSide.Unknown, TrafficPatternSideProvenance.Published));
        Assert.Throws<ArgumentException>(() =>
            new TrafficPatternSideResolution(TrafficPatternSide.Left, TrafficPatternSideProvenance.Unknown));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Unknown));
    }

    [Fact]
    public void Resolve_UsesPublishedThenVerifiedThenUserSelectedPrecedence()
    {
        var allCandidates = TrafficPatternSideResolver.Resolve(
            published: TrafficPatternSide.Right,
            xFlightVerified: TrafficPatternSide.Left,
            userSelected: TrafficPatternSide.Left);
        var withoutPublished = TrafficPatternSideResolver.Resolve(
            xFlightVerified: TrafficPatternSide.Right,
            userSelected: TrafficPatternSide.Left);

        Assert.Equal(
            new TrafficPatternSideResolution(TrafficPatternSide.Right, TrafficPatternSideProvenance.Published),
            allCandidates);
        Assert.Equal(
            new TrafficPatternSideResolution(TrafficPatternSide.Right, TrafficPatternSideProvenance.XFlightVerified),
            withoutPublished);
    }

    [Fact]
    public void Resolve_RepeatedInput_ProducesEqualResolution()
    {
        TrafficPatternSideResolution Resolve() => TrafficPatternSideResolver.Resolve(
            xFlightVerified: TrafficPatternSide.Left,
            userSelected: TrafficPatternSide.Right);

        Assert.Equal(Resolve(), Resolve());
    }

    private static CircuitDefinition Circuit(
        AirportIdentifier airport,
        RunwayEnd runwayEnd,
        TrafficPatternSide side) =>
        new(
            airport,
            runwayEnd,
            TrafficPatternSideResolver.Resolve(published: side),
            PatternAltitudeResolution.Unresolved);
}
