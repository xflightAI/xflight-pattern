namespace XFlight.Pattern.Core.PhaseDetection;

public enum PatternPhase
{
    Unknown, Entry, Departure, Crosswind, Downwind, Base, Final, LandingRoll, GoAround, Exited
}

/// <summary>All detector thresholds are supplied by the caller; distances are metres and angles are degrees.</summary>
public sealed record PhaseDetectionPolicy
{
    public PhaseDetectionPolicy(
        TimeSpan candidatePersistence, TimeSpan groundContactPersistence, TimeSpan departurePersistence,
        TimeSpan exitPersistence, TimeSpan maximumSampleGap, TimeSpan maximumCandidateGap,
        double corridorEnterMarginMetres, double corridorExitMarginMetres,
        double trackEnterToleranceDegrees, double trackExitToleranceDegrees,
        double groundContactMaximumSpeedMetresPerSecond,
        double departureMinimumSpeedMetresPerSecond, double departureMinimumClimbMetresPerSecond,
        double exitMarginMetres, double maximumRelocationSpeedMetresPerSecond,
        double maximumRelocationDistanceMetres)
    {
        Positive(candidatePersistence, nameof(candidatePersistence));
        Positive(groundContactPersistence, nameof(groundContactPersistence));
        Positive(departurePersistence, nameof(departurePersistence));
        Positive(exitPersistence, nameof(exitPersistence));
        Positive(maximumSampleGap, nameof(maximumSampleGap));
        Positive(maximumCandidateGap, nameof(maximumCandidateGap));
        if (maximumCandidateGap > maximumSampleGap) throw new ArgumentException("Candidate continuity cannot exceed sample continuity.");
        Nonnegative(corridorEnterMarginMetres, nameof(corridorEnterMarginMetres));
        Nonnegative(corridorExitMarginMetres, nameof(corridorExitMarginMetres));
        if (corridorExitMarginMetres < corridorEnterMarginMetres) throw new ArgumentException("Exit margin must be at least enter margin.");
        Angle(trackEnterToleranceDegrees, nameof(trackEnterToleranceDegrees));
        Angle(trackExitToleranceDegrees, nameof(trackExitToleranceDegrees));
        if (trackExitToleranceDegrees < trackEnterToleranceDegrees) throw new ArgumentException("Track exit tolerance must be at least enter tolerance.");
        Nonnegative(groundContactMaximumSpeedMetresPerSecond, nameof(groundContactMaximumSpeedMetresPerSecond));
        Nonnegative(departureMinimumSpeedMetresPerSecond, nameof(departureMinimumSpeedMetresPerSecond));
        Nonnegative(departureMinimumClimbMetresPerSecond, nameof(departureMinimumClimbMetresPerSecond));
        Nonnegative(exitMarginMetres, nameof(exitMarginMetres));
        Positive(maximumRelocationSpeedMetresPerSecond, nameof(maximumRelocationSpeedMetresPerSecond));
        Positive(maximumRelocationDistanceMetres, nameof(maximumRelocationDistanceMetres));
        CandidatePersistence = candidatePersistence;
        GroundContactPersistence = groundContactPersistence;
        DeparturePersistence = departurePersistence;
        ExitPersistence = exitPersistence;
        MaximumSampleGap = maximumSampleGap;
        MaximumCandidateGap = maximumCandidateGap;
        CorridorEnterMarginMetres = corridorEnterMarginMetres;
        CorridorExitMarginMetres = corridorExitMarginMetres;
        TrackEnterToleranceDegrees = trackEnterToleranceDegrees;
        TrackExitToleranceDegrees = trackExitToleranceDegrees;
        GroundContactMaximumSpeedMetresPerSecond = groundContactMaximumSpeedMetresPerSecond;
        DepartureMinimumSpeedMetresPerSecond = departureMinimumSpeedMetresPerSecond;
        DepartureMinimumClimbMetresPerSecond = departureMinimumClimbMetresPerSecond;
        ExitMarginMetres = exitMarginMetres;
        MaximumRelocationSpeedMetresPerSecond = maximumRelocationSpeedMetresPerSecond;
        MaximumRelocationDistanceMetres = maximumRelocationDistanceMetres;
    }

    public TimeSpan CandidatePersistence { get; }
    public TimeSpan GroundContactPersistence { get; }
    public TimeSpan DeparturePersistence { get; }
    public TimeSpan ExitPersistence { get; }
    public TimeSpan MaximumSampleGap { get; }
    public TimeSpan MaximumCandidateGap { get; }
    public double CorridorEnterMarginMetres { get; }
    public double CorridorExitMarginMetres { get; }
    public double TrackEnterToleranceDegrees { get; }
    public double TrackExitToleranceDegrees { get; }
    public double GroundContactMaximumSpeedMetresPerSecond { get; }
    public double DepartureMinimumSpeedMetresPerSecond { get; }
    public double DepartureMinimumClimbMetresPerSecond { get; }
    public double ExitMarginMetres { get; }
    public double MaximumRelocationSpeedMetresPerSecond { get; }
    public double MaximumRelocationDistanceMetres { get; }

    private static void Positive(TimeSpan value, string name)
    {
        if (value <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(name);
    }
    private static void Positive(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(name);
    }
    private static void Nonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
    }
    private static void Angle(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0 || value > 180) throw new ArgumentOutOfRangeException(name);
    }
}
