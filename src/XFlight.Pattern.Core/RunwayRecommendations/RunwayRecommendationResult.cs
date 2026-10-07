using XFlight.Pattern.Core.Airports;

namespace XFlight.Pattern.Core.RunwayRecommendations;

public sealed record RunwayRecommendationResult
{
    internal RunwayRecommendationResult(
        RunwayRecommendationOutcome outcome,
        RunwayEnd? recommendedRunwayEnd,
        IReadOnlyList<RunwayEndWindEvaluation> rankedCandidates)
    {
        Outcome = outcome;
        RecommendedRunwayEnd = recommendedRunwayEnd;
        RankedCandidates = rankedCandidates;
    }

    public RunwayRecommendationOutcome Outcome { get; }
    public RunwayEnd? RecommendedRunwayEnd { get; }
    public IReadOnlyList<RunwayEndWindEvaluation> RankedCandidates { get; }
}
