using System.Net;
using System.Net.Sockets;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// Yerel DNS TSF adını çözemeyince DNS-over-HTTPS yedeği (sahte DoH sunucusu ve sahte TSF sitesiyle).
/// "localhost" kullanılır: yerel çözümleme testte bilerek başarısız kılınır.
/// </summary>
public class FallbackDnsTests
{
    private const string Cname = """
        {"Status":0,"Answer":[{"name":"www.isparta.tsf.org.tr","type":5,"TTL":14400,"data":"isparta.tsf.org.tr."},
                              {"name":"isparta.tsf.org.tr","type":1,"TTL":14400,"data":"185.8.32.34"}]}
        """;

    private const string TsfList = """<a href="/turnuva-kayit-form?layout=girisform&amp;tid=358">Isparta Küçükler İl Birinciliği 2026</a>""";

    private static LocalServer DohServer(string ip = "127.0.0.1") => new(_ => LocalServer.Text(
        $$"""{"Status":0,"Answer":[{"name":"localhost","type":1,"TTL":60,"data":"{{ip}}"}]}"""));

    private static Task<IPAddress[]> Broken(string host, CancellationToken ct) => throw new SocketException((int)SocketError.HostNotFound);

    [Fact]
    public void ParseDohJson_FollowsCnameToAddress()
        => Assert.Equal(new[] { IPAddress.Parse("185.8.32.34") }, FallbackDns.ParseDohJson(Cname));

    [Fact]
    public void ParseDohJson_NoAnswer_IsEmpty()
        => Assert.Empty(FallbackDns.ParseDohJson("""{"Status":3}"""));

    [Fact]
    public async Task LocalDnsFails_SiteIsReadThroughDoh()
    {
        using var doh = DohServer();
        using var site = new LocalServer(_ => LocalServer.Text(TsfList));
        var dns = new FallbackDns(Broken, new[] { doh.BaseUrl + "/doh?name={0}" });
        string? used = null;
        dns.FallbackUsed += h => used = h;
        using var sites = new TsfSites(new HttpClient(dns.CreateHandler()));

        var open = await sites.ListOpenAsync(site.BaseUrl);

        Assert.Equal(358, Assert.Single(open).Tid);
        Assert.Equal("localhost", used);
        Assert.Equal(1, doh.Requests);
    }

    [Fact]
    public async Task HangingLocalDns_TimesOutThenFallsBack()
    {
        using var doh = DohServer();
        var dns = new FallbackDns(async (h, ct) => { await Task.Delay(Timeout.Infinite, ct); return Array.Empty<IPAddress>(); },
                                  new[] { doh.BaseUrl + "/doh?name={0}" }, TimeSpan.FromMilliseconds(200));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var addrs = await dns.ResolveAsync("www.isparta.tsf.org.tr");
        Assert.Equal(IPAddress.Loopback, Assert.Single(addrs));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"çok uzun sürdü: {sw.Elapsed}");
    }

    [Fact]
    public async Task FoundAddress_IsCached_LocalDnsNotRetried()
    {
        using var doh = DohServer();
        int calls = 0;
        var dns = new FallbackDns((h, ct) => { calls++; return Broken(h, ct); }, new[] { doh.BaseUrl + "/doh?name={0}" });
        await dns.ResolveAsync("a.tsf.org.tr");
        await dns.ResolveAsync("a.tsf.org.tr");
        Assert.Equal(1, calls);
        Assert.Equal(1, doh.Requests);
    }

    [Fact]
    public async Task WorkingLocalDns_DohNotUsed()
    {
        using var doh = DohServer();
        var dns = new FallbackDns((h, ct) => Task.FromResult(new[] { IPAddress.Loopback }), new[] { doh.BaseUrl + "/doh?name={0}" });
        Assert.Equal(IPAddress.Loopback, Assert.Single(await dns.ResolveAsync("x.tsf.org.tr")));
        Assert.Equal(0, doh.Requests);
    }

    [Fact]
    public async Task FirstDohDown_SecondUsed()
    {
        using var doh = DohServer();
        var dns = new FallbackDns(Broken, new[] { "http://localhost:1/doh?name={0}", doh.BaseUrl + "/doh?name={0}" });
        Assert.Equal(IPAddress.Loopback, Assert.Single(await dns.ResolveAsync("y.tsf.org.tr")));
    }

    [Fact]
    public async Task EverythingDown_ReportsNetworkError()
    {
        var dns = new FallbackDns(Broken, new[] { "http://localhost:1/doh?name={0}" });
        using var sites = new TsfSites(new HttpClient(dns.CreateHandler()));
        // TsfNet bunu "TSF il sitesine ulaşılamıyor" diye gösterir
        await Assert.ThrowsAsync<HttpRequestException>(() => sites.ListOpenAsync("http://www.isparta.tsf.org.tr"));
    }
}
