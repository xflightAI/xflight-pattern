namespace XFlight.Pattern.Core.Airports;

public sealed class Airport
{
    private readonly IReadOnlyList<RunwayEnd> _runwayEnds;

    public Airport(AirportIdentifier identifier, IEnumerable<RunwayEnd> runwayEnds)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(runwayEnds);
        var materialized = runwayEnds.ToArray();

        if (materialized.Any(static end => end is null))
            throw new ArgumentException("Runway ends cannot contain null values.", nameof(runwayEnds));

        if (materialized.Select(static end => end.Identifier).Distinct(StringComparer.Ordinal).Count() != materialized.Length)
            throw new ArgumentException("Runway end identifiers must be unique within an airport.", nameof(runwayEnds));

        Identifier = identifier;
        _runwayEnds = Array.AsReadOnly(materialized);
    }

    public AirportIdentifier Identifier { get; }
    public IReadOnlyList<RunwayEnd> RunwayEnds => _runwayEnds;
}
