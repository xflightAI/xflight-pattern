namespace XFlight.Pattern.Core.Airports;

public sealed record AirportIdentifier
{
    public AirportIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToUpperInvariant();
    }

    public string Value { get; }
    public override string ToString() => Value;
}
