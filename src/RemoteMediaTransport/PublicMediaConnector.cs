using System.Net;
using System.Net.Sockets;

namespace WidgetRail.RemoteMediaTransport;

internal static class PublicMediaConnector
{
    internal static ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token) =>
        ConnectAsync(context.DnsEndPoint, Dns.GetHostAddressesAsync, OpenSocketAsync, token);

    internal static async ValueTask<Stream> ConnectAsync(DnsEndPoint endpoint,
        Func<string, CancellationToken, Task<IPAddress[]>> resolve,
        Func<IPEndPoint, CancellationToken, ValueTask<Stream>> connect, CancellationToken token)
    {
        if (endpoint.Port != 443) throw new PublicMediaException(PublicMediaFailure.DestinationRejected);
        var addresses = await resolve(endpoint.Host, token).ConfigureAwait(false);
        if (addresses.Length is 0 or > 32 || addresses.Any(address => !PublicMediaAddress.IsPublic(address)))
            throw new PublicMediaException(PublicMediaFailure.DestinationRejected);
        // Resolve once and dial the checked address, never the hostname again.
        // SocketsHttpHandler still owns TLS/SNI/certificate validation for Host.
        foreach (var address in addresses.Distinct())
        {
            token.ThrowIfCancellationRequested();
            try { return await connect(new(address, endpoint.Port), token).ConfigureAwait(false); }
            catch (SocketException) { }
        }
        throw new PublicMediaException(PublicMediaFailure.Network);
    }

    private static async ValueTask<Stream> OpenSocketAsync(IPEndPoint endpoint, CancellationToken token)
    {
        var socket = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(endpoint, token).ConfigureAwait(false);
            if (socket.RemoteEndPoint is not IPEndPoint remote || !remote.Equals(endpoint) || !PublicMediaAddress.IsPublic(remote.Address))
                throw new PublicMediaException(PublicMediaFailure.DestinationRejected);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }
}
