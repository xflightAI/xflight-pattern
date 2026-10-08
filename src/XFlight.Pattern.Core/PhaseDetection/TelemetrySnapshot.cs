using XFlight.Pattern.Core.Geometry;

namespace XFlight.Pattern.Core.PhaseDetection;

public enum TelemetryValidity { Available, Unavailable }
public enum TelemetryProvenance { Measured, Derived, Unavailable }

/// <summary>A normalized observation; unavailable values carry no payload or claimed provenance.</summary>
public sealed record TelemetryObservation<T> where T : struct
{
    public TelemetryObservation(T? value, TelemetryValidity validity, TelemetryProvenance provenance)
    {
        if (!Enum.IsDefined(validity)) throw new ArgumentOutOfRangeException(nameof(validity));
        if (!Enum.IsDefined(provenance)) throw new ArgumentOutOfRangeException(nameof(provenance));
        if ((validity == TelemetryValidity.Available) != (value is not null) ||
            (validity == TelemetryValidity.Available) != (provenance != TelemetryProvenance.Unavailable))
            throw new ArgumentException("Value, validity and provenance must agree.");
        Value = value;
        Validity = validity;
        Provenance = provenance;
    }

    public T? Value { get; }
    public TelemetryValidity Validity { get; }
    public TelemetryProvenance Provenance { get; }
}

public sealed record TelemetryPositionObservation
{
    public TelemetryPositionObservation(GeographicCoordinate? value, TelemetryValidity validity, TelemetryProvenance provenance)
    {
        if (!Enum.IsDefined(validity)) throw new ArgumentOutOfRangeException(nameof(validity));
        if (!Enum.IsDefined(provenance)) throw new ArgumentOutOfRangeException(nameof(provenance));
        if ((validity == TelemetryValidity.Available) != (value is not null) ||
            (validity == TelemetryValidity.Available) != (provenance != TelemetryProvenance.Unavailable))
            throw new ArgumentException("Value, validity and provenance must agree.");
        Value = value; Validity = validity; Provenance = provenance;
    }
    public GeographicCoordinate? Value { get; }
    public TelemetryValidity Validity { get; }
    public TelemetryProvenance Provenance { get; }
}

/// <summary>One source sample. Source time is monotonic elapsed time within an epoch, not wall time.</summary>
public sealed record TelemetrySnapshot
{
    public TelemetrySnapshot(string streamEpoch, ulong sampleSequence, TimeSpan sourceTime,
        TelemetryPositionObservation position,
        TelemetryObservation<double> altitudeMslMetres,
        TelemetryObservation<double> groundSpeedMetresPerSecond,
        TelemetryObservation<double> trueGroundTrackDegrees,
        TelemetryObservation<double> verticalSpeedMetresPerSecond,
        TelemetryObservation<bool> onGround)
    {
        if (string.IsNullOrWhiteSpace(streamEpoch)) throw new ArgumentException("Epoch is required.", nameof(streamEpoch));
        if (sampleSequence == 0) throw new ArgumentOutOfRangeException(nameof(sampleSequence));
        if (sourceTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(sourceTime));
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(altitudeMslMetres);
        ArgumentNullException.ThrowIfNull(groundSpeedMetresPerSecond);
        ArgumentNullException.ThrowIfNull(trueGroundTrackDegrees);
        ArgumentNullException.ThrowIfNull(verticalSpeedMetresPerSecond);
        ArgumentNullException.ThrowIfNull(onGround);
        ValidateFinite(altitudeMslMetres.Value, nameof(altitudeMslMetres));
        ValidateFinite(verticalSpeedMetresPerSecond.Value, nameof(verticalSpeedMetresPerSecond));
        ValidateFinite(groundSpeedMetresPerSecond.Value, nameof(groundSpeedMetresPerSecond));
        if (groundSpeedMetresPerSecond.Value is < 0) throw new ArgumentOutOfRangeException(nameof(groundSpeedMetresPerSecond));
        ValidateFinite(trueGroundTrackDegrees.Value, nameof(trueGroundTrackDegrees));
        if (trueGroundTrackDegrees.Value is < 0 or >= 360) throw new ArgumentOutOfRangeException(nameof(trueGroundTrackDegrees));

        StreamEpoch = streamEpoch;
        SampleSequence = sampleSequence;
        SourceTime = sourceTime;
        Position = position;
        AltitudeMslMetres = altitudeMslMetres;
        GroundSpeedMetresPerSecond = groundSpeedMetresPerSecond;
        TrueGroundTrackDegrees = trueGroundTrackDegrees;
        VerticalSpeedMetresPerSecond = verticalSpeedMetresPerSecond;
        OnGround = onGround;
    }

    public string StreamEpoch { get; }
    public ulong SampleSequence { get; }
    public TimeSpan SourceTime { get; }
    public TelemetryPositionObservation Position { get; }
    public TelemetryObservation<double> AltitudeMslMetres { get; }
    public TelemetryObservation<double> GroundSpeedMetresPerSecond { get; }
    public TelemetryObservation<double> TrueGroundTrackDegrees { get; }
    public TelemetryObservation<double> VerticalSpeedMetresPerSecond { get; }
    public TelemetryObservation<bool> OnGround { get; }

    private static void ValidateFinite(double? value, string name)
    {
        if (value is { } number && !double.IsFinite(number)) throw new ArgumentOutOfRangeException(name);
    }
}
