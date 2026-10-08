namespace XFlight.Pattern.Core.Geometry;

/// <summary>Closed rectangular bounds in runway coordinates, in metres. No aircraft membership decision.</summary>
public sealed record HorizontalArea
{
    public HorizontalArea(double minimumAlongTrackMetres, double maximumAlongTrackMetres,
        double minimumCrossTrackMetres, double maximumCrossTrackMetres)
    {
        // Coordinate constructors reject non-finite values before checking strict ordering.
        _ = new RunwayCoordinate(minimumAlongTrackMetres, minimumCrossTrackMetres);
        _ = new RunwayCoordinate(maximumAlongTrackMetres, maximumCrossTrackMetres);
        if (minimumAlongTrackMetres >= maximumAlongTrackMetres || minimumCrossTrackMetres >= maximumCrossTrackMetres)
            throw new ArgumentException("Area must have nonzero, ordered bounds on both axes.");
        if (!double.IsFinite(maximumAlongTrackMetres - minimumAlongTrackMetres) ||
            !double.IsFinite(maximumCrossTrackMetres - minimumCrossTrackMetres))
            throw new ArgumentException("Area dimensions must be finite.");
        MinimumAlongTrackMetres = minimumAlongTrackMetres;
        MaximumAlongTrackMetres = maximumAlongTrackMetres;
        MinimumCrossTrackMetres = minimumCrossTrackMetres;
        MaximumCrossTrackMetres = maximumCrossTrackMetres;
    }

    public double MinimumAlongTrackMetres { get; }
    public double MaximumAlongTrackMetres { get; }
    public double MinimumCrossTrackMetres { get; }
    public double MaximumCrossTrackMetres { get; }
}
