using SdkConnection = Microsoft.FlightSimulator.SimConnect.SimConnect;

namespace XFlight.Pattern.SimConnect;

internal sealed class SdkSimConnectSession : ISimConnectSession
{
    private readonly SdkConnection connection;
    private Action<ConnectionMessage>? receiver;
    private bool disposed;

    public SdkSimConnectSession()
    {
        SdkConnection? opened = null;
        try
        {
            // Official polling mode: no window or event handle. Each Pump services
            // the queue, so coalesced notifications cannot strand a later Quit.
            opened = new SdkConnection("XFlight Pattern connection proof", IntPtr.Zero, 0, null, 0);
            opened.OnRecvOpen += (_, _) => receiver?.Invoke(new(ConnectionMessageKind.Open));
            opened.OnRecvQuit += (_, _) => receiver?.Invoke(new(ConnectionMessageKind.Quit));
            opened.OnRecvException += (_, data) => receiver?.Invoke(new(ConnectionMessageKind.Exception,
                new InvalidOperationException($"SimConnect exception {data.dwException}; send ID {data.dwSendID}; index {data.dwIndex}.")));
            connection = opened;
        }
        catch
        {
            opened?.Dispose();
            throw;
        }
    }

    public void ReceiveMessages(Action<ConnectionMessage> receive)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        receiver = receive;
        try { connection.ReceiveMessage(); }
        finally { receiver = null; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        connection.Dispose();
    }
}
