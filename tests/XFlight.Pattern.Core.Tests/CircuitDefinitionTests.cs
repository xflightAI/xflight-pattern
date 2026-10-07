using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Circuits;

namespace XFlight.Pattern.Core.Tests;

public sealed class CircuitDefinitionTests
{
    [Theory]
    [InlineData(TrafficPatternSide.Left)]
    [InlineData(TrafficPatternSide.Right)]
    public void Definition_RetainsKnownSideAndSelectedRunway(TrafficPatternSide side)
    {
        var runway = new RunwayEnd("16", new Heading(160d));

        var definition = new CircuitDefinition(
            new AirportIdentifier("LIRF"),
            runway,
            TrafficPatternSideResolver.Resolve(userSelected: side),
            PatternAltitudeResolution.UserSelected(new PatternAltitudeMsl(1_850d)));

        Assert.Same(runway, definition.RunwayEnd);
        Assert.Equal("16", definition.RunwayEnd.Identifier);
        Assert.Equal(side, definition.TrafficPatternSide.Side);
    }

    [Fact]
    public void Definition_AllowsUnknownSideAndUnresolvedAltitudeDuringPreparation()
    {
        var definition = new CircuitDefinition(
            new AirportIdentifier("LIRF"),
            new RunwayEnd("16", new Heading(160d)),
            TrafficPatternSideResolution.Unknown,
            PatternAltitudeResolution.Unresolved);

        Assert.Equal(TrafficPatternSide.Unknown, definition.TrafficPatternSide.Side);
        Assert.False(definition.PatternAltitude.IsResolved);
    }

    [Fact]
    public void ReciprocalRunwayEnds_ProduceDifferentDefinitions()
    {
        var airport = new AirportIdentifier("LIRF");
        var altitude = PatternAltitudeResolution.TrainingDefault(new AirportElevationMsl(850d));
        var runway16 = new CircuitDefinition(
            airport,
            new RunwayEnd("16", new Heading(160d)),
            TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Left),
            altitude);
        var runway34 = new CircuitDefinition(
            airport,
            new RunwayEnd("34", new Heading(340d)),
            TrafficPatternSideResolver.Resolve(published: TrafficPatternSide.Right),
            altitude);

        Assert.NotEqual(runway16, runway34);
        Assert.Equal("16", runway16.RunwayEnd.Identifier);
        Assert.Equal("34", runway34.RunwayEnd.Identifier);
    }

    [Fact]
    public void Definition_PreservesDistinctMslAndAglValues()
    {
        var altitude = PatternAltitudeResolution.TrainingDefault(new AirportElevationMsl(850d));

        var definition = Definition(altitude);

        Assert.Equal(850d, definition.PatternAltitude.AirportElevationMsl!.Feet);
        Assert.Equal(1_000d, definition.PatternAltitude.OriginatingHeightAgl!.Feet);
        Assert.Equal(1_850d, definition.PatternAltitude.TargetMsl!.Feet);
    }

    [Fact]
    public void SameInput_ProducesEqualCircuitDefinition()
    {
        CircuitDefinition Create() => new(
            new AirportIdentifier("LIRF"),
            new RunwayEnd("16", new Heading(160d)),
            TrafficPatternSideResolver.Resolve(xFlightVerified: TrafficPatternSide.Left),
            PatternAltitudeResolver.Resolve(
                airportElevationMsl: new AirportElevationMsl(850d),
                useC172TrainingDefault: true));

        Assert.Equal(Create(), Create());
    }

    private static CircuitDefinition Definition(PatternAltitudeResolution altitude) =>
        new(
            new AirportIdentifier("LIRF"),
            new RunwayEnd("16", new Heading(160d)),
            TrafficPatternSideResolution.Unknown,
            altitude);
}
