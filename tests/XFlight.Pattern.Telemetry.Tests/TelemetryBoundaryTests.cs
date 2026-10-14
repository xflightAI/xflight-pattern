using XFlight.Pattern.Core.PhaseDetection;

namespace XFlight.Pattern.Telemetry.Tests;

public sealed class TelemetryBoundaryTests
{
    private static RawTelemetrySample Raw() => new(
        "sim-1", "epoch-1", 1, TimeSpan.FromSeconds(12),
        RawObservation<RawGeographicPosition>.Available(new(45, 180)),
        RawObservation<double>.Available(1000),
        RawObservation<double>.Available(100),
        RawObservation<double>.Available(450),
        RawObservation<double>.Available(-600),
        RawObservation<bool>.Available(false));

    private static NormalizedTelemetrySample? Normalize(RawTelemetrySample? raw,
        TimeSpan? receipt = null) => raw is null ? TelemetryNormalizer.Normalize(null)
        : TelemetryNormalizer.Normalize(new(raw, receipt ?? TimeSpan.FromSeconds(20)));

    [Fact]
    public void Converts_units_and_canonicalizes_angles_and_longitude()
    {
        var result = Normalize(Raw())!;
        var sample = result.Snapshot;

        Assert.Equal("sim-1", result.SourceId);
        Assert.Equal("epoch-1", sample.StreamEpoch);
        Assert.Equal((ulong)1, sample.SampleSequence);
        Assert.Equal(TimeSpan.FromSeconds(12), sample.SourceTime);
        Assert.Equal(TimeSpan.FromSeconds(20), result.ReceiptTime);
        Assert.Equal(45, sample.Position.Value!.LatitudeDegrees);
        Assert.Equal(-180, sample.Position.Value.LongitudeDegrees);
        Assert.Equal(304.8, sample.AltitudeMslMetres.Value!.Value, 10);
        Assert.Equal(100 * 1852d / 3600d, sample.GroundSpeedMetresPerSecond.Value!.Value, 10);
        Assert.Equal(90, sample.TrueGroundTrackDegrees.Value);
        Assert.Equal(-3.048, sample.VerticalSpeedMetresPerSecond.Value!.Value, 10);
        Assert.False(sample.OnGround.Value);
        Assert.All(new[] { sample.AltitudeMslMetres, sample.GroundSpeedMetresPerSecond,
            sample.TrueGroundTrackDegrees, sample.VerticalSpeedMetresPerSecond },
            observation => Assert.Equal(TelemetryProvenance.Measured, observation.Provenance));
        Assert.Equal(TelemetryProvenance.Measured, sample.Position.Provenance);
        Assert.Equal(TelemetryProvenance.Measured, sample.OnGround.Provenance);
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(360, 0)]
    [InlineData(-720, 0)]
    public void Wraps_finite_true_track_into_core_range(double rawTrack, double expected)
    {
        var sample = Normalize(Raw() with
        {
            TrueGroundTrackDegrees = RawObservation<double>.Available(rawTrack)
        })!.Snapshot;

        Assert.Equal(expected, sample.TrueGroundTrackDegrees.Value);
    }

    [Fact]
    public void Each_explicitly_unavailable_observation_stays_unavailable_without_payload()
    {
        var raw = Raw() with
        {
            Position = RawObservation<RawGeographicPosition>.Unavailable,
            AltitudeMslFeet = RawObservation<double>.Unavailable,
            GroundSpeedKnots = RawObservation<double>.Unavailable,
            TrueGroundTrackDegrees = RawObservation<double>.Unavailable,
            VerticalSpeedFeetPerMinute = RawObservation<double>.Unavailable,
            OnGround = RawObservation<bool>.Unavailable
        };
        var sample = Normalize(raw)!.Snapshot;

        AssertUnavailable(sample.Position.Validity, sample.Position.Provenance, sample.Position.Value);
        AssertUnavailable(sample.AltitudeMslMetres.Validity, sample.AltitudeMslMetres.Provenance,
            sample.AltitudeMslMetres.Value);
        AssertUnavailable(sample.GroundSpeedMetresPerSecond.Validity, sample.GroundSpeedMetresPerSecond.Provenance,
            sample.GroundSpeedMetresPerSecond.Value);
        AssertUnavailable(sample.TrueGroundTrackDegrees.Validity, sample.TrueGroundTrackDegrees.Provenance,
            sample.TrueGroundTrackDegrees.Value);
        AssertUnavailable(sample.VerticalSpeedMetresPerSecond.Validity, sample.VerticalSpeedMetresPerSecond.Provenance,
            sample.VerticalSpeedMetresPerSecond.Value);
        AssertUnavailable(sample.OnGround.Validity, sample.OnGround.Provenance, sample.OnGround.Value);
    }

    [Fact]
    public void Invalid_observations_are_isolated_and_do_not_create_flight_data()
    {
        var raw = Raw() with
        {
            Position = RawObservation<RawGeographicPosition>.Available(new(91, 0)),
            AltitudeMslFeet = RawObservation<double>.Available(double.NaN),
            GroundSpeedKnots = RawObservation<double>.Available(-1),
            TrueGroundTrackDegrees = RawObservation<double>.Available(double.PositiveInfinity),
            VerticalSpeedFeetPerMinute = RawObservation<double>.Available(double.NegativeInfinity),
            OnGround = new RawObservation<bool>(true, null)
        };
        var sample = Normalize(raw)!.Snapshot;

        Assert.Equal(TelemetryValidity.Unavailable, sample.Position.Validity);
        Assert.Equal(TelemetryValidity.Unavailable, sample.AltitudeMslMetres.Validity);
        Assert.Equal(TelemetryValidity.Unavailable, sample.GroundSpeedMetresPerSecond.Validity);
        Assert.Equal(TelemetryValidity.Unavailable, sample.TrueGroundTrackDegrees.Validity);
        Assert.Equal(TelemetryValidity.Unavailable, sample.VerticalSpeedMetresPerSecond.Validity);
        Assert.Equal(TelemetryValidity.Unavailable, sample.OnGround.Validity);

        var partial = Normalize(Raw() with
        {
            GroundSpeedKnots = new RawObservation<double>(false, 100),
            Position = RawObservation<RawGeographicPosition>.Available(new(45, 9))
        })!.Snapshot;
        Assert.Null(partial.GroundSpeedMetresPerSecond.Value);
        Assert.Equal(TelemetryValidity.Available, partial.Position.Validity);
    }

