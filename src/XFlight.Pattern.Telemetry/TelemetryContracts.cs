namespace XFlight.Pattern.Telemetry;

/// <summary>A raw observation may be absent. An available observation must carry a value.</summary>
public readonly record struct RawObservation<T>(bool IsAvailable, T? Value) where T : struct
{
    public static RawObservation<T> Available(T value) => new(true, value);
    public static RawObservation<T> Unavailable => new(false, null);
}

public readonly record struct RawGeographicPosition(double LatitudeDegrees, double LongitudeDegrees);

/// <summary>
/// Simulator-neutral raw sample. Altitude is MSL feet, speed is knots, track is true degrees,
/// and vertical speed is feet per minute. SourceTime is elapsed time within StreamEpoch.
/// </summary>
public sealed record RawTelemetrySample(
    string SourceId,
    string StreamEpoch,
    ulong SampleSequence,
    TimeSpan SourceTime,
    RawObservation<RawGeographicPosition> Position,
    RawObservation<double> AltitudeMslFeet,
    RawObservation<double> GroundSpeedKnots,
    RawObservation<double> TrueGroundTrackDegrees,
    RawObservation<double> VerticalSpeedFeetPerMinute,
    RawObservation<bool> OnGround);

/// <summary>Receipt time uses a monotonic elapsed clock independent of simulator source time.</summary>
public interface IReceiptClock
{
    TimeSpan Elapsed { get; }
}

public enum TelemetrySourceStatus { Disconnected, Connected, Paused }

/// <summary>A read-only source yields received raw samples in delivery order and reports lifecycle separately.</summary>
public interface ITelemetrySource
{
    TelemetrySourceStatus Status { get; }
    bool TryRead(out ReceivedRawTelemetrySample? sample);
}

public sealed record ReceivedRawTelemetrySample(RawTelemetrySample Sample, TimeSpan ReceiptTime);
