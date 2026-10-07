namespace XFlight.Pattern.Core.Circuits;

public sealed record PatternAltitudeResolution
{
    public static PatternAltitudeResolution Unresolved { get; } =
        new(PatternAltitudeProvenance.Unresolved, null, null, null);

    private PatternAltitudeResolution(
        PatternAltitudeProvenance provenance,
        PatternAltitudeMsl? targetMsl,
        AirportElevationMsl? airportElevationMsl,
        PatternHeightAgl? originatingHeightAgl)
    {
        Provenance = provenance;
        TargetMsl = targetMsl;
        AirportElevationMsl = airportElevationMsl;
        OriginatingHeightAgl = originatingHeightAgl;
    }

    public PatternAltitudeProvenance Provenance { get; }
    public PatternAltitudeMsl? TargetMsl { get; }
    public AirportElevationMsl? AirportElevationMsl { get; }
    public PatternHeightAgl? OriginatingHeightAgl { get; }
    public bool IsResolved => TargetMsl is not null;

    public static PatternAltitudeResolution Published(PatternAltitudeMsl targetMsl) =>
        Direct(PatternAltitudeProvenance.Published, targetMsl);

    public static PatternAltitudeResolution XFlightVerified(PatternAltitudeMsl targetMsl) =>
        Direct(PatternAltitudeProvenance.XFlightVerified, targetMsl);

    public static PatternAltitudeResolution UserSelected(PatternAltitudeMsl targetMsl) =>
        Direct(PatternAltitudeProvenance.UserSelected, targetMsl);

    public static PatternAltitudeResolution UserSelected(
        AirportElevationMsl airportElevationMsl,
        PatternHeightAgl patternHeightAgl) =>
        Derived(PatternAltitudeProvenance.UserSelected, airportElevationMsl, patternHeightAgl);

    public static PatternAltitudeResolution TrainingDefault(AirportElevationMsl airportElevationMsl) =>
        Derived(
            PatternAltitudeProvenance.TrainingDefault,
            airportElevationMsl,
            C172TrainingProfile.DefaultPatternHeightAgl);

    private static PatternAltitudeResolution Direct(
        PatternAltitudeProvenance provenance,
        PatternAltitudeMsl targetMsl)
    {
        ArgumentNullException.ThrowIfNull(targetMsl);
        return new(provenance, targetMsl, null, null);
    }

    private static PatternAltitudeResolution Derived(
        PatternAltitudeProvenance provenance,
        AirportElevationMsl airportElevationMsl,
        PatternHeightAgl patternHeightAgl)
    {
        ArgumentNullException.ThrowIfNull(airportElevationMsl);
        ArgumentNullException.ThrowIfNull(patternHeightAgl);

        var targetFeet = airportElevationMsl.Feet + patternHeightAgl.Feet;
        if (!double.IsFinite(targetFeet))
            throw new ArgumentOutOfRangeException(nameof(patternHeightAgl), "Derived pattern altitude must be finite.");

        return new(
            provenance,
            new PatternAltitudeMsl(targetFeet),
            airportElevationMsl,
            patternHeightAgl);
    }
}
