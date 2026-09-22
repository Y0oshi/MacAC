using System.Net;
using System.Net.Sockets;

namespace MacAC.Wire;

public sealed class WireClient : IDisposable
{
    private readonly UdpClient _udp;
    private readonly IPEndPoint _distant;
    private readonly IPEndPoint _anyone = new(IPAddress.Any, 0);
    private int _appliedTimeoutMs = int.MinValue;

    public WireClient(IPEndPoint distant)
    {
        _distant = distant;
        _udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));

        if (OperatingSystem.IsWindows())
        {
            // Stop ICMP port-unreachable replies surfacing as receive errors
            const int SioUdpConnRestart = -1744830452;
            _udp.Client.IOControl((IOControlCode)SioUdpConnRestart, [0], null);
        }
    }

    public IPEndPoint OwnFinishPt => (IPEndPoint)_udp.Client.LocalEndPoint!;

    public IPEndPoint DistantFinishPt => _distant;

    public void Send(ReadOnlySpan<byte> datagram) => _udp.Client.SendTo(datagram, SocketFlags.None, _distant);

    public void Send(IPEndPoint distant, ReadOnlySpan<byte> datagram) => _udp.Client.SendTo(datagram, SocketFlags.None, distant);

    /// <summary>Returns the datagram length, or -1 on timeout. A zero timeout only polls.</summary>
    public int Receive(Span<byte> dest, TimeSpan timeout, out IPEndPoint? from)
    {
        if (timeout == TimeSpan.Zero && !_udp.Client.Poll(0, SelectMode.SelectRead))
        {
            from = null;
            return -1;
        }
        ImposeTimeout(timeout);

        try
        {
            EndPoint sender = _anyone;
            int len = _udp.Client.ReceiveFrom(dest, SocketFlags.None, ref sender);
            from = (IPEndPoint)sender;
            return len;
        }
        catch (SocketException exc) when (exc.SocketErrorCode == SocketError.TimedOut)
        {
            from = null;
            return -1;
        }
    }

    public async ValueTask<InboundVerdict> AcceptAsync(Memory<byte> dest, CancellationToken abortTicket)
    {
        var got = await _udp.Client.ReceiveFromAsync(dest, SocketFlags.None, _anyone, abortTicket).ConfigureAwait(false);
        return new InboundVerdict(got.ReceivedBytes, (IPEndPoint)got.RemoteEndPoint);
    }

    /// <summary>Allocating receive; null on timeout.</summary>
    public byte[]? Receive(TimeSpan timeout, out IPEndPoint? from)
    {
        ImposeTimeout(timeout);
        try
        {
            IPEndPoint sender = new(IPAddress.Any, 0);
            byte[] octets = _udp.Receive(ref sender);
            from = sender;
            return octets;
        }
        catch (SocketException exc) when (exc.SocketErrorCode == SocketError.TimedOut)
        {
            from = null;
            return null;
        }
    }

    /// <summary>Non-blocking receive; null when nothing is queued or the socket errors.</summary>
    public byte[]? TryTake(out IPEndPoint? from)
    {
        from = null;
        if (_udp.Available is 0)
            return null;
        try
        {
            IPEndPoint sender = new(IPAddress.Any, 0);
            byte[] octets = _udp.Receive(ref sender);
            from = sender;
            return octets;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    public void Dispose() => _udp.Dispose();

    private void ImposeTimeout(TimeSpan timeout)
    {
        int msec = checked((int)timeout.TotalMilliseconds);
        if (msec == _appliedTimeoutMs)
            return;
        _udp.Client.ReceiveTimeout = msec;
        _appliedTimeoutMs = msec;
    }
}

public readonly record struct InboundVerdict(int Length, IPEndPoint RemoteEndPoint);
