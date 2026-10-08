namespace XFlight.Pattern.Core.Geometry;

/// <summary>WGS84 ECEF to horizontal ENU at a fixed origin. Both positions use ellipsoid height zero.</summary>
public sealed record LocalHorizontalFrame
{
    private const double SemiMajorAxisMetres = 6_378_137d;
    private const double Flattening = 1d / 298.257223563d;
    private const double EccentricitySquared = Flattening * (2d - Flattening);

    public LocalHorizontalFrame(GeographicCoordinate origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
    }

    public GeographicCoordinate Origin { get; }

    public LocalEnuCoordinate ToLocal(GeographicCoordinate position)
    {
        ArgumentNullException.ThrowIfNull(position);
        var origin = ToEcef(Origin);
        var point = ToEcef(position);
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var dz = point.Z - origin.Z;
        var latitude = Origin.LatitudeDegrees * (Math.PI / 180d);
        var longitude = Origin.LongitudeDegrees * (Math.PI / 180d);
        var east = -Math.Sin(longitude) * dx + Math.Cos(longitude) * dy;
        var north = -Math.Sin(latitude) * Math.Cos(longitude) * dx
            - Math.Sin(latitude) * Math.Sin(longitude) * dy + Math.Cos(latitude) * dz;
        return new(east, north);
    }

    private static (double X, double Y, double Z) ToEcef(GeographicCoordinate position)
    {
        var latitude = position.LatitudeDegrees * (Math.PI / 180d);
        var longitude = position.LongitudeDegrees * (Math.PI / 180d);
        var sinLatitude = Math.Sin(latitude);
        var radius = SemiMajorAxisMetres / Math.Sqrt(1d - EccentricitySquared * sinLatitude * sinLatitude);
        return (
            radius * Math.Cos(latitude) * Math.Cos(longitude),
            radius * Math.Cos(latitude) * Math.Sin(longitude),
            radius * (1d - EccentricitySquared) * sinLatitude);
    }
}
