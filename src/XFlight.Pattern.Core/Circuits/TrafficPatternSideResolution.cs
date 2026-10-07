namespace XFlight.Pattern.Core.Circuits;

public sealed record TrafficPatternSideResolution
{
    public static TrafficPatternSideResolution Unknown { get; } =
        new(TrafficPatternSide.Unknown, TrafficPatternSideProvenance.Unknown);

    public TrafficPatternSideResolution(TrafficPatternSide side, TrafficPatternSideProvenance provenance)
    {
        if (!Enum.IsDefined(side))
            throw new ArgumentOutOfRangeException(nameof(side));

        if (!Enum.IsDefined(provenance))
            throw new ArgumentOutOfRangeException(nameof(provenance));

        var isUnknownSide = side == TrafficPatternSide.Unknown;
        var isUnknownProvenance = provenance == TrafficPatternSideProvenance.Unknown;

        if (isUnknownSide != isUnknownProvenance)
        {
            throw new ArgumentException(
                "An unknown side must have unknown provenance, and a known side must have known provenance.");
        }

        Side = side;
        Provenance = provenance;
    }

    public TrafficPatternSide Side { get; }
    public TrafficPatternSideProvenance Provenance { get; }
}
