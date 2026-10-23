using System.Text.RegularExpressions;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>Kayıt listesindeki bir kategori ve kayıtlı sporcu sayısı.</summary>
public sealed record RegistrationCategory(string Name, int Count)
{
    /// <summary>Masa sayısı: her masada iki sporcu; tek sayıda bir sporcu BAY alır (BAY'ın masası yok).</summary>
    public int Boards => Count / 2;
}

/// <summary>
/// TSF kayıt listesinin kategorileri ve sayıları. Masa kartları gibi turnuva başında basılan kâğıtların
/// adedi bu sayılara göre hesaplanır (chess-results'ta henüz eşleştirme yokken de bilinir).
/// Kategori adları chess-results'takilerle yaşa göre ("12 Yaş ve Altı" ↔ "12 Yaş") ya da açık/genel
/// kategori olarak eşlenir.
/// </summary>
public static class RegistrationCategories
{
    /// <summary>Sporcunun kategorisi: KATEGORİ sütunu, boşsa kaydın formunun başlığındaki kategori.</summary>
    public static string CategoryOf(PlayerRegistration e, EventRegistration reg)
    {
        if (!string.IsNullOrWhiteSpace(e.Category)) return Clean(e.Category);
        int i = reg.Tids.IndexOf(e.Tid);
        var title = i >= 0 && i < reg.Titles.Count ? reg.Titles[i] : "";
        // "Ankara 29 Ekim … Turnuvası - 8 Yaş ve Altı Kategorisi" → "8 Yaş ve Altı Kategorisi"
        int dash = title.LastIndexOf(" - ", StringComparison.Ordinal);
        return Clean(dash >= 0 ? title[(dash + 3)..] : title);
    }

    /// <summary>"10 Yaş ve Altı Kategorisi (2017 ve 2020 yılları … doğan sporcular)" → "10 Yaş ve Altı Kategorisi".</summary>
    private static string Clean(string s) => Regex.Replace(Regex.Replace(s, @"\s*[\(\[].*?[\)\]]", ""), @"\s+", " ").Trim();

    /// <summary>Kategoriler ve kayıt sayıları (listedeki sırayla).</summary>
    public static List<RegistrationCategory> Counts(EventRegistration reg)
        => reg.Entries.GroupBy(e => CategoryOf(e, reg), StringComparer.OrdinalIgnoreCase)
            .Select(g => new RegistrationCategory(g.Key, g.Count()))
            .ToList();

    /// <summary>Kayıt kategorisi chess-results kategorisiyle aynı mı (yaş ya da açık/genel kategori ya da aynı ad).</summary>
    public static bool Same(string registrationCategory, string chessResultsCategory)
    {
        // "15 Yaş Kız" ile "15 Yaş Genel" ayrı kategorilerdir: cinsiyet de tutmalı.
        if (IsGirls(registrationCategory) != IsGirls(chessResultsCategory)) return false;
        int? a = ProvinceMatcher.Age(registrationCategory), b = ProvinceMatcher.Age(chessResultsCategory);
        if (a is not null || b is not null) return a == b;
        string ka = Key(registrationCategory), kb = Key(chessResultsCategory);
        if (ka.Length == 0 || kb.Length == 0) return IsOpen(registrationCategory) && IsOpen(chessResultsCategory);
        return ka == kb || (IsOpen(registrationCategory) && IsOpen(chessResultsCategory));
    }

    /// <summary>chess-results kategorisinin kayıt sayısı (eşleşen kayıt kategorilerinin toplamı); yoksa null.</summary>
    public static RegistrationCategory? For(EventRegistration? reg, string chessResultsCategory)
    {
        if (reg is not { Entries.Count: > 0 }) return null;
        var hits = Counts(reg).Where(c => Same(c.Name, chessResultsCategory)).ToList();
        return hits.Count == 0 ? null : new RegistrationCategory(hits[0].Name, hits.Sum(h => h.Count));
    }

    /// <summary>Kız/kadın/bayan kategorisi mi ("15 Yaş Kız", "Kızlar", "Kadınlar", "Bayan").</summary>
    public static bool IsGirls(string s)
        => Regex.IsMatch(EventGrouping.Fold(s), @"\b(kiz|kizlar|kadin|kadinlar|bayan|bayanlar|girls?|women)\b");

    private static bool IsOpen(string s)
    {
        var f = EventGrouping.Fold(s);
        return Regex.IsMatch(f, @"\b(acik|genel|open|serbest|buyukler)\b");
    }

    /// <summary>Kategori adının ayırt edici kısmı ("A Kategorisi" → "a"; "Açık Kategori" → "").</summary>
    private static string Key(string s)
    {
        var f = EventGrouping.Fold(Clean(s));
        f = Regex.Replace(f, @"\b(kategorisi|kategori|grubu|grup|acik|genel|open|satranc|turnuvasi)\b", " ");
        return Regex.Replace(f, @"[^a-z0-9]+", " ").Trim();
    }
}
