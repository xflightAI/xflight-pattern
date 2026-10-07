namespace XFlight.Pattern.Core.Circuits;

public sealed record PatternAltitudeMsl
{
    public PatternAltitudeMsl(double feet)
    {
        if (!double.IsFinite(feet))
            throw new ArgumentOutOfRangeException(nameof(feet), "Pattern altitude must be finite.");

        Feet = feet;
    }

    public double Feet { get; }
}
