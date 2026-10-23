using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Registration;

public enum ProvinceSource { None, Manual, Registration, Probable, OtherList, Learned }

/// <summary>
/// Notasyondaki "İl" kutusuna yazılacak il. Sıra: bu turnuvada elle verilen → bu turnuvanın kayıt
/// listesi (kontrol ekranında onaylanan ya da anlık eşleşme; en eksiksiz kaynak) → önceki turnuvaların
/// kayıt listeleri (yalnız birebir ad, en yeni liste) → önceki turnuvalardan öğrenilen il.
/// Hiçbiri yoksa kutu boş kalır.
/// </summary>
public static class ProvinceBook
{
    /// <summary>
    /// Turnuvanın kayıt listesi: önce bu turnuvaya bağlı olan; yoksa (liste başka bir turnuva açıkken
    /// alınmışsa) form başlığı bu turnuvanın adıyla açıkça eşleşen kayıtlı liste.
    /// </summary>
    public static EventRegistration? For(AppConfig cfg, int? eventTnr, string? eventName = null)
    {
        if (eventTnr is int n && cfg.Registrations.TryGetValue(n.ToString(), out var r)) return r;
        return string.IsNullOrWhiteSpace(eventName) ? null : ByTitle(cfg, eventName);
    }

    /// <summary>Başlığı turnuva adıyla en iyi eşleşen kayıtlı liste (benzerlik ≥ 0,6), yoksa null.</summary>
    public static EventRegistration? ByTitle(AppConfig cfg, string eventName)
        => cfg.Registrations.Values
            .Where(r => r.Entries.Count > 0)
            .Select(r => (r, s: r.Titles.Append(r.EventName).Max(t => RegistrationMatch.TitleScore(t, eventName))))
            .Where(x => x.s >= 0.6)
            .OrderByDescending(x => x.s).ThenByDescending(x => x.r.FetchedAt)
            .Select(x => x.r).FirstOrDefault();

    /// <summary>Eşleştirmedeki sporculara illerini ekler (kağıda basmadan hemen önce).</summary>
    public static Tournament Apply(Tournament t, AppConfig cfg, int? eventTnr, string? eventName = null)
    {
        var reg = For(cfg, eventTnr, eventName);
        if (reg is null && cfg.PlayerProvinces.Count == 0 && cfg.Registrations.Count == 0) return t;
        var others = OtherLists(cfg, reg);

        var roster = t.Pairings
            .SelectMany(p => p.Black is null ? new[] { (p.White, p.Category) } : new[] { (p.White, p.Category), (p.Black, p.Category) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Item1.Name) && x.Item1.Name != "BAY")
            .Select(x => new RosterPlayer(x.Item1.Name, x.Item1.Rating, x.Category))
            .DistinctBy(x => ProvinceMatcher.PersonKey(x.Name))
            .ToList();
        var live = reg is { Entries.Count: > 0 }
            ? ProvinceMatcher.MatchAll(roster, reg.Entries).ToDictionary(m => ProvinceMatcher.PersonKey(m.Player.Name))
            : new Dictionary<string, ProvinceMatch>();

        Player With(Player pl)
        {
            if (!string.IsNullOrWhiteSpace(pl.Province)) return pl;
            var (prov, _) = Lookup(pl.Name, cfg, reg, live, others);
            return prov is null ? pl : pl with { Province = prov };
        }
        return t with
        {
            Pairings = t.Pairings.Select(p => p with { White = With(p.White), Black = p.Black is null ? null : With(p.Black) }).ToList()
        };
    }

    /// <summary>Verilen sporcular için il bulucu (kayıt listesiyle tek seferde eşleştirilir).</summary>
    public static Func<string, string?> Resolver(AppConfig cfg, int? eventTnr, IEnumerable<RosterPlayer> roster, string? eventName = null)
    {
        var reg = For(cfg, eventTnr, eventName);
        var players = roster.DistinctBy(p => ProvinceMatcher.PersonKey(p.Name)).ToList();
        var live = reg is { Entries.Count: > 0 }
            ? ProvinceMatcher.MatchAll(players, reg.Entries).ToDictionary(m => ProvinceMatcher.PersonKey(m.Player.Name))
            : new Dictionary<string, ProvinceMatch>();
        var others = OtherLists(cfg, reg);
        return name => Lookup(name, cfg, reg, live, others).Province;
    }

    /// <summary>
    /// Önceki turnuvaların kayıt listelerinden ad → il (ad sözcükleri birebir aynı olanlar; en yeni liste
    /// kazanır). Bir listede aynı adla farklı illerden kayıt varsa o ad kullanılmaz.
    /// </summary>
    public static Dictionary<string, string> OtherLists(AppConfig cfg, EventRegistration? current)
    {
        var map = new Dictionary<string, string>();
        foreach (var reg in cfg.Registrations.Values.Where(r => !ReferenceEquals(r, current) && r.Entries.Count > 0).OrderBy(r => r.FetchedAt))
        {
            if (current is not null && reg.Tids.Count > 0 && reg.Tids.SequenceEqual(current.Tids)) continue; // aynı liste
            foreach (var g in reg.Entries.GroupBy(e => ProvinceMatcher.PersonKey(e.FullName)))
            {
                var provs = g.Select(e => Provinces.Print(e.Province)).Where(x => x.Length > 0).Distinct().ToList();
                if (provs.Count == 1) map[g.Key] = provs[0];
            }
        }
        return map;
    }

    public static (string? Province, ProvinceSource Source) Lookup(string name, AppConfig cfg, EventRegistration? reg,
        IReadOnlyDictionary<string, ProvinceMatch>? live = null, IReadOnlyDictionary<string, string>? others = null)
    {
        var key = ProvinceMatcher.PersonKey(name);
        if (reg is not null)
        {
            if (reg.Overrides.TryGetValue(key, out var o))
                return (o.Length == 0 ? null : o, ProvinceSource.Manual);
            if (reg.Resolved.TryGetValue(key, out var r) && r.Length > 0)
                return (r, ProvinceSource.Registration);
            if (live is not null && live.TryGetValue(key, out var m))
            {
                if (m.Province is { } lp)
                    return (lp, m.Kind == MatchKind.Exact ? ProvinceSource.Registration : ProvinceSource.Probable);
                if (m.Kind == MatchKind.Ambiguous)
                    return (null, ProvinceSource.None); // aynı adlı, farklı illi kayıtlar: tahmin etme
            }
        }
        if (others is not null && others.TryGetValue(key, out var ol)) return (ol, ProvinceSource.OtherList);
        return cfg.PlayerProvinces.TryGetValue(key, out var l) && l.Length > 0
            ? (l, ProvinceSource.Learned) : (null, ProvinceSource.None);
    }
}
