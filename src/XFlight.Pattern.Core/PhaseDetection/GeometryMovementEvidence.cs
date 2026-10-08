using XFlight.Pattern.Core.Geometry;

namespace XFlight.Pattern.Core.PhaseDetection;

public enum DirectionEvidenceStatus
{
    MissingGroundSpeed,
    BelowMinimumGroundSpeed,
    MissingGroundTrack,
    OutsideExitTolerance,
    WithinExitTolerance,
    WithinEnterTolerance
}

/// <summary>One instantaneous direction comparison; no phase or persistence decision.</summary>
public sealed record DirectionEvidence(
    DirectionEvidenceStatus Status, double? AngularErrorDegrees);

/// <summary>Closed enter and exit membership and independent direction evidence for one region.</summary>
public sealed record MovementRegionEvidence(
    bool InsideEnterBounds, bool InsideExitBounds, DirectionEvidence Direction);

/// <summary>Stateless horizontal evidence for a single normalized telemetry snapshot.</summary>
public sealed record GeometryMovementEvidence
{
    private GeometryMovementEvidence(RunwayCoordinate? position, MovementRegionEvidence entry,
        MovementRegionEvidence departureUpwind, MovementRegionEvidence crosswind,
        MovementRegionEvidence downwind, MovementRegionEvidence @base, MovementRegionEvidence final,
        bool insideBaseTurnWindow, bool insideRunwayContactEnvelope, bool insideOuterExitEnvelope)
    {
        Position = position;
        Entry = entry;
        DepartureUpwind = departureUpwind;
        Crosswind = crosswind;
        Downwind = downwind;
        Base = @base;
        Final = final;
        InsideBaseTurnWindow = insideBaseTurnWindow;
        InsideRunwayContactEnvelope = insideRunwayContactEnvelope;
        InsideOuterExitEnvelope = insideOuterExitEnvelope;
    }

    public RunwayCoordinate? Position { get; }
    public MovementRegionEvidence Entry { get; }
    public MovementRegionEvidence DepartureUpwind { get; }
    public MovementRegionEvidence Crosswind { get; }
    public MovementRegionEvidence Downwind { get; }
    public MovementRegionEvidence Base { get; }
    public MovementRegionEvidence Final { get; }
    public bool InsideBaseTurnWindow { get; }
    public bool InsideRunwayContactEnvelope { get; }
    public bool InsideOuterExitEnvelope { get; }

