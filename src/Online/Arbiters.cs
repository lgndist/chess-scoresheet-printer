using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Online;

/// <summary>Turnuva görevlisi (yaka kartı satırı).</summary>
public sealed record ArbiterEntry(string Name, string Role, string? FideTitle);

/// <summary>
/// chess-results turnuva bilgilerinden görevli listesi (direktör, başhakem, yardımcı, hakemler) ve
/// hakem derecelerine göre yaka kartı renkleri.
/// </summary>
public static class Arbiters
{
    public const string Aday = "Aday Hakem", Il = "İl Hakemi", Ulusal = "Ulusal Hakem", Fide = "FIDE Hakemi", Uluslararasi = "Uluslararası Hakem";

    /// <summary>Hakem dereceleri (düşükten yükseğe).</summary>
    public static readonly string[] Grades = { Aday, Il, Ulusal, Fide, Uluslararasi };

    /// <summary>Görev seçenekleri.</summary>
    public static readonly string[] Roles =
    {
        "Başhakem", "Başhakem Yardımcısı", "Hakem", "Turnuva Direktörü", "Eşlendirme Hakemi", "Saha Hakemi", "Organizasyon Görevlisi"
    };

    /// <summary>
    /// Karta basılacak görev. Sade yazımda yalnız iki görev vardır: başhakem "Başhakem", diğer
    /// herkes (yardımcı, direktör, saha/eşlendirme hakemi…) "Hakem".
    /// </summary>
    public static string PrintedRole(string? role, bool simple)
    {
        var r = string.IsNullOrWhiteSpace(role) ? "Hakem" : role.Trim();
        if (!simple) return r;
        return EventGrouping.Fold(r) == EventGrouping.Fold("Başhakem") ? "Başhakem" : "Hakem";
    }

    /// <summary>Dereceye göre ayırt edici şerit rengi.</summary>
    public static string ColorFor(string? grade) => grade switch
    {
        Aday => "#78909C",          // gri-mavi
        Il => "#2E7D32",            // yeşil
        Ulusal => "#1565C0",        // mavi
        Fide => "#C62828",          // kırmızı
        Uluslararasi => "#B8860B",  // altın
        _ => "#455A64"
    };

    /// <summary>FIDE unvanından derece: IA → Uluslararası, FA → FIDE, NA → Ulusal; unvan yoksa null.</summary>
    public static string? GradeFromTitle(string? title) => title?.ToUpperInvariant() switch
    {
        "IA" => Uluslararasi,
        "FA" => Fide,
        "NA" => Ulusal,
        _ => null
    };

    public static List<ArbiterEntry> FromInfo(IReadOnlyDictionary<string, string> info)
    {
        string Exact(params string[] labels)
        {
            foreach (var l in labels)
                foreach (var (k, v) in info)
                    if (EventGrouping.Fold(k).Trim() == EventGrouping.Fold(l)) return v;
            return "";
        }
        var list = new List<ArbiterEntry>();
        void Add(string raw, string role)
        {
            foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var (title, name) = SplitPerson(part);
                if (name.Length > 1 && !list.Any(a => EventGrouping.Fold(a.Name) == EventGrouping.Fold(name)))
                    list.Add(new ArbiterEntry(name, role, title));
            }
        }
        Add(Exact("Başhakem", "Chief Arbiter"), "Başhakem");
        Add(Exact("Başhakem Yardımcısı", "Deputy Chief Arbiter"), "Başhakem Yardımcısı");
        Add(Exact("Hakem", "Arbiter"), "Hakem");
        Add(Exact("Turnuva direktoru", "Turnuva direktörü", "Tournament director"), "Turnuva Direktörü");
        return list;
    }

    /// <summary>"NA Kutdemir, Mehmet 34570535" → ("NA", "Mehmet KUTDEMİR"); "Haluk Terlemez" → (null, "Haluk TERLEMEZ").</summary>
    public static (string? Title, string Name) SplitPerson(string raw)
    {
        var s = Regex.Replace(raw ?? "", @"\.\.\..*$", "").Trim();
        s = Regex.Replace(s, @"\s*\d{4,}\s*$", "").Trim();
        string? title = null;
        var tm = Regex.Match(s, @"^(IA|FA|NA|IO|FST|FT)\s+");
        if (tm.Success) { title = tm.Groups[1].Value; s = s[tm.Length..].Trim(); }
        var parts = s.Split(',', 2);
        if (parts.Length == 2) return (title, $"{parts[1].Trim()} {ReportBuilder.TurkishUpper(parts[0].Trim())}".Trim());
        // "Ad Soyad": son sözcük soyadı
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 2) words[^1] = ReportBuilder.TurkishUpper(words[^1]);
        return (title, string.Join(" ", words));
    }
}
