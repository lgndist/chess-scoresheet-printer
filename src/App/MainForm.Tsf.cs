using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// "Kayıttaki turnuva": ilin TSF sitesinde kayda açık olup chess-results'ta henüz bulunmayan turnuva.
/// Turnuva listesinde "📋 Kayıtta: …" olarak görünür ve eksi numarayla (−tid) tutulur. Seçilince
/// kategoriler ve sporcu sayıları TSF kayıt formundan gelir; masa kartları bu sayılarla basılır.
/// Eşleştirme isteyen işlemler turnuva chess-results'ta yayımlanınca açılır (o zaman kayıt listesi
/// adından tanınıp chess-results turnuvasına kendiliğinden bağlanır).
/// </summary>
public partial class MainForm
{
    private const string TsfLabel = "📋 Kayıtta: ";
    private const string TsfClosedLabel = "📋 Kayıt bitti: ";
    private readonly Dictionary<int, string> _tsfFormSites = new();   // form numarası → il sitesi

    /// <summary>
    /// Listedeki kayıt formunun durumu: kayıt sürüyor mu, bitiş tarihi, kayıtlı sporcu sayısı (bilinmiyorsa
    /// null); <paramref name="StoredAt"/>: site okunamadı, daha önce alınmış liste gösteriliyor.
    /// </summary>
    private sealed record TsfFormInfo(bool Closed, DateTime? ClosesAt, int? Count, DateTime? StoredAt = null);
    private static readonly Dictionary<int, TsfFormInfo> TsfInfo = new();
    // Form sayfaları kısa süre saklanır: her liste yenilemede/aramada hepsi yeniden indirilmesin.
    private readonly Dictionary<int, (DateTime At, RegistrationPage? Page)> _tsfPageCache = new();
    // İl sitesi → yeni kapanmış formlar (her liste yenilemede ~12 sayfa indirmemek için saatte bir yoklanır)
    private readonly Dictionary<string, (DateTime At, List<RegistrationPage> Pages)> _tsfClosedCache = new();

    /// <summary>Açık turnuva kayıttaki (henüz chess-results'ta olmayan) turnuva mı.</summary>
    private bool IsTsfEvent => _eventTnr is < 0;

    private static bool IsTsfClosed(int tid) => TsfInfo.GetValueOrDefault(tid) is { Closed: true };

    /// <summary>
    /// "📋 Kayıtta: Isparta Küçükler … — 32 sporcu  (TSF kayıt #358)",
    /// "📋 Kayıt bitti (23.10): Isparta Yıldızlar … — 21 sporcu  (TSF kayıt #359)".
    /// </summary>
    private static string ComboLabel(TournamentRef t)
    {
        if (t.Tnr >= 0) return $"{t.Name}  (#{t.Tnr})";
        var info = TsfInfo.GetValueOrDefault(-t.Tnr);
        var head = info switch
        {
            { StoredAt: { } at } => $"📋 Saklı kayıt listesi ({at:dd.MM}): ",
            { Closed: true, ClosesAt: { } end } => $"📋 Kayıt bitti ({end:dd.MM}): ",
            { Closed: true } => TsfClosedLabel,
            _ => TsfLabel
        };
        var count = info?.Count switch { 0 => " — henüz kayıt yok", int n => $" — {n} sporcu", _ => "" };
        return $"{head}{t.Name}{count}  (TSF kayıt #{-t.Tnr})";
    }

