namespace XFlight.Pattern.Core.Geometry;

/// <summary>Closed rectangle extending laterally by HalfWidth from an oriented axis, with flat ends.</summary>
public sealed record LegCorridor
{
    public LegCorridor(HorizontalSegment axis, HorizontalDistance halfWidth)
    {
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(halfWidth);
        Axis = axis;
        HalfWidth = halfWidth;
        var along = (axis.End.AlongTrackMetres - axis.Start.AlongTrackMetres) / axis.Length.Metres;
        var cross = (axis.End.CrossTrackMetres - axis.Start.CrossTrackMetres) / axis.Length.Metres;
        var alongOffset = -cross * halfWidth.Metres;
        var crossOffset = along * halfWidth.Metres;
        StartPlus = Offset(axis.Start, alongOffset, crossOffset);
        EndPlus = Offset(axis.End, alongOffset, crossOffset);
        EndMinus = Offset(axis.End, -alongOffset, -crossOffset);
        StartMinus = Offset(axis.Start, -alongOffset, -crossOffset);
        if (StartPlus == StartMinus || EndPlus == EndMinus || StartPlus == EndPlus || StartMinus == EndMinus)
            throw new ArgumentException("Corridor length and width must remain nonzero at the configured coordinate scale.");
    }

    public HorizontalSegment Axis { get; }
    public HorizontalDistance HalfWidth { get; }
    // Fixed winding: start-plus, end-plus, end-minus, start-minus. No mutable collection.
    public RunwayCoordinate StartPlus { get; }
    public RunwayCoordinate EndPlus { get; }
    public RunwayCoordinate EndMinus { get; }
    public RunwayCoordinate StartMinus { get; }

    private static RunwayCoordinate Offset(RunwayCoordinate point, double along, double cross) =>
        new(point.AlongTrackMetres + along, point.CrossTrackMetres + cross);
}
