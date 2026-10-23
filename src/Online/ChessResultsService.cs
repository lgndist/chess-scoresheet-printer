using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// chess-results üzerinden turnuva arama, kategori/tur keşfi ve eşleştirme çekme.
/// UI bu cepheyi kullanır; tüm çağrılar async'tir.
/// </summary>
public sealed class ChessResultsService : IDisposable
{
    private readonly ChessResultsClient _client;
    private readonly bool _ownsClient;

    public ChessResultsService(ChessResultsClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new ChessResultsClient();
    }

    /// <summary>
    /// Türkiye turnuvalarını getirir. İl ve/veya metin verilirse ad içinde (Türkçe-duyarsız) süzer;
    /// varsayılan olarak aynı etkinliğin kategorilerini tek girişe indirir.
    /// Federasyon sayfası yalnızca son ~50 turnuvayı gösterdiğinden, il için orada sonuç yoksa
    /// chess-results'ın turnuva arama formuna (son güncellenene göre) düşülür.
    /// </summary>
    public async Task<IReadOnlyList<TournamentRef>> SearchTurkeyAsync(
        string? text, string? province = null, bool dedupe = true, CancellationToken ct = default)
    {
        var html = await _client.FetchAsync(ChessResultsClient.FederationUrl("TUR"), ct).ConfigureAwait(false);
        IReadOnlyList<TournamentRef> all = ChessResultsParser.ParseFederationList(html);
        var list = Filter(all, text, province);

        if (list.Count == 0 && !string.IsNullOrWhiteSpace(province))
        {
            try
            {
                var found = await SearchByNameAsync(province!, ct).ConfigureAwait(false);
                list = Filter(found, text, province);
            }
            catch (HttpRequestException) { /* arama formu çalışmazsa boş liste */ }
        }
        return dedupe ? EventGrouping.Dedupe(list) : list.ToList();
    }

    private static List<TournamentRef> Filter(IReadOnlyList<TournamentRef> list, string? text, string? province)
    {
        IEnumerable<TournamentRef> q = list;
        if (!string.IsNullOrWhiteSpace(province)) { var pf = Fold(province); q = q.Where(t => Fold(t.Name).Contains(pf)); }
        if (!string.IsNullOrWhiteSpace(text)) { var f = Fold(text); q = q.Where(t => Fold(t.Name).Contains(f)); }
        return q.ToList();
    }

    /// <summary>chess-results turnuva arama formu: Türkiye, ad içinde metin, son güncellenen önce.</summary>
    public async Task<IReadOnlyList<TournamentRef>> SearchByNameAsync(string name, CancellationToken ct = default)
    {
        var html = await _client.PostSearchAsync(new Dictionary<string, string>
        {
            ["ctl00$P1$txt_bez"] = name,
            ["ctl00$P1$combo_land"] = "TUR",
            ["ctl00$P1$combo_art"] = "5",          // tüm turnuva tipleri
            ["ctl00$P1$combo_sort"] = "1",         // son güncelleme
            ["ctl00$P1$combo_anzahl_zeilen"] = "0",// 100 satır
            ["ctl00$P1$combo_bedenkzeit"] = "0",
            ["ctl00$P1$cb_suchen"] = "Arama",
        }, ct).ConfigureAwait(false);
        return ChessResultsParser.ParseFederationList(html)
            .Select(t => t with { Name = TrimTruncatedName(t.Name) }).ToList();
    }

    /// <summary>
    /// Arama sayfası adları ~50 karakterde keser ("… TURNUVASIAÇ"); kategori eki yarım kalınca
    /// aynı etkinlik tek girişe inmez. Kesilmiş adı son "turnuvası/şampiyonası/…" kelimesinde bitir.
    /// </summary>
    public static string TrimTruncatedName(string name)
    {
        var folded = EventGrouping.Fold(name);
        int cut = -1;
        foreach (var word in new[] { "turnuvasi", "sampiyonasi", "festivali", "ligi", "kupasi", "tournament" })
        {
            int i = folded.LastIndexOf(word, StringComparison.Ordinal);
            if (i > 0) cut = Math.Max(cut, i + word.Length);
        }
        // Anahtar kelimeden sonra kalan kısa parça kategori ekidir ("AÇIK", "12", bitişik "AÇ").
        if (cut > 0 && name.Length - cut <= 20) return name[..cut].Trim();
        // Anahtar kelime yoksa ve ad kesilmişse son yarım kelimeyi at.
        if (name.Length >= 48) { int sp = name.LastIndexOf(' '); if (sp > 20) return name[..sp].Trim(); }
        return name;
    }