    /// <summary>
    /// İlin TSF sitesinde kayda açık ve kayıtları yeni bitmiş turnuvalar (chess-results'ta adıyla zaten
    /// bulunanlar hariç), kaydı sürenler üstte. Her formun sayfası okunur: sitedeki listede kaydı bitmiş
    /// formlar da kalabildiğinden durum ve sporcu sayısı buradan gelir. Kayıt bitip turnuva henüz
    /// chess-results'a yüklenmemişken de liste ve sayılar TSF'den gelir. Site erişilemezse daha önce
    /// alınmış kayıttaki turnuvalar (son 30 gün) yine listelenir.
    /// </summary>
    private async Task<List<TournamentRef>> TsfOpenFormsAsync(string? province, IReadOnlyList<TournamentRef> chessResults, string? text = null)
    {
        var list = new List<TournamentRef>();
        var site = TsfSites.SiteFor(province, _config.TsfSites);
        if (site is null) return list;
        try
        {
            using var sites = new TsfSites();
            var open = await sites.ListOpenAsync(site);
            var pages = await Task.WhenAll(open.Select(o => FormPageAsync(sites, site, o.Tid)));
            var forms = open.Select((o, i) => (o.Tid, o.Title, Page: pages[i])).ToList();
            foreach (var p in await RecentlyClosedAsync(sites, site, open.Select(o => o.Tid)))
                if (forms.All(f => f.Tid != p.Tid)) forms.Add((p.Tid, p.Title, (RegistrationPage?)p));
            var ordered = forms.OrderBy(f => f.Page is { Closed: true })
                               .ThenByDescending(f => f.Page?.ClosesAt ?? DateTime.MaxValue)
                               .ThenByDescending(f => f.Tid);
            foreach (var (tid, title, page) in ordered)
            {
                if (chessResults.Any(t => RegistrationMatch.TitleScore(title, t.Name) >= 0.6)) continue;
                if (!string.IsNullOrWhiteSpace(text) && !EventGrouping.Fold(title).Contains(EventGrouping.Fold(text))) continue;
                _tsfFormSites[tid] = site;
                TsfInfo[tid] = new TsfFormInfo(page?.Closed ?? false, page?.ClosesAt, page?.Entries.Count);
                list.Add(new TournamentRef(-tid, title));
            }
            int nOpen = list.Count(t => !IsTsfClosed(-t.Tnr)), nClosed = list.Count - nOpen;
            if (list.Count > 0 && string.IsNullOrWhiteSpace(text))
                LogOnce($"📋 TSF {province}: {(nOpen > 0 ? $"{nOpen} turnuvada kayıt sürüyor" : "kaydı süren turnuva yok")}" +
                        $"{(nClosed > 0 ? $", {nClosed} turnuvanın kaydı bitti" : "")} (chess-results'ta henüz yok; turnuva listesinin en üstünde).");
        }
        catch (Exception ex) { LogOnce("Uyarı: TSF kayıt formları alınamadı — " + TsfNet(ex)); }

        // Daha önce alınmış kayıttaki turnuvalar (son 30 gün) site kapalıyken ya da form sitede artık
        // görünmezken de listede kalır: saklı listeyle masa kartı basılabilsin.
        foreach (var (key, reg) in _config.Registrations)
        {
            if (!int.TryParse(key, out var tnr) || tnr >= 0 || reg.Site != site || list.Any(t => t.Tnr == tnr)) continue;
            if (DateTime.Now - reg.FetchedAt > TimeSpan.FromDays(30) || reg.Entries.Count == 0) continue;
            var title = reg.EventName.Length > 0 ? reg.EventName : reg.Titles.FirstOrDefault() ?? $"Kayıt formu {-tnr}";
            if (chessResults.Any(t => RegistrationMatch.TitleScore(title, t.Name) >= 0.6)) continue;
            if (!string.IsNullOrWhiteSpace(text) && !EventGrouping.Fold(title).Contains(EventGrouping.Fold(text))) continue;
            _tsfFormSites[-tnr] = site;
            TsfInfo[-tnr] = new TsfFormInfo(false, null, reg.Entries.Count, reg.FetchedAt);
            list.Add(new TournamentRef(tnr, title));
        }
        return list;
    }

    /// <summary>Kayıt formunun sayfası (5 dakika saklanır); okunamazsa null.</summary>
    private async Task<RegistrationPage?> FormPageAsync(TsfSites sites, string site, int tid)
    {
        if (_tsfPageCache.TryGetValue(tid, out var c) && DateTime.Now - c.At < TimeSpan.FromMinutes(5)) return c.Page;
        RegistrationPage? page;
        try { page = await sites.GetPageAsync(site, tid); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return null; }
        _tsfPageCache[tid] = (DateTime.Now, page);
        return page;
    }

    /// <summary>
    /// Yeni kapanmış formlar: bilinen en büyük form numarasının (açık formlar ve daha önce alınmış
    /// listeler) çevresi yoklanır; sonuç bir saat saklanır.
    /// </summary>
    private async Task<List<RegistrationPage>> RecentlyClosedAsync(TsfSites sites, string site, IEnumerable<int> openTids)
    {
        if (_tsfClosedCache.TryGetValue(site, out var c) && DateTime.Now - c.At < TimeSpan.FromHours(1)) return c.Pages;
        var known = openTids.Concat(_config.Registrations.Values.Where(r => r.Site == site).SelectMany(r => r.Tids)).ToList();
        var pages = known.Count == 0 ? new List<RegistrationPage>()
                  : await sites.ListRecentlyClosedAsync(site, known.Max(), openTids);
        _tsfClosedCache[site] = (DateTime.Now, pages);
        return pages;
    }

