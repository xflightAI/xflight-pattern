namespace XFlight.Pattern.Telemetry;

/// <summary>Manually driven clock for deterministic receipt-time tests.</summary>
public sealed class ManualReceiptClock : IReceiptClock
{
    public TimeSpan Elapsed { get; private set; }

    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        Elapsed = checked(Elapsed + duration);
    }
}

/// <summary>Single-threaded, FIFO fake. Status changes never emit samples.</summary>
public sealed class FakeTelemetrySource : ITelemetrySource
{
    private readonly Queue<ReceivedRawTelemetrySample> samples = new();
    private readonly IReceiptClock clock;

    public FakeTelemetrySource(IReceiptClock clock)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public TelemetrySourceStatus Status { get; private set; } = TelemetrySourceStatus.Disconnected;

    public void Connect() => Status = TelemetrySourceStatus.Connected;
    public void Pause()
    {
        if (Status != TelemetrySourceStatus.Connected) throw new InvalidOperationException("Source is not connected.");
        Status = TelemetrySourceStatus.Paused;
    }
    public void Resume()
    {
        if (Status != TelemetrySourceStatus.Paused) throw new InvalidOperationException("Source is not paused.");
        Status = TelemetrySourceStatus.Connected;
    }
    public void Disconnect()
    {
        Status = TelemetrySourceStatus.Disconnected;
        samples.Clear();
    }

    public void Enqueue(RawTelemetrySample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        if (Status != TelemetrySourceStatus.Connected) throw new InvalidOperationException("Source is not connected.");
        if (clock.Elapsed < TimeSpan.Zero) throw new InvalidOperationException("Receipt time cannot be negative.");
        samples.Enqueue(new ReceivedRawTelemetrySample(sample, clock.Elapsed));
    }

    public bool TryRead(out ReceivedRawTelemetrySample? sample)
    {
        if (Status != TelemetrySourceStatus.Connected || samples.Count == 0)
        {
            sample = null;
            return false;
        }
        sample = samples.Dequeue();
        return true;
    }
}