    [Fact]
    public void Malformed_sample_identity_or_time_yields_no_snapshot()
    {
        Assert.Null(Normalize(null));
        Assert.Null(Normalize(Raw() with { SourceId = " " }));
        Assert.Null(Normalize(Raw() with { StreamEpoch = "" }));
        Assert.Null(Normalize(Raw() with { SampleSequence = 0 }));
        Assert.Null(Normalize(Raw() with { SourceTime = TimeSpan.FromTicks(-1) }));
        Assert.Null(Normalize(Raw(), TimeSpan.FromTicks(-1)));
    }

    [Fact]
    public void Preserves_sequences_epochs_and_independent_clocks_without_reordering()
    {
        var clock = new ManualReceiptClock();
        var source = new FakeTelemetrySource(clock);
        source.Connect();
        source.Enqueue(Raw() with { SampleSequence = 2, SourceTime = TimeSpan.FromSeconds(8) });
        clock.Advance(TimeSpan.FromSeconds(5));
        source.Enqueue(Raw() with { SampleSequence = 1, SourceTime = TimeSpan.FromSeconds(3) });
        clock.Advance(TimeSpan.FromSeconds(2));
        source.Enqueue(Raw() with { StreamEpoch = "epoch-2", SampleSequence = 1, SourceTime = TimeSpan.Zero });

        Assert.True(source.TryRead(out var first));
        Assert.True(source.TryRead(out var second));
        Assert.True(source.TryRead(out var third));
        Assert.False(source.TryRead(out _));
        var a = TelemetryNormalizer.Normalize(first)!;
        var b = TelemetryNormalizer.Normalize(second)!;
        var c = TelemetryNormalizer.Normalize(third)!;
        Assert.Equal(new ulong[] { 2, 1, 1 }, new[] { a.Snapshot.SampleSequence, b.Snapshot.SampleSequence,
            c.Snapshot.SampleSequence });
        Assert.Equal(new[] { "epoch-1", "epoch-1", "epoch-2" }, new[] { a.Snapshot.StreamEpoch,
            b.Snapshot.StreamEpoch, c.Snapshot.StreamEpoch });
        Assert.Equal(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(7) },
            new[] { a.ReceiptTime, b.ReceiptTime, c.ReceiptTime });
        Assert.Equal(new[] { TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(3), TimeSpan.Zero },
            new[] { a.Snapshot.SourceTime, b.Snapshot.SourceTime, c.Snapshot.SourceTime });
    }

    [Fact]
    public void Pause_disconnect_and_reconnect_change_status_without_synthetic_samples()
    {
        var clock = new ManualReceiptClock();
        var source = new FakeTelemetrySource(clock);
        Assert.Equal(TelemetrySourceStatus.Disconnected, source.Status);
        Assert.False(source.TryRead(out _));
        source.Connect();
        source.Enqueue(Raw());
        source.Pause();
        clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(TelemetrySourceStatus.Paused, source.Status);
        Assert.False(source.TryRead(out _));
        source.Resume();
        Assert.True(source.TryRead(out var original));
        Assert.Equal(TimeSpan.Zero, original!.ReceiptTime);
        Assert.False(source.TryRead(out _));
        source.Enqueue(Raw());
        source.Disconnect();
        Assert.False(source.TryRead(out _));
        source.Connect();
        source.Enqueue(Raw() with { StreamEpoch = "epoch-2", SampleSequence = 1,
            SourceTime = TimeSpan.Zero });
        Assert.True(source.TryRead(out var reconnected));
        Assert.Equal(TimeSpan.FromMinutes(10), reconnected!.ReceiptTime);
        Assert.Equal("epoch-2", reconnected.Sample.StreamEpoch);
        Assert.Equal(TimeSpan.Zero, reconnected.Sample.SourceTime);
    }

    [Fact]
    public void Fake_source_is_repeatable_and_clock_rejects_reversal()
    {
        static ReceivedRawTelemetrySample[] Run()
        {
            var clock = new ManualReceiptClock();
            var source = new FakeTelemetrySource(clock);
            source.Connect();
            source.Enqueue(Raw());
            clock.Advance(TimeSpan.FromTicks(3));
            source.Enqueue(Raw() with { SampleSequence = 2 });
            Assert.True(source.TryRead(out var one));
            Assert.True(source.TryRead(out var two));
            return [one!, two!];
        }

        Assert.Equal(Run(), Run());
        Assert.Throws<ArgumentOutOfRangeException>(() => new ManualReceiptClock().Advance(TimeSpan.FromTicks(-1)));
    }

    private static void AssertUnavailable(TelemetryValidity validity, TelemetryProvenance provenance, object? value)
    {
        Assert.Equal(TelemetryValidity.Unavailable, validity);
        Assert.Equal(TelemetryProvenance.Unavailable, provenance);
        Assert.Null(value);
    }
}