    /// <summary>
    /// Bir kategorinin üst bilgisi + kategoriler + sistem + tur bilgisi. Döner (Berger) turnuvada
    /// menüde tur linki olmadığından turlar eşleştirme sayfasından çözülür: toplam tur = bölüm
    /// sayısı, güncel tur = sonucu girilmemiş oyunu olan ilk tur.
    /// </summary>
    public async Task<EventInfo> GetEventAsync(int tnr, CancellationToken ct = default)
    {
        var html = await _client.FetchAsync(ChessResultsClient.EventUrl(tnr), ct).ConfigureAwait(false);
        var info = ChessResultsParser.ParseEventInfo(html, tnr);

        if (info.System == TournamentSystem.RoundRobin && info.CurrentRound == 0)
        {
            try
            {
                var pairHtml = await _client.FetchAsync(ChessResultsClient.PairingsUrl(tnr, 1), ct).ConfigureAwait(false);
                var sections = ChessResultsParser.ParseRoundSections(pairHtml)
                    .Where(s => s.Round is not null).OrderBy(s => s.Round).ToList();
                if (sections.Count > 0)
                {
                    int current = BergerTable.CurrentRound(
                        sections.Select(s => (IReadOnlyList<string?>)s.Pairings.Where(p => !p.IsBye).Select(p => p.Result).ToList()).ToList());
                    info = info with
                    {
                        MaxRound = Math.Max(info.MaxRound == ChessResultsParser.UnknownMaxRound ? 0 : info.MaxRound, sections.Max(s => s.Round!.Value)),
                        CurrentRound = sections[current - 1].Round!.Value
                    };
                }
            }
            catch (InvalidOperationException) { /* eşleştirme henüz yok */ }
        }
        return info;
    }

    /// <summary>
    /// Seçili kategori (tnr) + turun eşleştirmesini çeker. Takım turnuvasında maç listesi değil
    /// maç bazlı MASA eşleştirmesi (art=3) okunur; sistem bilinmiyorsa sayfadan anlaşılır.
    /// </summary>
    public async Task<Tournament> GetPairingsAsync(int tnr, int round,
        TournamentSystem system = TournamentSystem.Unknown, CancellationToken ct = default)
    {
        if (system.IsTeam())
            return ChessResultsParser.ParsePairings(
                await _client.FetchAsync(ChessResultsClient.TeamBoardsUrl(tnr, round), ct).ConfigureAwait(false),
                tnr, round, system);

        var html = await _client.FetchAsync(ChessResultsClient.PairingsUrl(tnr, round), ct).ConfigureAwait(false);
        if (system == TournamentSystem.Unknown && ChessResultsParser.IsTeamMatchList(html))
            return ChessResultsParser.ParsePairings(
                await _client.FetchAsync(ChessResultsClient.TeamBoardsUrl(tnr, round), ct).ConfigureAwait(false),
                tnr, round, TournamentSystem.TeamSwiss);
        return ChessResultsParser.ParsePairings(html, tnr, round, system);
    }

    /// <summary>Rapor/yönerge için bilgi tablosunun tamamı (turdet=ALL: tam hakem listesi).</summary>
    public async Task<Dictionary<string, string>> GetInfoTableAsync(int tnr, CancellationToken ct = default)
        => ChessResultsParser.ParseInfoTable(
            await _client.FetchAsync(ChessResultsClient.EventUrl(tnr).Replace("turdet=YES", "turdet=ALL"), ct).ConfigureAwait(false));

    /// <summary>Kategorinin <paramref name="round"/>. turdan sonraki sıralaması (yoksa null).</summary>
    public async Task<ListTable?> GetStandingsAsync(int tnr, int round, CancellationToken ct = default)
        => ChessResultsParser.ParseStandings(
            await _client.FetchAsync(ChessResultsClient.StandingsUrl(tnr, round), ct).ConfigureAwait(false));

    /// <summary>Kategorinin sporcuları (başlangıç sıralaması).</summary>
    public async Task<List<Player>> GetStartingListAsync(int tnr, CancellationToken ct = default)
        => ChessResultsParser.ParseStartingList(
            await _client.FetchAsync(ChessResultsClient.StartingListUrl(tnr), ct).ConfigureAwait(false));

    /// <summary>Tur tarih/saat programı (art=14).</summary>
    public async Task<List<(int Round, DateTime? Date, string Time)>> GetScheduleAsync(int tnr, CancellationToken ct = default)
        => ChessResultsParser.ParseSchedule(
            await _client.FetchAsync(ChessResultsClient.ScheduleUrl(tnr), ct).ConfigureAwait(false));

    /// <summary>Türkçe karakterleri ASCII'ye indirip küçük harfe çevirir (arama için).</summary>
    private static string Fold(string s) => EventGrouping.Fold(s.Trim());

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
