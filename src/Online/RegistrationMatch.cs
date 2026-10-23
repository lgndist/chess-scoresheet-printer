using System.Text.RegularExpressions;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// TSF kayıt formunun hangi chess-results turnuvasına ait olduğunu bulur. İki ölçüt:
/// <list type="bullet">
/// <item><b>Sporcu örtüşmesi</b> (asıl ölçüt): formdaki kayıtlıların ne kadarı chess-results listesinde.
/// Aynı il sitesinde aynı ay iki turnuva olduğunda ("Ekim Ayı UKD" / "29 Ekim") adlar yanıltır, sporcular yanıltmaz.</item>
/// <item><b>Ad benzerliği</b>: il adı, ay adı ve "satranç / turnuvası / kategori / yaş" gibi her turnuvada geçen
/// sözcükler atılarak karşılaştırılır (Dice). Sporcu listesi henüz yoksa yalnız buna bakılır.</item>
/// </list>
/// </summary>
public static class RegistrationMatch
{
    private static readonly HashSet<string> Stop = new()
    {
        "satra", "satranc", "turnu", "kateg", "yas", "yasi", "alti", "ustu", "ve", "acik", "genel", "sporc", "dogan",
        "yilla", "yili", "arasi", "once", "sonra", "il", "ilce", "bayan", "erkek", "kadin", "karma", "musab", "tsf", "ayi"
    };

    private static readonly HashSet<string> Months = new()
    {
        "ocak", "subat", "mart", "nisan", "mayis", "hazir", "temmu", "agust", "eylul", "ekim", "kasim", "arali"
    };

    private static readonly Lazy<HashSet<string>> ProvinceWords = new(() =>
        Provinces.List.Skip(1).SelectMany(p => Regex.Split(EventGrouping.Fold(p), "[^a-z0-9]+"))
            .Where(w => w.Length >= 2).Select(Trunc).ToHashSet());

    private static string Trunc(string w) => w.Length > 5 ? w[..5] : w;

    /// <summary>Ayırt edici sözcükler ("Isparta 29 Ekim Cumhuriyet Bayramı Satranç Turnuvası" → 29, cumhu, bayra).</summary>
    public static HashSet<string> Words(string? title)
    {
        var f = EventGrouping.Fold(title ?? "");
        f = Regex.Replace(f, @"\d+\s*yas(\s+ve\s+(alti|ustu))?", " ");     // "8 Yaş ve Altı" kategori adıdır
        f = Regex.Replace(f, @"\(.*?\)|\[.*?\]", " ");                       // "(2017-2020 doğumlu)"
        return Regex.Split(f, "[^a-z0-9]+")
            .Where(w => w.Length >= 2)
            .Select(Trunc)
            .Where(w => !Stop.Contains(w) && !Months.Contains(w) && !ProvinceWords.Value.Contains(w))
            .ToHashSet();
    }

    /// <summary>Ad benzerliği 0–1 (Dice). Ayırt edici sözcük yoksa 0.</summary>
    public static double TitleScore(string? formTitle, string? eventName)
    {
        var a = Words(formTitle);
        var b = Words(EventGrouping.BaseName(eventName ?? ""));
        if (a.Count == 0 || b.Count == 0) return 0;
        return 2.0 * a.Intersect(b).Count() / (a.Count + b.Count);
    }

    /// <summary>Formdaki kayıtlılardan chess-results listesinde bulunanlar (birebir ya da benzer ad).</summary>
    public static int Overlap(IReadOnlyList<RosterPlayer> roster, IReadOnlyList<PlayerRegistration> entries)
    {
        if (roster.Count == 0 || entries.Count == 0) return 0;
        // Formun kendi gözünden: her kayıtlı en fazla bir kez sayılır.
        return ProvinceMatcher.MatchAll(roster, entries).Count(m => m.Kind is MatchKind.Exact or MatchKind.Probable);
    }

    /// <summary>
    /// Form bu turnuvanın mı: sporcu listesi varsa kayıtlıların en az %40'ı (en az 2 kişi) listede olmalı;
    /// liste henüz yoksa (turnuva yüklenmemiş) ad benzerliği en az 0,5 olmalı.
    /// </summary>
    public static bool IsLikely(double titleScore, int matched, int entries, int rosterCount)
    {
        if (rosterCount >= 3 && entries > 0)
            return matched >= Math.Max(2, (int)Math.Ceiling(entries * 0.4)) || (matched >= 1 && titleScore >= 0.6);
        return titleScore >= 0.5;
    }
}
