namespace XFlight.Pattern.Core.Airports;

public readonly record struct Heading
{
    public Heading(double degrees)
    {
        if (!double.IsFinite(degrees) || degrees < 0d || degrees > 360d)
            throw new ArgumentOutOfRangeException(nameof(degrees), "Heading must be finite and between 0 and 360 degrees inclusive.");

        Degrees = degrees == 360d ? 0d : degrees;
    }

    public double Degrees { get; }
    public override string ToString() => $"{Degrees:0.###}°";
}
