using XFlight.Pattern.Core.Circuits;

namespace XFlight.Pattern.Core.Geometry;

public enum PatternGeometryUnavailableReason
{
    UnknownTrafficPatternSide
}

public sealed record PatternGeometryResult
{
    private PatternGeometryResult(CircuitDefinition circuit, RunwayGeometryReference runwayReference,
        PatternGeometryParameters parameters, PatternGeometry? geometry, PatternGeometryUnavailableReason? unavailableReason)
    {
        Circuit = circuit;
        RunwayReference = runwayReference;
        Parameters = parameters;
        Geometry = geometry;
        UnavailableReason = unavailableReason;
    }

    public CircuitDefinition Circuit { get; }
    public RunwayGeometryReference RunwayReference { get; }
    public PatternGeometryParameters Parameters { get; }
    public PatternGeometry? Geometry { get; }
    public PatternGeometryUnavailableReason? UnavailableReason { get; }
    public bool IsAvailable => Geometry is not null;

    public static PatternGeometryResult Generate(CircuitDefinition circuit,
        RunwayGeometryReference runwayReference, PatternGeometryParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(runwayReference);
        ArgumentNullException.ThrowIfNull(parameters);
        if (circuit.AirportIdentifier != runwayReference.AirportIdentifier ||
            !StringComparer.Ordinal.Equals(circuit.RunwayEnd.Identifier, runwayReference.RunwayEndIdentifier))
            throw new ArgumentException("Runway geometry must identify the circuit's airport and selected runway end.", nameof(runwayReference));

        if (circuit.TrafficPatternSide.Side == TrafficPatternSide.Unknown)
            return new(circuit, runwayReference, parameters, null, PatternGeometryUnavailableReason.UnknownTrafficPatternSide);

        return new(circuit, runwayReference, parameters, new(circuit, runwayReference, parameters), null);
    }
}
