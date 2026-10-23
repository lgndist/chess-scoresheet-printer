using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>TSF il sitesinde kayda açık bir turnuva (kayıt formu numarası ve adı).</summary>
public sealed record RegistrationOption(int Tid, string Title)
{
    public override string ToString() => Title;
}

/// <summary>Bir kayıt formunun herkese açık sporcu listesi.</summary>
/// <summary>
/// Bir kayıt formunun sayfası. <paramref name="Closed"/>: kayıtlar sona ermiş (site "Turnuva Kayıtları Sona
/// Ermiştir" yazar, liste yine de gösterilir); <paramref name="ClosesAt"/>: sitedeki kayıt bitiş tarihi.
/// </summary>
public sealed record RegistrationPage(int Tid, string Title, int? Total, List<PlayerRegistration> Entries,
                                      bool Closed = false, DateTime? ClosesAt = null);

/// <summary>
/// TSF il sitelerindeki turnuva kayıt sayfaları ("/turnuva-kayit-form"). Liste sayfası
/// (layout=tlist) kayda açık turnuvaları verir; tek turnuva varsa doğrudan onun formu gelir.
/// Form sayfasında AD, SOYAD, İL, ELO, UKD, KATEGORİ sütunlu herkese açık kayıt listesi bulunur.
/// Kayıt süresi bitince form yerine turnuva başlığı, "Turnuva Kayıtları Sona Ermiştir" ve aynı liste
/// gösterilir (bir süre sonra o da kalkar). Dikkat: olmayan ya da çok eski bir numara istendiğinde site
/// hata vermeden başlıksız BAŞKA bir listeyi gösterir; bu yüzden yalnız başlığı "… KAYIT FORMU" olan ve
/// numarası tutan sayfa ya da başlıklı "kayıtlar sona ermiştir" sayfası kabul edilir.
/// </summary>
public static class TsfRegistrations
{
    private static readonly HtmlParser Parser = new();

    public static string ListUrl(string site) => site.TrimEnd('/') + "/turnuva-kayit-form?view=turnuvakayit&layout=tlist";
    public static string FormUrl(string site, int tid) => site.TrimEnd('/') + $"/turnuva-kayit-form?layout=girisform&tid={tid}";

    /// <summary>Kayda açık turnuvalar (sitedeki sırayla).</summary>
    public static async Task<List<RegistrationOption>> ListOpenAsync(this TsfSites sites, string site, CancellationToken ct = default)
        => ParseOptions(Decode(await sites.DownloadAsync(ListUrl(site), ct).ConfigureAwait(false)));

    /// <summary>Kayıt formunun listesi; sayfa bu numaraya ait değilse (kapanmış/yanlış) null.</summary>
    public static async Task<RegistrationPage?> GetPageAsync(this TsfSites sites, string site, int tid, CancellationToken ct = default)
        => ParsePage(Decode(await sites.DownloadAsync(FormUrl(site, tid), ct).ConfigureAwait(false)), tid);

    /// <summary>
    /// Kayıtları yeni kapanmış formlar: kapanan form sitedeki listeden düşer ama sayfası bir süre
    /// okunur. Form numaraları sırayla verildiğinden bilinen en büyük numaranın çevresi yoklanır;
    /// yalnız başlıklı "kayıtlar sona ermiştir" sayfaları döner (açık ya da yabancı sayfalar değil).
    /// </summary>
    public static async Task<List<RegistrationPage>> ListRecentlyClosedAsync(this TsfSites sites, string site, int aroundTid,
        IEnumerable<int>? skip = null, int below = 10, int above = 3, CancellationToken ct = default)
    {
        var known = skip?.ToHashSet() ?? new HashSet<int>();
        var list = new List<RegistrationPage>();
        for (int tid = aroundTid + above; tid >= Math.Max(1, aroundTid - below); tid--)
        {
            if (known.Contains(tid)) continue;
            RegistrationPage? page;
            try { page = await sites.GetPageAsync(site, tid, ct).ConfigureAwait(false); }
            catch (HttpRequestException) { continue; }
            if (page is { Closed: true, Entries.Count: > 0 }) list.Add(page);
        }
        return list;
    }

    /// <summary>"356", "tid=356" ya da tam kayıt linkinden form numarası.</summary>
    public static int? ParseTid(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var m = Regex.Match(input, @"tid=(\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return int.Parse(m.Groups[1].Value);
        return int.TryParse(input.Trim(), out var n) && n > 0 ? n : null;
    }

    /// <summary>Linkteki site kökü ("https://www.isparta.tsf.org.tr/turnuva-kayit-form?…" → "https://www.isparta.tsf.org.tr").</summary>
    public static string? SiteOf(string? url)
        => Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var u) && u.Host.EndsWith("tsf.org.tr", StringComparison.OrdinalIgnoreCase)
            ? $"{u.Scheme}://{u.Host}" : null;

