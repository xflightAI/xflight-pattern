namespace XFlight.Pattern.Core.Circuits;

public sealed record PatternHeightAgl
{
    public PatternHeightAgl(double feet)
    {
        if (!double.IsFinite(feet) || feet <= 0d)
            throw new ArgumentOutOfRangeException(nameof(feet), "Pattern height must be finite and greater than zero.");

        Feet = feet;
    }

    public double Feet { get; }
}
