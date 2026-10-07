using XFlight.Pattern.Core.Airports;
using XFlight.Pattern.Core.Weather;

namespace XFlight.Pattern.Core.RunwayRecommendations;

public static class RunwayRecommendationService
{
    public static RunwayRecommendationResult Recommend(Airport airport, WindObservation wind)
    {
        ArgumentNullException.ThrowIfNull(airport);
        ArgumentNullException.ThrowIfNull(wind);

        var usableEnds = airport.RunwayEnds.Where(static end => end.IsUsable).ToArray();
        if (usableEnds.Length == 0)
            throw new InvalidOperationException("An airport must contain at least one usable runway end to produce a recommendation.");

        if (wind.State != WindState.Directional)
            return new(RunwayRecommendationOutcome.NoWindPreferredRunway, null, Array.Empty<RunwayEndWindEvaluation>());

        var ranked = usableEnds
            .Select(end => WindComponentCalculator.Evaluate(end, wind))
            .OrderByDescending(static evaluation => evaluation.SignedLongitudinalKnots)
            .ThenBy(static evaluation => evaluation.CrosswindKnots)
            .ThenBy(static evaluation => evaluation.RunwayEnd.Identifier, StringComparer.Ordinal)
            .ThenBy(static evaluation => evaluation.RunwayEnd.Heading.Degrees)
            .ToArray();

        var hasWindTie = ranked.Length > 1
            && ranked[0].SignedLongitudinalKnots == ranked[1].SignedLongitudinalKnots
            && ranked[0].CrosswindKnots == ranked[1].CrosswindKnots;

        return new(hasWindTie ? RunwayRecommendationOutcome.RecommendedByDeterministicTieBreak : RunwayRecommendationOutcome.RecommendedByWind,
            ranked[0].RunwayEnd, Array.AsReadOnly(ranked));
    }
}
