namespace XFlight.Pattern.Core.Geometry;

/// <summary>Oriented finite segment in runway coordinates; start-to-end is the direction of travel.</summary>
public sealed record HorizontalSegment
{
    public HorizontalSegment(RunwayCoordinate start, RunwayCoordinate end)
    {
        var length = double.Hypot(end.AlongTrackMetres - start.AlongTrackMetres,
            end.CrossTrackMetres - start.CrossTrackMetres);
        Length = HorizontalDistance.FromMetres(length);
        Start = start;
        End = end;
    }

    public RunwayCoordinate Start { get; }
    public RunwayCoordinate End { get; }
    public HorizontalDistance Length { get; }
}
