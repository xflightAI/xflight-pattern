namespace XFlight.Pattern.Core.Geometry;

public sealed record PatternGeometryParameters
{
    public PatternGeometryParameters(
        HorizontalDistance downwindOffset,
        HorizontalDistance upwindExtension,
        HorizontalDistance nearerBaseTurnDistance,
        HorizontalDistance fartherBaseTurnDistance,
        HorizontalDistance finalExtension,
        HorizontalDistance entryLength,
        HorizontalDistance corridorHalfWidth)
    {
        ArgumentNullException.ThrowIfNull(downwindOffset);
        ArgumentNullException.ThrowIfNull(upwindExtension);
        ArgumentNullException.ThrowIfNull(nearerBaseTurnDistance);
        ArgumentNullException.ThrowIfNull(fartherBaseTurnDistance);
        ArgumentNullException.ThrowIfNull(finalExtension);
        ArgumentNullException.ThrowIfNull(entryLength);
        ArgumentNullException.ThrowIfNull(corridorHalfWidth);
        if (nearerBaseTurnDistance.Metres >= fartherBaseTurnDistance.Metres)
            throw new ArgumentException("Base Turn Window must have distinct, ordered nearer/farther limits.");
        if (finalExtension.Metres < fartherBaseTurnDistance.Metres)
            throw new ArgumentException("Final must extend at least to the farther Base Turn Window limit.");
        if (corridorHalfWidth.Metres >= downwindOffset.Metres)
            throw new ArgumentException("The downwind corridor must lie wholly on the selected side.");
        if (entryLength.Metres / Math.Sqrt(2d) <= corridorHalfWidth.Metres)
            throw new ArgumentException("The 45-degree entry must start outside the lateral circuit boundary.");

        DownwindOffset = downwindOffset;
        UpwindExtension = upwindExtension;
        NearerBaseTurnDistance = nearerBaseTurnDistance;
        FartherBaseTurnDistance = fartherBaseTurnDistance;
        FinalExtension = finalExtension;
        EntryLength = entryLength;
        CorridorHalfWidth = corridorHalfWidth;
    }

    public HorizontalDistance DownwindOffset { get; }
    public HorizontalDistance UpwindExtension { get; }
    public HorizontalDistance NearerBaseTurnDistance { get; }
    public HorizontalDistance FartherBaseTurnDistance { get; }
    public HorizontalDistance FinalExtension { get; }
    public HorizontalDistance EntryLength { get; }
    public HorizontalDistance CorridorHalfWidth { get; }
}
