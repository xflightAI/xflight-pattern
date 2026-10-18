using XFlight.Pattern.SimConnect;
using XFlight.Pattern.Telemetry;

namespace XFlight.Pattern.Integration.Tests;

public sealed class SimConnectConnectionTests
{
    [Fact]
    public void Connection_is_confirmed_only_by_open_callback()
    {
        var session = new FakeSession();
        int attempts = 0;
        using var connection = new SimConnectConnection(() => { attempts++; return session; });
        Assert.Equal(SimConnectConnectionState.Disconnected, connection.State);
        connection.Pump();
        Assert.Equal(0, attempts);

        connection.Connect();
        connection.Connect();
        Assert.Equal(1, attempts);
        Assert.Equal(SimConnectConnectionState.Connecting, connection.State);
        Assert.Equal(TelemetrySourceStatus.Disconnected, connection.Status);
        connection.Pump();
        Assert.Equal(SimConnectConnectionState.Connecting, connection.State);

        session.Messages.Enqueue(new(ConnectionMessageKind.Open));
        connection.Pump();
        Assert.Equal(SimConnectConnectionState.Connected, connection.State);
        Assert.Equal(TelemetrySourceStatus.Connected, connection.Status);
        Assert.Null(connection.LastError);
        connection.Connect();
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Failed_connection_is_reported_and_can_be_retried()
    {
        var failure = new InvalidOperationException("open failed");
        var session = new FakeSession();
        int attempts = 0;
        using var connection = new SimConnectConnection(() => ++attempts == 1 ? throw failure : session);
        connection.Connect();
        Assert.Equal(SimConnectConnectionState.Disconnected, connection.State);
        Assert.Equal(TelemetrySourceStatus.Disconnected, connection.Status);
        Assert.Same(failure, connection.LastError);
        connection.Pump();

        connection.Connect();
        Assert.Null(connection.LastError);
        session.Messages.Enqueue(new(ConnectionMessageKind.Open));
        connection.Pump();
        Assert.Equal(SimConnectConnectionState.Connected, connection.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Disconnect_closes_once_and_reconnect_uses_a_fresh_session(bool confirmOpen)
    {
        var first = new FakeSession();
        var second = new FakeSession();
        int attempts = 0;
        using var connection = new SimConnectConnection(() => ++attempts == 1 ? first : second);
        connection.Disconnect();
        connection.Connect();
        if (confirmOpen)
        {
            first.Messages.Enqueue(new(ConnectionMessageKind.Open));
            connection.Pump();
        }
        connection.Disconnect();
        connection.Disconnect();
        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(SimConnectConnectionState.Disconnected, connection.State);
        Assert.Equal(TelemetrySourceStatus.Disconnected, connection.Status);

        first.Messages.Enqueue(new(ConnectionMessageKind.Open));
        connection.Connect();
        connection.Pump();
        Assert.Equal(SimConnectConnectionState.Connecting, connection.State);
        second.Messages.Enqueue(new(ConnectionMessageKind.Open));
        connection.Pump();
        Assert.Equal(SimConnectConnectionState.Connected, connection.State);
        Assert.Equal(2, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quit_disconnects_after_receive_returns_and_allows_reconnect(bool confirmOpen)
    {
        var session = new FakeSession();
        var next = new FakeSession();
        int attempts = 0;
        using var connection = new SimConnectConnection(() => ++attempts == 1 ? session : next);
        connection.Connect();
        if (confirmOpen)
        {
            session.Messages.Enqueue(new(ConnectionMessageKind.Open));
            connection.Pump();
        }
        session.Messages.Enqueue(new(ConnectionMessageKind.Quit));
        connection.Pump();
        Assert.Equal(SimConnectConnectionState.Disconnected, connection.State);
        Assert.Equal(TelemetrySourceStatus.Disconnected, connection.Status);
        Assert.Equal(1, session.DisposeCount);
        Assert.Null(connection.LastError);
        connection.Pump();
        connection.Connect();
        Assert.Equal(SimConnectConnectionState.Connecting, connection.State);
    }

    [Fact]
    public void Quit_wins_over_open_in_the_same_dispatch()
    {
        var session = new FakeSession();
        session.Messages.Enqueue(new(ConnectionMessageKind.Quit));
        session.Messages.Enqueue(new(ConnectionMessageKind.Open));
        using var connection = new SimConnectConnection(() => session);
        connection.Connect();
        connection.Pump();
        Assert.Equal(SimConnectConnectionState.Disconnected, connection.State);
        Assert.Equal(1, session.DisposeCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Receive_failure_or_exception_callback_disconnects_and_preserves_primary_error(bool callback)
    {
        var failure = new InvalidOperationException("receive failure");
        var session = new FakeSession { DisposeFailure = new Exception("cleanup failure") };
        if (callback) session.Messages.Enqueue(new(ConnectionMessageKind.Exception, failure));
        else session.ReceiveFailure = failure;
        using var connection = new SimConnectConnection(() => session);
        connection.Connect();
        connection.Pump();
        Assert.Same(failure, connection.LastError);
        Assert.Equal(SimConnectConnectionState.Disconnected, connection.State);
        Assert.Equal(1, session.DisposeCount);
        connection.Disconnect();
        Assert.Equal(1, session.DisposeCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Disposal_is_terminal_idempotent_and_closes_any_owned_session(int lifecycle)
    {
        var session = new FakeSession();
        var connection = new SimConnectConnection(() => session);
        if (lifecycle > 0) connection.Connect();
        if (lifecycle > 1)
        {
            session.Messages.Enqueue(new(ConnectionMessageKind.Open));
            connection.Pump();
        }
        connection.Dispose();
        connection.Dispose();
        connection.Disconnect();
        Assert.Equal(SimConnectConnectionState.Disposed, connection.State);
        Assert.Equal(TelemetrySourceStatus.Disconnected, connection.Status);
        Assert.Equal(lifecycle == 0 ? 0 : 1, session.DisposeCount);
        Assert.Throws<ObjectDisposedException>(connection.Connect);
        Assert.Throws<ObjectDisposedException>(connection.Pump);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cleanup_failure_is_reported_without_reusing_or_double_closing_session(bool dispose)
    {
        var failure = new Exception("close failed");
        var session = new FakeSession { DisposeFailure = failure };
        using var connection = new SimConnectConnection(() => session);
        connection.Connect();
        if (dispose) connection.Dispose();
        else connection.Disconnect();
        Assert.Same(failure, connection.LastError);
        Assert.Equal(dispose ? SimConnectConnectionState.Disposed : SimConnectConnectionState.Disconnected,
            connection.State);
        connection.Disconnect();
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public void No_lifecycle_transition_fabricates_telemetry()
    {
        var session = new FakeSession();
        using var connection = new SimConnectConnection(() => session);
        ITelemetrySource source = connection;
        AssertEmpty();
        connection.Connect();
        AssertEmpty();
        session.Messages.Enqueue(new(ConnectionMessageKind.Open));
        connection.Pump();
        AssertEmpty();
        session.Messages.Enqueue(new(ConnectionMessageKind.Quit));
        connection.Pump();
        AssertEmpty();
        connection.Disconnect();
        connection.Dispose();
        AssertEmpty();

        void AssertEmpty()
        {
            Assert.False(source.TryRead(out var sample));
            Assert.Null(sample);
        }
    }

    private sealed class FakeSession : ISimConnectSession
    {
        public Queue<ConnectionMessage> Messages { get; } = new();
        public Exception? ReceiveFailure { get; set; }
        public Exception? DisposeFailure { get; set; }
        public int DisposeCount { get; private set; }
        private bool receiving;

        public void ReceiveMessages(Action<ConnectionMessage> receive)
        {
            Assert.Equal(0, DisposeCount);
            Assert.False(receiving);
            receiving = true;
            try
            {
                if (ReceiveFailure is not null) throw ReceiveFailure;
                while (Messages.TryDequeue(out var message)) receive(message);
            }
            finally { receiving = false; }
        }

        public void Dispose()
        {
            Assert.False(receiving); // Closing within a native callback is forbidden.
            DisposeCount++;
            if (DisposeFailure is not null) throw DisposeFailure;
        }
    }
}
