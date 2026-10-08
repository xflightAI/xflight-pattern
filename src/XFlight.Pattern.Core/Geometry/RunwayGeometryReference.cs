using XFlight.Pattern.Core.Airports;

namespace XFlight.Pattern.Core.Geometry;

/// <summary>Selected thresholds establish a local horizontal runway frame, independently of stored heading.</summary>
public sealed record RunwayGeometryReference
{
    // A millimetre is effectively zero at airport scale and exceeds ECEF subtraction roundoff.
    private const double MinimumRunwayLengthMetres = 0.001d;

    public RunwayGeometryReference(
        AirportIdentifier airportIdentifier,
        string runwayEndIdentifier,
        GeographicCoordinate landingThreshold,
        GeographicCoordinate oppositeThreshold)
    {
        ArgumentNullException.ThrowIfNull(airportIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(runwayEndIdentifier);
        ArgumentNullException.ThrowIfNull(landingThreshold);
        ArgumentNullException.ThrowIfNull(oppositeThreshold);
        AirportIdentifier = airportIdentifier;
        RunwayEndIdentifier = runwayEndIdentifier.Trim().ToUpperInvariant();
        LandingThreshold = landingThreshold;
        OppositeThreshold = oppositeThreshold;
        LocalFrame = new(landingThreshold);
        var opposite = LocalFrame.ToLocal(oppositeThreshold);
        var length = double.Hypot(opposite.EastMetres, opposite.NorthMetres);
        if (!double.IsFinite(length) || length <= MinimumRunwayLengthMetres)
            throw new ArgumentException("Thresholds must establish a nonzero horizontal runway axis.", nameof(oppositeThreshold));
        RunwayLength = HorizontalDistance.FromMetres(length);
        ForwardEast = opposite.EastMetres / length;
        ForwardNorth = opposite.NorthMetres / length;
    }

    public AirportIdentifier AirportIdentifier { get; }
    public string RunwayEndIdentifier { get; }
    public GeographicCoordinate LandingThreshold { get; }
    public GeographicCoordinate OppositeThreshold { get; }
    public LocalHorizontalFrame LocalFrame { get; }
    public HorizontalDistance RunwayLength { get; }
    public double ForwardEast { get; }
    public double ForwardNorth { get; }

    public RunwayCoordinate ToRunway(GeographicCoordinate position) => ToRunway(LocalFrame.ToLocal(position));

    public RunwayCoordinate ToRunway(LocalEnuCoordinate position) => new(
        position.EastMetres * ForwardEast + position.NorthMetres * ForwardNorth,
        position.EastMetres * ForwardNorth - position.NorthMetres * ForwardEast);
}
