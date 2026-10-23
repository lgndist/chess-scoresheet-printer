using System.Text.RegularExpressions;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>chess-results'taki bir sporcu (ad "SOYAD, AD" biçiminde), puanı ve kategorisi.</summary>
public sealed record RosterPlayer(string Name, int? Rating = null, string? Category = null);

public enum MatchKind
{
    /// <summary>Ad ve soyad birebir (Türkçe harf/sıra farkı dışında).</summary>
    Exact,
    /// <summary>Soyad aynı; ikinci ad eksik ya da adda tek harf farkı. İl yazılır ama kontrol önerilir.</summary>
    Probable,
    /// <summary>Birden çok kayıt eşit derecede uyuyor ve illeri farklı: il yazılmaz.</summary>
    Ambiguous,
    NotFound
}

public sealed record ProvinceMatch(RosterPlayer Player, MatchKind Kind, PlayerRegistration? Entry, double Score, string? Note = null)
{
    /// <summary>Kağıda yazılacak il (belirsiz/bulunamadıysa null).</summary>
    public string? Province => Kind is MatchKind.Exact or MatchKind.Probable && Entry is not null
        ? Provinces.Print(Entry.Province) : null;
}

/// <summary>
/// chess-results sporcularını TSF kayıt listesiyle eşler. Kurallar:
/// <list type="bullet">
/// <item>Türkçe harfler katlanır (İ/I/ı, Ç→C, Ş→S…), noktalama ve boşluk farkı yok sayılır.</item>
/// <item>chess-results "SOYAD, AD" ile kayıttaki AD + SOYAD karşılaştırılır; soyad mutlaka tutmalı
/// (kardeşler — aynı soyad, farklı ad — birbirine eşlenmez).</item>
/// <item>İkinci ad eksikliği ("AVCI, MEHMET" ↔ "MEHMET ARİF AVCI") ve adda tek harf yazım farkı
/// "benzer" sayılır.</item>
/// <item>Puan (UKD/ELO) ve kategori yaşı yalnız eşitliği bozar; puan değişebildiği için tutmaması
/// eşleşmeyi düşürmez, farklı yaş kategorisi ise düşürür.</item>
/// <item>Her kayıt satırı tek sporcuya verilir (en yüksek puanlı eşleşme önce); eşit uyan birden çok
/// kaydın illeri aynıysa il yine yazılır, farklıysa belirsiz bırakılır.</item>
/// </list>
/// </summary>
public static class ProvinceMatcher
{
    public const double ExactScore = 1.0, ReorderScore = 0.95, MiddleNameScore = 0.85, TypoScore = 0.8, FullTypoScore = 0.75;

    public static List<ProvinceMatch> MatchAll(IReadOnlyList<RosterPlayer> players, IReadOnlyList<PlayerRegistration> entries)
    {
        // Tüm aday çiftleri puanla, sonra yüksekten düşüğe tekil ata.
        var cands = new List<(int P, int E, double S)>();
        for (int p = 0; p < players.Count; p++)
            for (int e = 0; e < entries.Count; e++)
            {
                double s = Score(players[p], entries[e]);
                if (s > 0) cands.Add((p, e, s));
            }

        var result = new ProvinceMatch?[players.Count];
        var usedEntry = new HashSet<int>();
        foreach (var grp in cands.GroupBy(c => c.P))
        {
            // Bu sporcunun en iyi iki adayı: eşit derecede iyi ve illeri farklıysa belirsiz.
            var top = grp.OrderByDescending(c => c.S).ToList();
            if (top.Count > 1 && top[0].S - top[1].S < 0.02 &&
                !SameProvince(entries[top[0].E], entries[top[1].E]))
                result[grp.Key] = new ProvinceMatch(players[grp.Key], MatchKind.Ambiguous, null, top[0].S,
                    "Birden çok kayıt uyuyor: " + string.Join(", ", top.TakeWhile(c => top[0].S - c.S < 0.02)
                        .Select(c => $"{entries[c.E].FullName} ({entries[c.E].Province})")));
        }

        foreach (var (p, e, s) in cands.OrderByDescending(c => c.S))
        {
            if (result[p] is not null || usedEntry.Contains(e)) continue;
            usedEntry.Add(e);
            var kind = s >= ReorderScore ? MatchKind.Exact : MatchKind.Probable;
            result[p] = new ProvinceMatch(players[p], kind, entries[e], s,
                kind == MatchKind.Probable ? $"Kayıttaki ad: {entries[e].FullName}" : null);
        }

        for (int p = 0; p < players.Count; p++)
            result[p] ??= new ProvinceMatch(players[p], MatchKind.NotFound, null, 0);
        return result.Select(r => r!).ToList();
    }

