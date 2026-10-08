using XFlight.Pattern.Core.Geometry;

namespace XFlight.Pattern.Core.PhaseDetection;

/// <summary>M4 envelopes use the selected M3 runway frame but do not change M3 geometry.</summary>
public sealed record PhaseDetectionConfiguration
{
    public PhaseDetectionConfiguration(string algorithmId, string configurationId,
        PatternGeometryResult geometryAuthority, PhaseDetectionPolicy policy,
        double runwayContactHalfWidthMetres)
    {
        Identity.Required(algorithmId, nameof(algorithmId));
        Identity.Required(configurationId, nameof(configurationId));
        ArgumentNullException.ThrowIfNull(geometryAuthority);
        ArgumentNullException.ThrowIfNull(policy);
        if (!geometryAuthority.IsAvailable)
            throw new ArgumentException("Available M3 geometry is required.", nameof(geometryAuthority));
        if (!double.IsFinite(runwayContactHalfWidthMetres) || runwayContactHalfWidthMetres <= 0)
            throw new ArgumentOutOfRangeException(nameof(runwayContactHalfWidthMetres));

        var geometry = geometryAuthority.Geometry!;
        var length = geometryAuthority.RunwayReference.RunwayLength.Metres;
        var contact = new HorizontalArea(0, length, -runwayContactHalfWidthMetres, runwayContactHalfWidthMetres);
        var points = new List<RunwayCoordinate> { geometry.RunwayCenterline.Start, geometry.RunwayCenterline.End };
        foreach (var corridor in new[] { geometry.Upwind, geometry.Crosswind, geometry.Downwind, geometry.Final, geometry.Entry })
            points.AddRange([corridor.StartPlus, corridor.EndPlus, corridor.EndMinus, corridor.StartMinus]);
        points.Add(new(geometry.BaseArea.MinimumAlongTrackMetres, geometry.BaseArea.MinimumCrossTrackMetres));
        points.Add(new(geometry.BaseArea.MaximumAlongTrackMetres, geometry.BaseArea.MaximumCrossTrackMetres));
        var margin = policy.ExitMarginMetres;
        var exit = new HorizontalArea(points.Min(p => p.AlongTrackMetres) - margin,
            points.Max(p => p.AlongTrackMetres) + margin,
            points.Min(p => p.CrossTrackMetres) - margin,
            points.Max(p => p.CrossTrackMetres) + margin);

        AlgorithmId = algorithmId; ConfigurationId = configurationId;
        GeometryAuthority = geometryAuthority; Policy = policy;
        RunwayContactHalfWidthMetres = runwayContactHalfWidthMetres;
        RunwayContactEnvelope = contact; OuterExitEnvelope = exit;
    }

    public string AlgorithmId { get; }
    public string ConfigurationId { get; }
    public PatternGeometryResult GeometryAuthority { get; }
    public PhaseDetectionPolicy Policy { get; }
    public double RunwayContactHalfWidthMetres { get; }
    /// <summary>Closed runway-frame bounds from the two approved thresholds and caller-supplied half-width.</summary>
    public HorizontalArea RunwayContactEnvelope { get; }
    /// <summary>Closed bounds of all M3 areas, expanded by the explicit policy exit margin.</summary>
    public HorizontalArea OuterExitEnvelope { get; }
}
