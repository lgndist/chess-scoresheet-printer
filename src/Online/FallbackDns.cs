using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// Yedek ad çözümleme. Bazı ağlarda yerel DNS sunucusu tsf.org.tr adlarını çözemiyor (TSF'nin ad
/// sunucularına ulaşamıyor) ama sitenin kendisi çalışıyor. O zaman ad DNS-over-HTTPS ile çözülür
/// (Cloudflare 1.1.1.1, olmazsa Google 8.8.8.8; ikisine de IP adresiyle gidilir, kendileri DNS
/// gerektirmez). Yalnız yerel çözümleme başarısız olunca ve yalnız site adı gönderilir; bulunan adres
/// yarım saat saklanır, bu sürede yerel DNS yeniden beklenmez.
/// </summary>
public sealed class FallbackDns
{
    private static readonly HttpClient Doh = CreateDohClient();
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _system;
    private readonly IReadOnlyList<string> _endpoints;
    private readonly TimeSpan _systemTimeout;
    private readonly ConcurrentDictionary<string, (DateTime Until, IPAddress[] Addrs)> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Yerel DNS çözemeyip yedek çözümleme kullanıldığında (site adıyla).</summary>
    public event Action<string>? FallbackUsed;

    public static readonly string[] PublicEndpoints =
    {
        "https://1.1.1.1/dns-query?name={0}&type=A",
        "https://8.8.8.8/resolve?name={0}&type=A"
    };

    // PublicEndpoints'ten sonra: alan başlatma sırası önemli.
    public static FallbackDns Default { get; } = new();

    public FallbackDns(Func<string, CancellationToken, Task<IPAddress[]>>? system = null,
                       IReadOnlyList<string>? endpoints = null, TimeSpan? systemTimeout = null)
    {
        _system = system ?? ((h, ct) => Dns.GetHostAddressesAsync(h, ct));
        _endpoints = endpoints ?? PublicEndpoints;
        _systemTimeout = systemTimeout ?? TimeSpan.FromSeconds(4);
    }

    private static HttpClient CreateDohClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        http.DefaultRequestHeaders.Accept.ParseAdd("application/dns-json");
        return http;
    }

    /// <summary>Bağlanırken adı bu sınıfla çözen HTTP işleyicisi.</summary>
    public SocketsHttpHandler CreateHandler() => new()
    {
        AutomaticDecompression = DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = ConnectAsync
    };

    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext ctx, CancellationToken ct)
    {
        Exception? last = null;
        foreach (var addr in await ResolveAsync(ctx.DnsEndPoint.Host, ct).ConfigureAwait(false))
        {
            var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(addr, ctx.DnsEndPoint.Port), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex) { socket.Dispose(); last = ex; }
            catch { socket.Dispose(); throw; }
        }
        throw last ?? new SocketException((int)SocketError.HostNotFound);
    }

    /// <summary>Adın adresleri: önce yerel DNS (kısa süre beklenir), çözemezse DNS-over-HTTPS.</summary>
    public async Task<IPAddress[]> ResolveAsync(string host, CancellationToken ct = default)
    {
        if (IPAddress.TryParse(host, out var ip)) return new[] { ip };
        if (_cache.TryGetValue(host, out var c) && c.Until > DateTime.UtcNow) return c.Addrs;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_systemTimeout);
            var addrs = await _system(host, cts.Token).ConfigureAwait(false);
            if (addrs.Length > 0) return addrs;
        }
        catch (Exception ex) when (ex is SocketException || (ex is OperationCanceledException && !ct.IsCancellationRequested)) { }

        foreach (var endpoint in _endpoints)
        {
            try
            {
                var json = await Doh.GetStringAsync(string.Format(endpoint, Uri.EscapeDataString(host)), ct).ConfigureAwait(false);
                var found = ParseDohJson(json);
                if (found.Length == 0) continue;
                _cache[host] = (DateTime.UtcNow.AddMinutes(30), found);
                FallbackUsed?.Invoke(host);
                return found;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested)) { }
        }
        throw new SocketException((int)SocketError.HostNotFound);
    }

    /// <summary>DNS-over-HTTPS JSON yanıtındaki IPv4 adresleri (CNAME zinciri atlanır).</summary>
    public static IPAddress[] ParseDohJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("Answer", out var answers) || answers.ValueKind != JsonValueKind.Array)
            return Array.Empty<IPAddress>();
        return answers.EnumerateArray()
            .Where(a => a.TryGetProperty("type", out var t) && t.GetInt32() == 1)
            .Select(a => IPAddress.TryParse(a.GetProperty("data").GetString(), out var addr) ? addr : null)
            .OfType<IPAddress>()
            .ToArray();
    }
}
