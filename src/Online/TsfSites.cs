using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Online;

/// <summary>TSF il sitesinde bulunan bir belge (yönerge vb.).</summary>
public sealed record TsfDocument(string Title, string Url)
{
    public bool IsWord => Url.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);
    public string FileName => Uri.UnescapeDataString(Url.Split('/', '?')[^1]);
    public override string ToString() => Title + (IsWord ? "  (Word)" : "  (PDF)");
}

/// <summary>
/// TSF il temsilciliklerinin siteleri (gömülü 81 il listesi, ayarlardan değiştirilebilir) ve bu
/// sitelerdeki yönergelerin bulunması/indirilmesi. Siteler ana sayfada en yeni haberleri ve
/// ekli yönergeleri (çoğunlukla PDF) en üstte listeler.
/// </summary>
public sealed class TsfSites : IDisposable
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> BuiltIn = new(LoadBuiltIn);
    private readonly HttpClient _http;

    public TsfSites(HttpClient? http = null)
    {
        // Yerel DNS tsf.org.tr adlarını çözemezse yedek çözümleme (bkz. FallbackDns).
        _http = http ?? new HttpClient(FallbackDns.Default.CreateHandler()) { Timeout = TimeSpan.FromSeconds(25) };
        if (http is null)
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ChessScoresheetPrinter");
            _http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        }
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Gömülü liste: il → site kökü ("https://isparta.tsf.org.tr").</summary>
    public static IReadOnlyDictionary<string, string> All => BuiltIn.Value;

    private static IReadOnlyDictionary<string, string> LoadBuiltIn()
    {
        using var s = typeof(TsfSites).Assembly.GetManifestResourceStream("NotasyonOtomasyonu.Online.tsf_il_siteleri.json");
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (s is null) return d;
        using var doc = JsonDocument.Parse(s);
        foreach (var p in doc.RootElement.GetProperty("siteler").EnumerateObject())
            d[p.Name] = p.Value.GetString() ?? "";
        return d;
    }

    /// <summary>İlin sitesi: ayardaki özel adres, yoksa gömülü liste.</summary>
    public static string? SiteFor(string? province, IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (string.IsNullOrWhiteSpace(province)) return null;
        if (overrides is not null && overrides.TryGetValue(province, out var o) && !string.IsNullOrWhiteSpace(o)) return o.TrimEnd('/');
        return All.TryGetValue(province, out var u) ? u.TrimEnd('/') : null;
    }

    /// <summary>Sitedeki yönergeler, sayfadaki sırayla (en yeni üstte). Yönerge dışı belgeler (prosedür, talimat) atılır.</summary>
    public async Task<List<TsfDocument>> ListYonergelerAsync(string siteUrl, CancellationToken ct = default)
    {
        var html = await GetStringAsync(siteUrl + "/", ct).ConfigureAwait(false);
        return ParseDocuments(html, siteUrl);
    }

    public static List<TsfDocument> ParseDocuments(string html, string siteUrl)
    {
        var list = new List<TsfDocument>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Regex.Matches(html, @"<a\b[^>]*href=""([^""]+\.(?:pdf|docx))""[^>]*>(.*?)</a>",
                                          RegexOptions.IgnoreCase | RegexOptions.Singleline))
        {
            var href = WebUtility.HtmlDecode(m.Groups[1].Value);
            var url = href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : siteUrl.TrimEnd('/') + "/" + href.TrimStart('/');
            if (!seen.Add(url)) continue;
            // Yalnız ilin kendi sitesindeki belgeler (federasyon/FIDE mevzuat bağlantıları değil).
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !Uri.TryCreate(siteUrl, UriKind.Absolute, out var site)
                || !u.Host.EndsWith(site.Host.Replace("www.", ""), StringComparison.OrdinalIgnoreCase)) continue;
            var file = Uri.UnescapeDataString(url.Split('/')[^1]);
            var fold = EventGrouping.Fold(file);
            // Bazı iller yönergeyi turnuva adıyla yayımlıyor ("Ankara_Eyll_Ay_UKD_Satran_Turnuvas.pdf").
            if (!Regex.IsMatch(fold, "yonerge|ynerge|turnuv|birincil|brncl|kupas|sampiyona")) continue;
            if (Regex.IsMatch(fold, "prosedur|talimat|cerceve|ereve|kayit|liste|harc|bedel|takvim|sonuc|eslestir|klavuz|kilavuz|rehber|kural|tutanak|basvuru_formu|dilekce"))
                continue; // genel mevzuat, kayıt listesi vb. — turnuva yönergesi değil
            var text = WebUtility.HtmlDecode(Regex.Replace(m.Groups[2].Value, "<[^>]+>", " ")).Trim();
            text = Regex.Replace(text, @"\s+", " ");
            // "Turnuva Yönergesi", "Tıklayınız" gibi genel bağlantı yazısı yerine dosya adı kullanılır.
            var specific = Regex.Split(EventGrouping.Fold(text), @"[^a-z0-9]+")
                                .Count(w => w.Length > 2 && !GenericWords.Contains(w));
            var title = specific >= 2 ? text : Prettify(file);
            list.Add(new TsfDocument(title, url));
        }
        return list;
    }

    private static readonly HashSet<string> GenericWords = new()
    {
        "turnuva", "turnuvasi", "yonerge", "yonergesi", "ve", "programi", "program", "tiklayiniz", "tikla",
        "indir", "icin", "buraya", "link", "dosya", "belge", "ek", "son"
    };

    /// <summary>
    /// "ISPARTA_EKM_AYI_UKD_SATRAN_TURNUVASI_YNERGES.pdf" → "ISPARTA EKİM AYI UKD SATRANÇ TURNUVASI YÖNERGESİ"
    /// (site Türkçe harfleri dosya adından düşürüyor; sık sözcükler onarılır).
    /// </summary>
    public static string Prettify(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file).Replace('_', ' ').Replace('-', ' ');
        name = Regex.Replace(name, @"\s+", " ").Trim();
        foreach (var (bad, good) in Repairs)
            name = Regex.Replace(name, $@"\b{bad}\b", good, RegexOptions.IgnoreCase);
        return name;
    }

    private static readonly (string, string)[] Repairs =
    {
        ("SATRAN", "SATRANÇ"), ("YNERGES", "YÖNERGESİ"), ("YNERGESI", "YÖNERGESİ"), ("TURNUVAS", "TURNUVASI"),
        ("EKM", "EKİM"), ("HAFTAS", "HAFTASI"), ("LKRETM", "İLKÖĞRETİM"), ("GENLK", "GENÇLİK"),
        ("AMATR", "AMATÖR"), ("CUMHURYET", "CUMHURİYET"), ("BRNCL", "BİRİNCİLİĞİ")
    };

    public async Task<byte[]> DownloadAsync(string url, CancellationToken ct = default)
    {
        try { return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false); }
        catch (HttpRequestException) when (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return await _http.GetByteArrayAsync("http://" + url["https://".Length..], ct).ConfigureAwait(false);
        }
    }

    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        try { return await _http.GetStringAsync(url, ct).ConfigureAwait(false); }
        catch (HttpRequestException) when (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            // Bazı il sitelerinin sertifikası sorunlu olabiliyor: düz http ile dene.
            return await _http.GetStringAsync("http://" + url["https://".Length..], ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Belgenin bu turnuvaya ait olma puanı (0–1): başlık ile turnuva adının ortak anlamlı sözcükleri.
    /// </summary>
    public static double MatchScore(TsfDocument doc, string eventName)
        => Math.Max(MatchScore(doc.Title, eventName), MatchScore(Prettify(doc.FileName), eventName));

    public static double MatchScore(string docTitle, string eventName)
    {
        static HashSet<string> Words(string s) => Regex.Split(EventGrouping.Fold(s), @"[^a-z0-9]+")
            .Where(w => w.Length >= 3 && !Stop.Contains(w)).Select(w => w.Length > 5 ? w[..5] : w).ToHashSet();
        var a = Words(docTitle);
        var b = Words(EventGrouping.BaseName(eventName));
        if (a.Count == 0 || b.Count == 0) return 0;
        // Jaccard: "Ankara Eylül Ayı UKD" ile "Isparta Ekim Ayı UKD" yalnız "ayı, ukd" paylaşır → düşük puan.
        return a.Intersect(b).Count() / (double)a.Union(b).Count();
    }

    // PDF adlarında Türkçe harfler düşmüş olabilir ("SATRAN", "YNERGES"): ilk 5 harf karşılaştırılır.
    private static readonly HashSet<string> Stop = new()
    {
        "satranc", "satran", "turnuvasi", "turnuvas", "turnu", "yonergesi", "ynerges", "yonerge", "ynerge", "yoner",
        "pdf", "docx", "son", "ile", "icin"
    };
}
