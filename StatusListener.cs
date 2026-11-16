using System;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;

namespace XlaAndroidHelper;

/// <summary>
/// Receives device status from the XIVLauncher Android app.
///
/// The app and the game are the same Android process tree, so loopback UDP reaches straight from Wine to the app
/// with no bridge of its own. This is the same shape as the existing mouse helper: a fixed port, small datagrams,
/// no handshake and nothing to clean up if either side disappears. On a desktop the port simply stays quiet, which
/// is how the plugin knows to keep out of the way.
///
/// It also carries text typed on the phone's keyboard, which is the same direction on the same port. Text is handed
/// to <see cref="TextReceived"/> rather than stored, because it has to be written to the game on the framework
/// thread and this runs on a socket thread.
/// </summary>
public sealed class StatusListener : IDisposable
{
    /// <summary>Chosen next to the mouse helper's pair (7946/7947) so the range stays recognisable.</summary>
    public const int Port = 7948;

    private readonly UdpClient? socket;
    private readonly CancellationTokenSource stopping = new();

    /// <summary>The newest reading, or null while nothing has arrived.</summary>
    public DeviceStatus? Latest { get; private set; }

    /// <summary>Why there is no reading, when the socket could not be opened at all.</summary>
    public string? Error { get; }

    /// <summary>Raised on the socket thread when the app sends text the player typed. Never touch game memory here.</summary>
    public event Action<DeviceStatus>? TextReceived;

    public StatusListener()
    {
        try
        {
            socket = new UdpClient(new IPEndPoint(IPAddress.Loopback, Port));
        }
        catch (SocketException ex)
        {
            // Another copy of the plugin, or something else on the port. Not fatal: the plugin just shows nothing.
            Error = ex.Message;
            return;
        }
        new Thread(Receive) { IsBackground = true, Name = "XlaAndroidHelper status" }.Start();
    }

    private void Receive()
    {
        var from = new IPEndPoint(IPAddress.Loopback, 0);
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                var datagram = socket!.Receive(ref from);
                var status = JsonSerializer.Deserialize<DeviceStatus>(datagram);
                if (status == null)
                    continue;
                if (status.Text != null)
                {
                    TextReceived?.Invoke(status);
                    continue;
                }
                status.Received = DateTime.UtcNow;
                Latest = status;
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
            {
                return; // closed by Dispose
            }
            catch (JsonException)
            {
                // A datagram we do not understand is not a reason to stop listening.
            }
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        socket?.Dispose();
        stopping.Dispose();
    }
}
