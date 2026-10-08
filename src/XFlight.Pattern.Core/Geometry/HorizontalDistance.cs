namespace XFlight.Pattern.Core.Geometry;

/// <summary>A finite, strictly positive horizontal distance. No training defaults are implied.</summary>
public sealed record HorizontalDistance
{
    private HorizontalDistance(double metres)
    {
        if (!double.IsFinite(metres) || metres <= 0d)
            throw new ArgumentOutOfRangeException(nameof(metres), "Distance must be finite and positive.");
        Metres = metres;
    }

    public double Metres { get; }
    public static HorizontalDistance FromMetres(double metres) => new(metres);
    public static HorizontalDistance FromNauticalMiles(double nauticalMiles) => new(nauticalMiles * 1_852d);
}
