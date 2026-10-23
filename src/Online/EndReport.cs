namespace NotasyonOtomasyonu.Online;

/// <summary>Bir kategorinin final sıralaması (chess-results "N. Turdan Sonra Sıralama").</summary>
public sealed record CategoryStandings(string Category, ListTable? Standings, int RoundsPlayed, int RoundsTotal, bool IsTeam);

/// <summary>Turnuva sonu raporu için hesaplanan değerler ve tablolar.</summary>
public sealed class EndReportData
{
    /// <summary>Tekil alanlar: SPORCU_SAYISI, IL_SAYISI, RAPOR_TARIHI.</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Tablolar: "katilim", "il", "derece", "siralama".</summary>
    public Dictionary<string, List<Dictionary<string, string>>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Turnuva sonu raporu: chess-results'taki final sıralamalarından katılım (kategori başına sporcu,
/// oynanan tur), toplam sporcu ve il sayısı, il dağılımı (TSF kayıt listesinden / öğrenilen iller),
/// her kategoride dereceye girenler (ilk üç) ve tam sıralama. Ağ gerektirmez (test edilebilir).
/// </summary>
public static class EndReport
{
    public const string UnknownProvince = "Belirtilmemiş";

    /// <param name="provinceOf">Sporcunun ili (chess-results adıyla); bilinmiyorsa null.</param>
    public static EndReportData Build(IReadOnlyList<CategoryStandings> categories, Func<string, string?> provinceOf, DateTime reportDate, int podium = 3)
    {
        var data = new EndReportData();
        var katilim = new List<Dictionary<string, string>>();
        var derece = new List<Dictionary<string, string>>();
        var siralama = new List<Dictionary<string, string>>();
        var provinces = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int players = 0, teams = 0;

        foreach (var c in categories)
        {
            var rows = c.Standings is { } st ? Rows(st) : new List<Row>();
            katilim.Add(new()
            {
                ["kategori"] = c.Category,
                ["sporcu"] = rows.Count > 0 ? rows.Count.ToString() + (c.IsTeam ? " takım" : "") : "",
                ["tur"] = c.RoundsPlayed > 0 ? (c.RoundsTotal > c.RoundsPlayed ? $"{c.RoundsPlayed}/{c.RoundsTotal}" : c.RoundsPlayed.ToString()) : "",
            });
            if (c.IsTeam) teams += rows.Count; else players += rows.Count;

            foreach (var r in rows)
            {
                var il = c.IsTeam ? "" : provinceOf(r.Name) ?? "";
                if (!c.IsTeam)
                {
                    var key = il.Length == 0 ? UnknownProvince : il;
                    provinces[key] = provinces.GetValueOrDefault(key) + 1;
                }
                var person = c.IsTeam ? r.Name : ReportBuilder.FormatPerson(r.Name);
                var row = new Dictionary<string, string>
                {
                    ["kategori"] = c.Category, ["sira"] = r.Rank, ["ad"] = person, ["il"] = il, ["rtg"] = r.Rating, ["puan"] = r.Points
                };
                siralama.Add(row);
                if (int.TryParse(r.Rank, out var rk) && rk <= podium)
                    derece.Add(new Dictionary<string, string>(row) { ["derece"] = $"{rk}." });
            }
        }

        data.Lists["katilim"] = katilim;
        data.Lists["derece"] = derece;
        data.Lists["siralama"] = siralama;
        // İl dağılımı: çoktan aza; bilinmeyenler en sonda.
        data.Lists["il"] = provinces
            .OrderBy(p => p.Key == UnknownProvince).ThenByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Create(new System.Globalization.CultureInfo("tr-TR"), true))
            .Select(p => new Dictionary<string, string> { ["adi"] = p.Key, ["sayi"] = p.Value.ToString() })
            .ToList();

        data.Values["SPORCU_SAYISI"] = players == 0 && teams == 0 ? ""
            : players > 0 ? players.ToString() + (teams > 0 ? $" sporcu, {teams} takım" : "") : $"{teams} takım";
        int knownProvinces = provinces.Keys.Count(k => k != UnknownProvince);
        data.Values["IL_SAYISI"] = knownProvinces > 0 ? knownProvinces.ToString() : "";
        data.Values["RAPOR_TARIHI"] = reportDate.ToString("dd.MM.yyyy");
        return data;
    }

    private sealed record Row(string Rank, string Name, string Rating, string Points);

    /// <summary>Sıralamadaki sporcular (il eşleştirmesi için: ad ve rating).</summary>
    public static IEnumerable<RosterPlayer> Players(ListTable t, string category)
        => Rows(t).Select(r => new RosterPlayer(r.Name, int.TryParse(r.Rating, out var n) && n > 0 ? n : null, category));

    /// <summary>Sıralama tablosundan sıra, ad, rating ve puan sütunları (başlık adlarına göre).</summary>
    private static List<Row> Rows(ListTable t)
    {
        int Col(params string[] names)
        {
            for (int i = 0; i < t.Headers.Count; i++)
                if (names.Any(n => string.Equals(t.Headers[i].Trim(), n, StringComparison.OrdinalIgnoreCase))) return i;
            return -1;
        }
        int rank = Col("Sıra", "Rk.", "Rk", "Pl.", "Place");
        int name = Col("İsim", "Name", "Takım", "Team", "Ad");
        int rtg = Col("Rtg", "ELO", "Elo", "UKD", "Rating");
        int pts = Col("Puan", "Pts.", "Pts", "MP", "TB1");
        if (name < 0) return new();
        string At(IReadOnlyList<string> r, int i) => i >= 0 && i < r.Count ? r[i].Trim() : "";
        return t.Rows.Where(r => At(r, name).Length > 0)
            .Select((r, i) => new Row(At(r, rank).Length > 0 ? At(r, rank) : (i + 1).ToString(), At(r, name), At(r, rtg) is "0" ? "" : At(r, rtg), At(r, pts)))
            .ToList();
    }
}
