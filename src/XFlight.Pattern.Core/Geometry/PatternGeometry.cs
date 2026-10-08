using XFlight.Pattern.Core.Circuits;

namespace XFlight.Pattern.Core.Geometry;

/// <summary>Static horizontal training areas. Source M2 authority and altitude resolution remain intact.</summary>
public sealed record PatternGeometry
{
    internal PatternGeometry(CircuitDefinition circuit, RunwayGeometryReference runwayReference,
        PatternGeometryParameters parameters)
    {
        Circuit = circuit;
        RunwayReference = runwayReference;
        Parameters = parameters;
        var side = circuit.TrafficPatternSide.Side switch
        {
            TrafficPatternSide.Left => -1d,
            TrafficPatternSide.Right => 1d,
            _ => throw new ArgumentException("A known side is required.", nameof(circuit))
        };
        var length = runwayReference.RunwayLength.Metres;
        var upwindEnd = length + parameters.UpwindExtension.Metres;
        var downwindCross = side * parameters.DownwindOffset.Metres;
        var nearer = parameters.NearerBaseTurnDistance.Metres;
        var farther = parameters.FartherBaseTurnDistance.Metres;
        var halfWidth = parameters.CorridorHalfWidth;

        RunwayCenterline = new(new(0d, 0d), new(length, 0d));
        Upwind = Corridor(new(length, 0d), new(upwindEnd, 0d));
        Crosswind = Corridor(new(upwindEnd, 0d), new(upwindEnd, downwindCross));
        Downwind = Corridor(new(upwindEnd, downwindCross), new(-farther, downwindCross));
        BaseTurnWindow = new(-farther, -nearer, downwindCross - halfWidth.Metres, downwindCross + halfWidth.Metres);
        // All configured base positions are permitted; no actual turn point is selected.
        BaseArea = new(-farther, -nearer,
            Math.Min(0d, downwindCross) - halfWidth.Metres,
            Math.Max(0d, downwindCross) + halfWidth.Metres);
        Final = Corridor(new(-parameters.FinalExtension.Metres, 0d), new(0d, 0d));
        EntryJoinTarget = new(length / 2d, downwindCross);
        var entryComponent = parameters.EntryLength.Metres / Math.Sqrt(2d);
        Entry = Corridor(new(EntryJoinTarget.AlongTrackMetres + entryComponent,
            downwindCross + side * entryComponent), EntryJoinTarget);
        if (Math.Abs(Entry.Axis.Start.CrossTrackMetres) <= parameters.DownwindOffset.Metres + halfWidth.Metres)
            throw new ArgumentException("Entry start must remain outside the circuit at the configured coordinate scale.");

        LegCorridor Corridor(RunwayCoordinate start, RunwayCoordinate end) => new(new(start, end), halfWidth);
    }

    public CircuitDefinition Circuit { get; }
    public RunwayGeometryReference RunwayReference { get; }
    public PatternGeometryParameters Parameters { get; }
    public HorizontalSegment RunwayCenterline { get; }
    public LegCorridor Upwind { get; }
    public LegCorridor Crosswind { get; }
    public LegCorridor Downwind { get; }
    public HorizontalArea BaseTurnWindow { get; }
    /// <summary>Travel is from the selected-side downwind edge toward CrossTrack zero, at any window position.</summary>
    public HorizontalArea BaseArea { get; }
    public LegCorridor Final { get; }
    public LegCorridor Entry { get; }
    public RunwayCoordinate EntryJoinTarget { get; }
}
