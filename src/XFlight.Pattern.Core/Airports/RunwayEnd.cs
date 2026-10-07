namespace XFlight.Pattern.Core.Airports;

public sealed record RunwayEnd
{
    public RunwayEnd(string identifier, Heading heading, bool isUsable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        Identifier = identifier.Trim().ToUpperInvariant();
        Heading = heading;
        IsUsable = isUsable;
    }

    public string Identifier { get; }
    public Heading Heading { get; }
    public bool IsUsable { get; }
}
