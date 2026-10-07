using XFlight.Pattern.Core.Airports;

namespace XFlight.Pattern.Core.Circuits;

public sealed record CircuitDefinition
{
    public CircuitDefinition(
        AirportIdentifier airportIdentifier,
        RunwayEnd runwayEnd,
        TrafficPatternSideResolution trafficPatternSide,
        PatternAltitudeResolution patternAltitude)
    {
        ArgumentNullException.ThrowIfNull(airportIdentifier);
        ArgumentNullException.ThrowIfNull(runwayEnd);
        ArgumentNullException.ThrowIfNull(trafficPatternSide);
        ArgumentNullException.ThrowIfNull(patternAltitude);

        AirportIdentifier = airportIdentifier;
        RunwayEnd = runwayEnd;
        TrafficPatternSide = trafficPatternSide;
        PatternAltitude = patternAltitude;
    }

    public AirportIdentifier AirportIdentifier { get; }
    public RunwayEnd RunwayEnd { get; }
    public TrafficPatternSideResolution TrafficPatternSide { get; }
    public PatternAltitudeResolution PatternAltitude { get; }
}
