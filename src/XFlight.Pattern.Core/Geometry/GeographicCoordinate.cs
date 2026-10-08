namespace XFlight.Pattern.Core.Geometry;

/// <summary>WGS84 latitude/longitude in degrees, without an altitude. +180 longitude canonicalizes to -180.</summary>
public sealed record GeographicCoordinate
{
    public GeographicCoordinate(double latitudeDegrees, double longitudeDegrees)
    {
        if (!double.IsFinite(latitudeDegrees) || latitudeDegrees < -90d || latitudeDegrees > 90d)
            throw new ArgumentOutOfRangeException(nameof(latitudeDegrees));
        if (!double.IsFinite(longitudeDegrees) || longitudeDegrees < -180d || longitudeDegrees > 180d)
            throw new ArgumentOutOfRangeException(nameof(longitudeDegrees));

        LatitudeDegrees = latitudeDegrees;
        LongitudeDegrees = longitudeDegrees == 180d ? -180d : longitudeDegrees;
    }

    public double LatitudeDegrees { get; }
    public double LongitudeDegrees { get; }
}
