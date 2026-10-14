using XFlight.Pattern.Core.Geometry;
using XFlight.Pattern.Core.PhaseDetection;

namespace XFlight.Pattern.Telemetry;

public sealed record NormalizedTelemetrySample(string SourceId, TimeSpan ReceiptTime, TelemetrySnapshot Snapshot);

/// <summary>Pure raw-to-Core conversion. Invalid observations become unavailable; invalid sample metadata yields no sample.</summary>
public static class TelemetryNormalizer
{
    private const double MetresPerFoot = 0.3048d;
    private const double MetresPerSecondPerKnot = 1852d / 3600d;

    public static NormalizedTelemetrySample? Normalize(ReceivedRawTelemetrySample? received)
    {
        if (received?.Sample is not { } raw ||
            string.IsNullOrWhiteSpace(raw.SourceId) ||
            string.IsNullOrWhiteSpace(raw.StreamEpoch) ||
            raw.SampleSequence == 0 || raw.SourceTime < TimeSpan.Zero ||
            received.ReceiptTime < TimeSpan.Zero)
            return null;

        var snapshot = new TelemetrySnapshot(raw.StreamEpoch, raw.SampleSequence, raw.SourceTime,
            Position(raw.Position),
            Number(raw.AltitudeMslFeet, value => value * MetresPerFoot),
            Number(raw.GroundSpeedKnots, value => value * MetresPerSecondPerKnot, nonnegative: true),
            Number(raw.TrueGroundTrackDegrees, value => ((value % 360d) + 360d) % 360d),
            Number(raw.VerticalSpeedFeetPerMinute, value => value * MetresPerFoot / 60d),
            raw.OnGround.IsAvailable && raw.OnGround.Value is { } onGround
                ? new TelemetryObservation<bool>(onGround, TelemetryValidity.Available, TelemetryProvenance.Measured)
                : new TelemetryObservation<bool>(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable));

        return new NormalizedTelemetrySample(raw.SourceId, received.ReceiptTime, snapshot);
    }

    private static TelemetryPositionObservation Position(RawObservation<RawGeographicPosition> raw)
    {
        if (raw.IsAvailable && raw.Value is { } value &&
            double.IsFinite(value.LatitudeDegrees) && value.LatitudeDegrees is >= -90d and <= 90d &&
            double.IsFinite(value.LongitudeDegrees) && value.LongitudeDegrees is >= -180d and <= 180d)
            return new TelemetryPositionObservation(
                new GeographicCoordinate(value.LatitudeDegrees, value.LongitudeDegrees),
                TelemetryValidity.Available, TelemetryProvenance.Measured);

        return new TelemetryPositionObservation(null, TelemetryValidity.Unavailable, TelemetryProvenance.Unavailable);
    }

    private static TelemetryObservation<double> Number(RawObservation<double> raw,
        Func<double, double> convert, bool nonnegative = false)
    {
        if (raw.IsAvailable && raw.Value is { } value && double.IsFinite(value) &&
            (!nonnegative || value >= 0d))
        {
            var converted = convert(value);
            if (double.IsFinite(converted))
                return new TelemetryObservation<double>(converted, TelemetryValidity.Available,
                    TelemetryProvenance.Measured);
        }

        return new TelemetryObservation<double>(null, TelemetryValidity.Unavailable,
            TelemetryProvenance.Unavailable);
    }
}
