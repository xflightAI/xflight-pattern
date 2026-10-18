namespace XFlight.Pattern.SimConnect;

// Callbacks are synchronous within ReceiveMessages; the connection owns serialization.
internal interface ISimConnectSession : IDisposable
{
    void ReceiveMessages(Action<ConnectionMessage> receive);
}

internal enum ConnectionMessageKind { Open, Quit, Exception }

internal readonly record struct ConnectionMessage(ConnectionMessageKind Kind, Exception? Error = null);
