using XFlight.Pattern.Core.Airports;

namespace XFlight.Pattern.Core.RunwayRecommendations;

public sealed record RunwayEndWindEvaluation(RunwayEnd RunwayEnd, double AngularDifferenceDegrees, double HeadwindKnots, double TailwindKnots, double CrosswindKnots, LongitudinalWind LongitudinalWind)
{
    internal double SignedLongitudinalKnots => HeadwindKnots - TailwindKnots;
}
