namespace XFlight.Pattern.Core.Geometry;

/// <summary>Horizontal coordinates in metres in the origin's East/North tangent plane.</summary>
public readonly record struct LocalEnuCoordinate
{
    public LocalEnuCoordinate(double eastMetres, double northMetres)
    {
        if (!double.IsFinite(eastMetres))
            throw new ArgumentOutOfRangeException(nameof(eastMetres));
        if (!double.IsFinite(northMetres))
            throw new ArgumentOutOfRangeException(nameof(northMetres));
        EastMetres = eastMetres;
        NorthMetres = northMetres;
    }

    public double EastMetres { get; }
    public double NorthMetres { get; }
}
