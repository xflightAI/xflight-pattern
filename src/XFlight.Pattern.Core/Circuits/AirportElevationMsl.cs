namespace XFlight.Pattern.Core.Circuits;

public sealed record AirportElevationMsl
{
    public AirportElevationMsl(double feet)
    {
        if (!double.IsFinite(feet))
            throw new ArgumentOutOfRangeException(nameof(feet), "Airport elevation must be finite.");

        Feet = feet;
    }

    public double Feet { get; }
}