    /// <summary>Kayıttaki turnuvayı yükler: kategoriler ve sayılar TSF kayıt formundan (site kapalıysa eldeki listeden).</summary>
    private async Task LoadRegistrationEventAsync(int tnr, bool silent)
    {
        int tid = -tnr;
        var key = tnr.ToString();
        SetBusy(true);
        SetStatus("TSF kayıt formu okunuyor…", ok: true);
        try
        {
            var stored = _config.Registrations.GetValueOrDefault(key);
            var site = _tsfFormSites.GetValueOrDefault(tid) ?? (stored is { Site.Length: > 0 } ? stored.Site : null)
                       ?? TsfSites.SiteFor(CurrentProvince(), _config.TsfSites);
            RegistrationPage? page = null;
            string? problem = null;
            if (site is not null)
            {
                try { using var sites = new TsfSites(); page = await sites.GetPageAsync(site, tid); }
                catch (Exception ex) { problem = TsfNet(ex); }
                if (page is null && problem is null) problem = "form kapanmış ya da bulunamadı";
            }
            else problem = "ilin TSF sitesi bilinmiyor";

            EventRegistration reg;
            if (page is not null)
            {
                _tsfPageCache[tid] = (DateTime.Now, page);
                TsfInfo[tid] = new TsfFormInfo(page.Closed, page.ClosesAt, page.Entries.Count);
            }
            else if (stored is { Entries.Count: > 0 }) TsfInfo[tid] = new TsfFormInfo(false, null, stored.Entries.Count, stored.FetchedAt);
            if (page is { Entries.Count: > 0 })
            {
                reg = new EventRegistration
                {
                    Site = site!, EventName = page.Title, Tids = { tid }, Titles = { page.Title }, FetchedAt = DateTime.Now,
                    Entries = page.Entries, Overrides = stored?.Overrides ?? new(), Resolved = stored?.Resolved ?? new()
                };
                _config.Registrations[key] = reg;
            }
            else if (stored is { Entries.Count: > 0 }) reg = stored;   // site kapalı / form kapanmış: eldeki liste
            else
            {
                // Pencere açmadan tek satır uyarı; aynı uyarı günlüğe üst üste yazılmaz.
                var msg = page is { Entries.Count: 0 }
                    ? $"TSF kayıt formu #{tid}: henüz kayıtlı sporcu yok."
                    : $"TSF kayıt formu #{tid} okunamadı: {problem ?? "kayıt yok"}. Kayıt listesi sitede kalktıysa turnuva chess-results'a yüklenince oradan devam edilir.";
                SetStatus(msg, ok: false);
                LogOnce(msg);
                return;
            }

            bool isNewEvent = _config.Online.SelectedTnr != tnr;
            _eventTnr = tnr;
            var counts = RegistrationCategories.Counts(reg);
            var cats = counts.Select(c => new CategoryRef(StableId(c.Name), c.Name, true)).ToList();
            _roundCache[tnr] = (0, 0, TournamentSystem.Unknown);
            foreach (var c in cats) { _roundCache[c.Tnr] = (0, 0, TournamentSystem.Unknown); _ranked[c.Tnr] = 0; }
            BuildCategoryButtons(cats, null);
            await UpdateRoundsForCategoryAsync(SelectedCategory()?.Tnr ?? tnr); // önbellekten: henüz tur yok

            var name = EventGrouping.BaseName(reg.EventName.Length > 0 ? reg.EventName : reg.Titles.FirstOrDefault() ?? $"Kayıt formu {tid}");
            if (isNewEvent) _config.Tournament.ManualOverride = false;
            if (!_config.Tournament.ManualOverride)
            {
                _config.Tournament.Name = name;
                ApplyAutoDate(null); // kayıt formunda tarih yok: önceki turnuvanın tarihi kalmasın (bugün)
                UpdateInfoLabel();
            }
            _config.Online.SelectedTnr = tnr;
            _config.Online.SelectedTournamentName = name;
            ShowTournamentInCombo(tnr, name);
            SaveConfig();

            _syncedTournament = null;
            ShowPairings(null);
            var summary = string.Join(" · ", counts.Select(c => $"{c.Name} {c.Count}"));
            var state = page is { Closed: true } ? $"kayıtlar bitti{(page.ClosesAt is { } end ? $" ({end:dd.MM HH:mm})" : "")}" : page is null ? "eldeki liste" : "kayıt sürüyor";
            SetStatus($"Turnuva henüz chess-results'ta yok — TSF kayıt listesi ({state}): {reg.Entries.Count} sporcu. Masa kartları bu sayılarla basılabilir.", ok: true);
            LogOnce($"📋 Kayıttaki turnuva: {name} (TSF #{tid}, {state}) — {reg.Entries.Count} sporcu: {summary}" +
                    (page is null ? $" • site okunamadı ({problem}), {reg.FetchedAt:dd.MM HH:mm} tarihli liste kullanılıyor" : ""));
        }
        finally { SetBusy(false); }
    }

    /// <summary>
    /// TSF sitesi hatası: chess-results aynı anda çalışırken "internet yok" demek yanıltıcı olduğundan
    /// sorunun TSF il sitesinde olduğu söylenir (ad çözülemedi, bağlantı reddedildi, HTTP hatası).
    /// </summary>
    private static string TsfNet(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } code } => $"TSF il sitesi hata verdi (HTTP {(int)code}); site geçici olarak sorunlu olabilir.",
        HttpRequestException => "TSF il sitesine ulaşılamıyor (site geçici olarak kapalı olabilir; chess-results turnuvaları yine listelenir).",
        TaskCanceledException => "TSF il sitesi yanıt vermedi.",
        _ => ex.Message
    };

    /// <summary>Kayıttaki turnuvada eşleştirme isteyen işlem: kullanıcıya neden yapılamadığını söyler.</summary>
    private bool BlockedForTsfEvent(string what)
    {
        if (!IsTsfEvent) return false;
        SetStatus($"{what}: turnuva henüz chess-results'ta yok (yalnız TSF kayıt formu var). Turnuva yayımlanınca listeden seçin.", ok: false);
        return true;
    }
}