    /// <summary>Minimum usable speed is caller supplied and applies only to direction evidence.</summary>
    public static GeometryMovementEvidence Evaluate(PhaseDetectionConfiguration configuration,
        TelemetrySnapshot snapshot, double minimumUsableGroundSpeedMetresPerSecond)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!double.IsFinite(minimumUsableGroundSpeedMetresPerSecond) ||
            minimumUsableGroundSpeedMetresPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumUsableGroundSpeedMetresPerSecond));

        var geometry = configuration.GeometryAuthority.Geometry!;
        var runway = configuration.GeometryAuthority.RunwayReference;
        var policy = configuration.Policy;
        RunwayCoordinate? position = snapshot.Position.Validity == TelemetryValidity.Available
            ? runway.ToRunway(snapshot.Position.Value!) : null;

        DirectionEvidence Direction(double along, double cross)
        {
            if (snapshot.GroundSpeedMetresPerSecond.Validity != TelemetryValidity.Available)
                return new(DirectionEvidenceStatus.MissingGroundSpeed, null);
            if (snapshot.GroundSpeedMetresPerSecond.Value!.Value < minimumUsableGroundSpeedMetresPerSecond)
                return new(DirectionEvidenceStatus.BelowMinimumGroundSpeed, null);
            if (snapshot.TrueGroundTrackDegrees.Validity != TelemetryValidity.Available)
                return new(DirectionEvidenceStatus.MissingGroundTrack, null);

            var length = double.Hypot(along, cross);
            var unitAlong = along / length;
            var unitCross = cross / length;
            var east = unitAlong * runway.ForwardEast + unitCross * runway.ForwardNorth;
            var north = unitAlong * runway.ForwardNorth - unitCross * runway.ForwardEast;
            var expectedBearing = Math.Atan2(east, north) * 180d / Math.PI;
            var difference = Math.Abs(snapshot.TrueGroundTrackDegrees.Value!.Value - expectedBearing) % 360d;
            var error = difference <= 180d ? difference : 360d - difference;
            // The last angular subtraction can round an equality upward by one representable degree value.
            // Admit only that adjacent value; no fixed-degree epsilon is added to the policy tolerance.
            var status = error <= double.BitIncrement(policy.TrackEnterToleranceDegrees)
                ? DirectionEvidenceStatus.WithinEnterTolerance
                : error <= double.BitIncrement(policy.TrackExitToleranceDegrees)
                    ? DirectionEvidenceStatus.WithinExitTolerance
                    : DirectionEvidenceStatus.OutsideExitTolerance;
            return new(status, error);
        }

        MovementRegionEvidence Corridor(LegCorridor corridor)
        {
            var axis = corridor.Axis;
            return new(position is { } p && Contains(corridor, p, policy.CorridorEnterMarginMetres),
                position is { } q && Contains(corridor, q, policy.CorridorExitMarginMetres),
                Direction(axis.End.AlongTrackMetres - axis.Start.AlongTrackMetres,
                    axis.End.CrossTrackMetres - axis.Start.CrossTrackMetres));
        }

        var runwayCorridor = new LegCorridor(geometry.RunwayCenterline, geometry.Parameters.CorridorHalfWidth);
        var departure = Corridor(geometry.Upwind);
        var runwayDeparture = Corridor(runwayCorridor);
        var side = Math.Sign(geometry.Downwind.Axis.Start.CrossTrackMetres);
        var baseDirection = Direction(0, -side);
        var baseEnter = position is { } bp && ContainsBase(geometry.BaseArea, bp, policy.CorridorEnterMarginMetres);
        var baseExit = position is { } bq && ContainsBase(geometry.BaseArea, bq, policy.CorridorExitMarginMetres);

        return new(position, Corridor(geometry.Entry),
            new(departure.InsideEnterBounds || runwayDeparture.InsideEnterBounds,
                departure.InsideExitBounds || runwayDeparture.InsideExitBounds, departure.Direction),
            Corridor(geometry.Crosswind), Corridor(geometry.Downwind),
            new(baseEnter, baseExit, baseDirection), Corridor(geometry.Final),
            position is { } w && Contains(geometry.BaseTurnWindow, w, 0),
            position is { } c && Contains(configuration.RunwayContactEnvelope, c, 0),
            position is { } o && Contains(configuration.OuterExitEnvelope, o, 0));
    }

    /// <summary>Closed rectangle, expanded equally on every side by the supplied nonnegative margin.</summary>
    public static bool Contains(HorizontalArea area, RunwayCoordinate point, double marginMetres)
    {
        ArgumentNullException.ThrowIfNull(area);
        ValidateMargin(marginMetres);
        return point.AlongTrackMetres >= area.MinimumAlongTrackMetres - marginMetres &&
            point.AlongTrackMetres <= area.MaximumAlongTrackMetres + marginMetres &&
            point.CrossTrackMetres >= area.MinimumCrossTrackMetres - marginMetres &&
            point.CrossTrackMetres <= area.MaximumCrossTrackMetres + marginMetres;
    }

    /// <summary>Closed oriented rectangle; margin expands both flat ends and both sides.</summary>
    public static bool Contains(LegCorridor corridor, RunwayCoordinate point, double marginMetres)
    {
        ArgumentNullException.ThrowIfNull(corridor);
        ValidateMargin(marginMetres);
        var axis = corridor.Axis;
        var along = (axis.End.AlongTrackMetres - axis.Start.AlongTrackMetres) / axis.Length.Metres;
        var cross = (axis.End.CrossTrackMetres - axis.Start.CrossTrackMetres) / axis.Length.Metres;
        var dx = point.AlongTrackMetres - axis.Start.AlongTrackMetres;
        var dy = point.CrossTrackMetres - axis.Start.CrossTrackMetres;
        var longitudinal = dx * along + dy * cross;
        var lateral = -dx * cross + dy * along;
        return longitudinal >= -marginMetres && longitudinal <= axis.Length.Metres + marginMetres &&
            Math.Abs(lateral) <= corridor.HalfWidth.Metres + marginMetres;
    }

    private static bool ContainsBase(HorizontalArea area, RunwayCoordinate point, double marginMetres) =>
        point.AlongTrackMetres >= area.MinimumAlongTrackMetres &&
        point.AlongTrackMetres <= area.MaximumAlongTrackMetres &&
        point.CrossTrackMetres >= area.MinimumCrossTrackMetres - marginMetres &&
        point.CrossTrackMetres <= area.MaximumCrossTrackMetres + marginMetres;

    private static void ValidateMargin(double marginMetres)
    {
        if (!double.IsFinite(marginMetres) || marginMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(marginMetres));
    }
}
