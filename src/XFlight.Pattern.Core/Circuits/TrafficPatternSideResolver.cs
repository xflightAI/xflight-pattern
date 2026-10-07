namespace XFlight.Pattern.Core.Circuits;

public static class TrafficPatternSideResolver
{
    public static TrafficPatternSideResolution Resolve(
        TrafficPatternSide? published = null,
        TrafficPatternSide? xFlightVerified = null,
        TrafficPatternSide? userSelected = null)
    {
        ValidateCandidate(published, nameof(published));
        ValidateCandidate(xFlightVerified, nameof(xFlightVerified));
        ValidateCandidate(userSelected, nameof(userSelected));

        if (published is { } publishedSide)
            return new(publishedSide, TrafficPatternSideProvenance.Published);

        if (xFlightVerified is { } verifiedSide)
            return new(verifiedSide, TrafficPatternSideProvenance.XFlightVerified);

        if (userSelected is { } selectedSide)
            return new(selectedSide, TrafficPatternSideProvenance.UserSelected);

        return TrafficPatternSideResolution.Unknown;
    }

    private static void ValidateCandidate(TrafficPatternSide? candidate, string parameterName)
    {
        if (candidate is null)
            return;

        if (!Enum.IsDefined(candidate.Value) || candidate == TrafficPatternSide.Unknown)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "An explicit side candidate must be Left or Right. Omit it when the value is unknown.");
        }
    }
}