    public static List<RegistrationOption> ParseOptions(string html)
    {
        var doc = Parser.ParseDocument(html);
        var list = new List<RegistrationOption>();
        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href") ?? "";
            if (!href.Contains("girisform", StringComparison.OrdinalIgnoreCase)) continue;
            var tid = ParseTid(href);
            var title = Clean(a.TextContent);
            if (tid is null || title.Length == 0 || list.Any(o => o.Tid == tid)) continue;
            list.Add(new RegistrationOption(tid.Value, title));
        }
        // Tek turnuva kayıttaysa liste yerine doğrudan formu gelir.
        if (list.Count == 0 && FormTitle(html) is { } t && HiddenTid(html) is int only)
            list.Add(new RegistrationOption(only, t));
        return list;
    }

    public static RegistrationPage? ParsePage(string html, int tid)
    {
        var title = FormTitle(html);
        bool closed = false;
        if (title is null || HiddenTid(html) != tid)
        {
            if (ClosedTitle(html) is not { } ct) return null; // başlıksız yabancı liste
            (title, closed) = (ct, true);
        }

        var doc = Parser.ParseDocument(html);
        var entries = new List<PlayerRegistration>();
        foreach (var table in doc.QuerySelectorAll("table"))
        {
            var rows = table.QuerySelectorAll("tr").ToList();
            if (rows.Count == 0) continue;
            var head = rows[0].Children.Where(c => c.TagName is "TD" or "TH").Select(c => EventGrouping.Fold(Clean(c.TextContent))).ToList();
            int iAd = head.IndexOf("ad"), iSoyad = head.IndexOf("soyad"), iIl = head.IndexOf("il");
            if (iAd < 0 || iSoyad < 0 || iIl < 0) continue;
            int iElo = head.IndexOf("elo"), iUkd = head.IndexOf("ukd"), iKat = head.IndexOf("kategori");
            foreach (var tr in rows.Skip(1))
            {
                var c = tr.Children.Where(x => x.TagName == "TD").Select(x => Clean(x.TextContent)).ToList();
                string At(int i) => i >= 0 && i < c.Count ? c[i] : "";
                if (At(iAd).Length == 0 && At(iSoyad).Length == 0) continue;
                entries.Add(new PlayerRegistration
                {
                    Given = At(iAd), Surname = At(iSoyad), Province = At(iIl),
                    Elo = Rating(At(iElo)), Ukd = Rating(At(iUkd)), Category = At(iKat), Tid = tid
                });
            }
            break;
        }
        var total = Regex.Match(html, @"Toplam\s+Kay\S*\s*:\s*(\d+)");
        var end = Regex.Match(html, @"Biti\S*\s+Tarihi\s*:\s*(\d{4}-\d{2}-\d{2}(?:\s+\d{2}:\d{2})?)");
        DateTime? closesAt = end.Success && DateTime.TryParse(end.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture,
                                                              System.Globalization.DateTimeStyles.None, out var d) ? d : null;
        return new RegistrationPage(tid, title, total.Success ? int.Parse(total.Groups[1].Value) : null, entries, closed, closesAt);
    }

    /// <summary>Kayıtları sona ermiş formun başlığı (başlığın hemen ardından "Turnuva Kayıtları Sona Ermiştir" gelir).</summary>
    private static string? ClosedTitle(string html)
    {
        var m = Regex.Match(html, @"<b>\s*([^<>]{3,250}?)\s*</b>\s*(?:</font>\s*)?(?:<br\s*/?>\s*)*<b>\s*Turnuva\s+Kay\S*\s+Sona\s+Er",
                            RegexOptions.IgnoreCase);
        return m.Success ? Clean(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)) : null;
    }

    private static string? FormTitle(string html)
    {
        var m = Regex.Match(html, @">\s*([^<>]{3,250}?)\s+KAYIT FORMU\s*<");
        return m.Success ? Clean(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value)) : null;
    }

    private static int? HiddenTid(string html)
    {
        var m = Regex.Match(html, @"name=""tid""\s+value=""(\d+)""");
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    private static int? Rating(string s) => int.TryParse(s, out var n) && n > 0 ? n : null;

    private static string Clean(string? s) => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, @"\s+", " ").Trim();

    /// <summary>
    /// Sayfa karışık kodlanmış: şablon ve sporcu listesi UTF-8, bileşenin sabit metinleri
    /// Windows-1254. Geçerli UTF-8 dizileri UTF-8, kalan baytlar 1254 olarak çözülür.
    /// </summary>
    public static string Decode(byte[] b)
    {
        var sb = new StringBuilder(b.Length);
        int i = 0;
        while (i < b.Length)
        {
            byte x = b[i];
            if (x < 0x80) { sb.Append((char)x); i++; continue; }
            int n = x >= 0xF5 ? 0 : x >= 0xF0 ? 4 : x >= 0xE0 ? 3 : x >= 0xC2 ? 2 : 0;
            if (n > 0 && i + n <= b.Length && Enumerable.Range(1, n - 1).All(k => (b[i + k] & 0xC0) == 0x80))
            {
                sb.Append(Encoding.UTF8.GetString(b, i, n));
                i += n;
                continue;
            }
            sb.Append(Cp1254(x));
            i++;
        }
        return sb.ToString();
    }

    // Windows-1254 = Windows-1252 + altı Türkçe harf.
    private static char Cp1254(byte x) => x switch
    {
        0xD0 => 'Ğ', 0xDD => 'İ', 0xDE => 'Ş', 0xF0 => 'ğ', 0xFD => 'ı', 0xFE => 'ş',
        0x80 => '€', 0x8A => 'Š', 0x8C => 'Œ', 0x9A => 'š', 0x9C => 'œ', 0x9F => 'Ÿ',
        0x91 => '‘', 0x92 => '’', 0x93 => '“', 0x94 => '”', 0x96 => '–', 0x97 => '—',
        _ => (char)x // 0xA0–0xFF Latin-1 ile aynı
    };
}
