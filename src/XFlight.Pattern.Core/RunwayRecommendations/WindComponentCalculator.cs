using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Weather;

namespace XFlight.Pattern.Core.RunwayRecommendations;

public static class WindComponentCalculator
{
    private const double NeutralTolerance = 1e-10;

    public static RunwayEndWindEvaluation Evaluate(RunwayEnd runwayEnd, WindObservation wind)
    {
        ArgumentNullException.ThrowIfNull(runwayEnd);
        ArgumentNullException.ThrowIfNull(wind);

        if (wind.State != WindState.Directional || wind.Direction is null || wind.SpeedKnots is null)
            throw new ArgumentException("Wind components require a directional wind observation.", nameof(wind));

        var difference = MinimumAngularDifference(runwayEnd.Heading.Degrees, wind.Direction.Value.Degrees);
        var radians = difference * Math.PI / 180d;
        var signedLongitudinal = CleanZero(wind.SpeedKnots.Value * Math.Cos(radians));
        var crosswind = CleanZero(wind.SpeedKnots.Value * Math.Abs(Math.Sin(radians)));

        return new(runwayEnd, difference, Math.Max(signedLongitudinal, 0d), Math.Max(-signedLongitudinal, 0d), crosswind,
            signedLongitudinal > 0d ? LongitudinalWind.Headwind : signedLongitudinal < 0d ? LongitudinalWind.Tailwind : LongitudinalWind.Neutral);
    }

    private static double MinimumAngularDifference(double firstDegrees, double secondDegrees)
    {
        var directDifference = Math.Abs(firstDegrees - secondDegrees) % 360d;
        return Math.Min(directDifference, 360d - directDifference);
    }

    private static double CleanZero(double value) => Math.Abs(value) < NeutralTolerance ? 0d : value;
}