    private static bool SameProvince(PlayerRegistration a, PlayerRegistration b)
        => Provinces.Print(a.Province) == Provinces.Print(b.Province);

    /// <summary>0 = eşleşmez; 0,75–1,05 arası eşleşme gücü.</summary>
    public static double Score(RosterPlayer player, PlayerRegistration reg)
    {
        var (sur, giv) = Split(player.Name);
        var rs = Key(reg.Surname);
        var rg = Key(reg.Given);
        if (sur.Length == 0 || rs.Length == 0) return 0;

        double s;
        if (sur == rs && giv == rg) s = ExactScore;
        else if (Tokens(sur + " " + giv).SetEquals(Tokens(rs + " " + rg))) s = ReorderScore;
        else if (sur == rs && giv.Length > 0 && rg.Length > 0 && (Tokens(giv).IsSubsetOf(Tokens(rg)) || Tokens(rg).IsSubsetOf(Tokens(giv))))
            s = MiddleNameScore;
        else if (sur == rs && giv.Length >= 3 && Levenshtein(giv, rg) <= 1) s = TypoScore;
        else if (Levenshtein(sur + giv, rs + rg) <= 1 && (rs + rg).Length >= 9 && Levenshtein(sur, rs) <= 1) s = FullTypoScore;
        else return 0;

        // Eşitlik bozucular.
        if (player.Rating is int r && r > 0 && (r == reg.Ukd || r == reg.Elo)) s += 0.04;
        int? pa = Age(player.Category), ra = Age(reg.Category);
        if (pa is not null && ra is not null) s += pa == ra ? 0.02 : -0.15;
        return s >= FullTypoScore - 0.001 ? s : 0;
    }

    /// <summary>"SOYAD, AD" → (soyad, ad) katlanmış; virgül yoksa son sözcük soyad sayılır.</summary>
    public static (string Surname, string Given) Split(string name)
    {
        int comma = name.IndexOf(',');
        if (comma >= 0) return (Key(name[..comma]), Key(name[(comma + 1)..]));
        var t = Key(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return t.Length == 0 ? ("", "") : (t[^1], string.Join(' ', t[..^1]));
    }

    /// <summary>Karşılaştırma anahtarı: Türkçe harfler katlanmış, küçük harf, yalnız harf ve tek boşluk.</summary>
    public static string Key(string s)
        => Regex.Replace(Regex.Replace(EventGrouping.Fold(s ?? ""), @"[^a-z ]", " "), @"\s+", " ").Trim();

    /// <summary>Sporcu adının sıra/virgülden bağımsız anahtarı (öğrenilen il hafızası için).</summary>
    public static string PersonKey(string name) => string.Join(' ', Tokens(Key(name.Replace(',', ' '))).Order());

    private static HashSet<string> Tokens(string s) => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    /// <summary>Kategori adındaki yaş ("12 Yaş ve Altı", "U12", "12 YAŞ") ya da null.</summary>
    public static int? Age(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        var f = EventGrouping.Fold(category);
        var m = Regex.Match(f, @"(\d{1,2})\s*yas|\bu\s?(\d{1,2})\b");
        if (!m.Success) return null;
        return int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
    }

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
