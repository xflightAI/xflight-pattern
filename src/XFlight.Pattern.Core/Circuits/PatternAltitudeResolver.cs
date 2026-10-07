namespace XFlight.Pattern.Core.Circuits;

public static class PatternAltitudeResolver
{
    public static PatternAltitudeResolution Resolve(
        PatternAltitudeMsl? published = null,
        PatternAltitudeMsl? xFlightVerified = null,
        PatternAltitudeResolution? userSelected = null,
        AirportElevationMsl? airportElevationMsl = null,
        bool useC172TrainingDefault = false)
    {
        if (userSelected is not null && userSelected.Provenance != PatternAltitudeProvenance.UserSelected)
        {
            throw new ArgumentException(
                "The user-selected candidate must retain UserSelected provenance.",
                nameof(userSelected));
        }

        if (published is not null)
            return PatternAltitudeResolution.Published(published);

        if (xFlightVerified is not null)
            return PatternAltitudeResolution.XFlightVerified(xFlightVerified);

        if (userSelected is not null)
            return userSelected;

        if (useC172TrainingDefault && airportElevationMsl is not null)
        {
            return PatternAltitudeResolution.TrainingDefault(airportElevationMsl);
        }

        return PatternAltitudeResolution.Unresolved;
    }
}
