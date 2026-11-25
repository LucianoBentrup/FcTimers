using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace FcTimers;

// IPv4 primeiro, e cada endereço com 5 s para conectar. Em rede com IPv6 quebrado o padrão do .NET
// fica preso no IPv6 até o timeout e o serviço parece fora do ar.
public static class NetHttp
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    public static HttpClient Create(TimeSpan timeout) => new(new SocketsHttpHandler { ConnectCallback = Connect }) { Timeout = timeout };

    private static async ValueTask<System.IO.Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancel)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancel);
        Exception? last = null;
        foreach (var address in addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1))
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                attempt.CancelAfter(ConnectTimeout);
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception e) when (e is SocketException or OperationCanceledException && !cancel.IsCancellationRequested)
            {
                last = e;
                socket.Dispose();
            }
        }

        throw new HttpRequestException($"Sem conexão com {context.DnsEndPoint.Host}", last);
    }
}
