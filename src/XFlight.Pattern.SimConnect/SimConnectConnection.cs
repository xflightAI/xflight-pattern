using XFlight.Pattern.Telemetry;

namespace XFlight.Pattern.SimConnect;

public enum SimConnectConnectionState { Disconnected, Connecting, Connected, Disposed }

/// <summary>
/// Read-only, caller-driven connection proof. Call Pump to dispatch pending SDK
/// notifications. All SDK calls and callbacks share one lock; no worker is started.
/// </summary>
public sealed class SimConnectConnection : ITelemetrySource, IDisposable
{
    private readonly object gate = new();
    private readonly Func<ISimConnectSession> openSession;
    private ISimConnectSession? session;
    private SimConnectConnectionState state;
    private Exception? lastError;

    public SimConnectConnection() : this(OpenSdkSession) { }

    internal SimConnectConnection(Func<ISimConnectSession> openSession) =>
        this.openSession = openSession ?? throw new ArgumentNullException(nameof(openSession));

    public SimConnectConnectionState State { get { lock (gate) return state; } }
    public Exception? LastError { get { lock (gate) return lastError; } }
    public TelemetrySourceStatus Status => State == SimConnectConnectionState.Connected
        ? TelemetrySourceStatus.Connected : TelemetrySourceStatus.Disconnected;

    /// <summary>Requests a connection. Only an Open callback confirms Connected.</summary>
    public void Connect()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(state == SimConnectConnectionState.Disposed, this);
            if (state != SimConnectConnectionState.Disconnected) return;
            lastError = null;
            state = SimConnectConnectionState.Connecting;
            try { session = openSession(); }
            catch (Exception error)
            {
                lastError = error;
                state = SimConnectConnectionState.Disconnected;
            }
        }
    }

    /// <summary>Dispatches available messages without waiting for a simulator event.</summary>
    public void Pump()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(state == SimConnectConnectionState.Disposed, this);
            if (session is null) return;
            bool opened = false, quit = false;
            Exception? failure = null;
            try
            {
                session.ReceiveMessages(message =>
                {
                    switch (message.Kind)
                    {
                        case ConnectionMessageKind.Open: opened = true; break;
                        case ConnectionMessageKind.Quit: quit = true; break;
                        case ConnectionMessageKind.Exception:
                            failure ??= message.Error ?? new InvalidOperationException("SimConnect reported an exception.");
                            break;
                    }
                });
            }
            catch (Exception error) { failure ??= error; }

            // Never dispose the SDK inside ReceiveMessage or one of its callbacks.
            if (failure is not null || quit)
            {
                lastError = failure;
                CloseSession();
            }
            else if (opened) state = SimConnectConnectionState.Connected;
        }
    }

    public void Disconnect()
    {
        lock (gate)
        {
            if (state == SimConnectConnectionState.Disposed) return;
            CloseSession();
        }
    }

    // Lifecycle changes never fabricate telemetry; subscriptions belong to a later milestone.
    public bool TryRead(out ReceivedRawTelemetrySample? sample)
    {
        sample = null;
        return false;
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (state == SimConnectConnectionState.Disposed) return;
            CloseSession();
            state = SimConnectConnectionState.Disposed;
        }
    }

    private void CloseSession()
    {
        var closing = session;
        session = null;
        state = SimConnectConnectionState.Disconnected;
        try { closing?.Dispose(); }
        catch (Exception error) { lastError ??= error; }
    }

    private static ISimConnectSession OpenSdkSession()
    {
        if (!OperatingSystem.IsWindows() || !Environment.Is64BitProcess)
            throw new PlatformNotSupportedException("The official SimConnect SDK requires a Windows x64 process.");
        return new SdkSimConnectSession();
    }
}
