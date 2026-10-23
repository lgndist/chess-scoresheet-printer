using System.Net;
using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// chess-results.com'a HTTP istekleri yapar ve URL'leri kurar.
/// Sayfalar UTF-8 + HTML entity döner; gövde olduğu gibi okunur (parser entity çözer).
/// </summary>
public sealed class ChessResultsClient : IDisposable
{
    private const string BaseHost = "https://chess-results.com";
    private readonly HttpClient _http;

    public ChessResultsClient(HttpClient? http = null)
    {
        if (http is not null) { _http = http; return; }

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("tr,en;q=0.8");
        // Yenilemede (F5) ara sunucu/önbellekten eski tur bilgisi gelmesin.
        _http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        _http.DefaultRequestHeaders.Pragma.ParseAdd("no-cache");
    }

    /// <summary>
    /// Sayfayı indirir. chess-results yükte s1/s2/s3 sunucularına yönlendirir; bu sırada
    /// görülen geçici bağlantı hatalarında bir kez daha dener.
    /// </summary>
    public async Task<string> FetchAsync(string url, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            await Task.Delay(1500, ct).ConfigureAwait(false);
            return await _http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// chess-results turnuva arama formu (ASP.NET postback): önce sayfayı alıp gizli alanları
    /// (__VIEWSTATE vb.) okur, sonra verilen alanlarla birlikte geri gönderir.
    /// </summary>
    public async Task<string> PostSearchAsync(IDictionary<string, string> fields, CancellationToken ct = default)
    {
        const string url = BaseHost + "/TurnierSuche.aspx?lan=8";
        using var get = await _http.GetAsync(url, ct).ConfigureAwait(false);
        get.EnsureSuccessStatusCode();
        var page = await get.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var form = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(page, "<input type=\"hidden\" name=\"([^\"]+)\"[^>]*value=\"([^\"]*)\""))
            form[m.Groups[1].Value] = WebUtility.HtmlDecode(m.Groups[2].Value);
        foreach (var (k, v) in fields) form[k] = v;
        // Yönlendirme sonrası (s1/s2…) aynı sunucuya gönder.
        var target = get.RequestMessage?.RequestUri?.ToString() ?? url;
        using var post = await _http.PostAsync(target, new FormUrlEncodedContent(form), ct).ConfigureAwait(false);
        post.EnsureSuccessStatusCode();
        return await post.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
    }

    // ---- URL kurucular (lan=8 = Türkçe) ----
    public static string FederationUrl(string fed = "TUR") => $"{BaseHost}/fed.aspx?lan=8&fed={fed}";

    /// <summary>Belirli tur eşleştirmeleri (art=2 = eşleştirme listesi).</summary>
    public static string PairingsUrl(int tnr, int round)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=2&rd={round}&turdet=YES";

    /// <summary>Takım turnuvası: maç bazlı masa eşleştirmeleri (art=3), "1.1, 1.2 …".</summary>
    public static string TeamBoardsUrl(int tnr, int round)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=3&rd={round}&turdet=YES";

    /// <summary>Bir turdan sonraki sıralama (art=1&amp;rd=N).</summary>
    public static string StandingsUrl(int tnr, int round)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=1&rd={round}&turdet=YES";

    /// <summary>Başlangıç sıralaması (art=0): sporcu listesi, eşleştirme yayımlanmadan da vardır.</summary>
    public static string StartingListUrl(int tnr)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=0&turdet=YES";

    /// <summary>Tur tarihleri ve saatleri (art=14).</summary>
    public static string ScheduleUrl(int tnr)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=14&turdet=YES";

    /// <summary>Etkinlik genel sayfası (başlangıç sıralaması) — kategori/tur bilgisi içerir.</summary>
    public static string EventUrl(int tnr)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=1&turdet=YES";

    /// <summary>
    /// Kullanıcı girdisinden turnuva numarasını çıkarır.
    /// "1437263", "tnr1437263", tam URL (…/Tnr1437263.aspx?…) kabul eder.
    /// </summary>
    public static int? ParseTnr(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        var m = Regex.Match(input, @"[Tt]nr(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var t1)) return t1;

        // Düz sayı
        if (Regex.IsMatch(input, @"^\d+$") && int.TryParse(input, out var t2)) return t2;

        return null;
    }

    public void Dispose() => _http.Dispose();
}
