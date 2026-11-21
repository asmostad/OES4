using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;

namespace Oes.Infrastructure.Http;

public sealed class SafeFetchException(string message) : Exception(message);

/// <summary>Fetches text from a URL a user typed (calendar feeds, later images).</summary>
public interface ISafeFetcher
{
    Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct = default);
}

/// <summary>
/// Outbound HTTP to user-supplied URLs without SSRF: https on port 443 only; every address the name
/// resolves to must be public, and the connection goes to that checked address (no second lookup an
/// attacker could swap); redirects are followed only on the same host (three at most); responses are
/// capped in size and time. Errors are plain sentences for the person who typed the URL.
/// </summary>
public sealed class SafeHttpFetcher : ISafeFetcher
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>webcal:// becomes https://; anything but https on 443 is refused.</summary>
    public static Uri Normalize(string raw)
    {
        var s = (raw ?? "").Trim();
        if (s.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) s = "https://" + s["webcal://".Length..];
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new SafeFetchException("Use an https:// (or webcal://) link.");
        if (uri.Port != 443) throw new SafeFetchException("Only links on the standard https port are allowed.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new SafeFetchException("Links with a user name or password aren't allowed.");
        if (uri.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 && !IsPublic(IPAddress.Parse(uri.Host.Trim('[', ']'))))
            throw new SafeFetchException("That address isn't on the public internet.");
        return uri;
    }

    /// <summary>True only for globally routable unicast addresses.</summary>
    public static bool IsPublic(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224                 // this network, private, loopback, multicast/reserved
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)                       // carrier-grade NAT
                     || (b[0] == 169 && b[1] == 254)                                     // link-local (cloud metadata)
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 192 && b[1] == 0 && (b[2] == 0 || b[2] == 2))           // IETF, TEST-NET-1
                     || (b[0] == 198 && (b[1] == 18 || b[1] == 19))                      // benchmarking
                     || (b[0] == 198 && b[1] == 51 && b[2] == 100) || (b[0] == 203 && b[1] == 0 && b[2] == 113));
        }
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6Multicast || ip.Equals(IPAddress.IPv6Any)) return false;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return false;                                     // unique local fc00::/7
            if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8) return false; // documentation
            return (b[0] & 0xE0) == 0x20;                                               // global unicast 2000::/3
        }
        return false;
    }

    public async Task<string> GetStringAsync(string url, int maxBytes, CancellationToken ct = default)
    {
        var uri = Normalize(url);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            for (var hop = 0; hop < 4; hop++)
            {
                var address = await PublicAddressAsync(uri.IdnHost, timeout.Token);
                using var handler = new SocketsHttpHandler
                {
                    AllowAutoRedirect = false, UseCookies = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.All,
                    ConnectTimeout = TimeSpan.FromSeconds(8),
                    ConnectCallback = async (_, token) =>
                    {
                        var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        try { await socket.ConnectAsync(new IPEndPoint(address, 443), token); return new NetworkStream(socket, ownsSocket: true); }
                        catch { socket.Dispose(); throw; }
                    },
                };
                using var http = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Pich-Calendar", "1.0"));
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)res.StatusCode is >= 300 and < 400 && res.Headers.Location is { } location)
                {
                    var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    if (!string.Equals(next.Host, uri.Host, StringComparison.OrdinalIgnoreCase)) throw new SafeFetchException("The link redirects to another site.");
                    uri = Normalize(next.ToString());
                    continue;
                }
                if (!res.IsSuccessStatusCode) throw new SafeFetchException($"The calendar site answered {(int)res.StatusCode} ({res.ReasonPhrase}).");
                if (res.Content.Headers.ContentLength > maxBytes) throw new SafeFetchException("The calendar is too large.");
                await using var stream = await res.Content.ReadAsStreamAsync(timeout.Token);
                var buffer = new MemoryStream();
                var chunk = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
                {
                    if (buffer.Length + read > maxBytes) throw new SafeFetchException("The calendar is too large.");
                    buffer.Write(chunk, 0, read);
                }
                return Encoding.UTF8.GetString(buffer.ToArray());
            }
            throw new SafeFetchException("The link redirects too many times.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new SafeFetchException("The calendar site took too long to answer."); }
        catch (HttpRequestException) { throw new SafeFetchException("Couldn't reach the calendar site."); }
        catch (SocketException) { throw new SafeFetchException("Couldn't reach the calendar site."); }
    }

    private static async Task<IPAddress> PublicAddressAsync(string host, CancellationToken ct)
    {
        IPAddress[] addresses;
        try { addresses = await Dns.GetHostAddressesAsync(host, ct); }
        catch (SocketException) { throw new SafeFetchException("That site's name doesn't resolve."); }
        if (addresses.Length == 0) throw new SafeFetchException("That site's name doesn't resolve.");
        // Every answer must be public, so a name that mixes in a private address is refused outright.
        if (addresses.Any(a => !IsPublic(a))) throw new SafeFetchException("That address isn't on the public internet.");
        return addresses[0];
    }
}
