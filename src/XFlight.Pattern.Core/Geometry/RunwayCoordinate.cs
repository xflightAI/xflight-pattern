namespace XFlight.Pattern.Core.Geometry;

/// <summary>Metres along the runway's forward axis and to its right; origin is the landing threshold.</summary>
public readonly record struct RunwayCoordinate
{
    public RunwayCoordinate(double alongTrackMetres, double crossTrackMetres)
    {
        if (!double.IsFinite(alongTrackMetres))
            throw new ArgumentOutOfRangeException(nameof(alongTrackMetres));
        if (!double.IsFinite(crossTrackMetres))
            throw new ArgumentOutOfRangeException(nameof(crossTrackMetres));
        AlongTrackMetres = alongTrackMetres;
        CrossTrackMetres = crossTrackMetres;
    }

    public double AlongTrackMetres { get; }
    public double CrossTrackMetres { get; }
}
