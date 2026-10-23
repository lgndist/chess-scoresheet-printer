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
    private readonly Dictionary<int, string> _tsfFormSites = new();   // form numarası → il sitesi

    /// <summary>Açık turnuva kayıttaki (henüz chess-results'ta olmayan) turnuva mı.</summary>
    private bool IsTsfEvent => _eventTnr is < 0;

    private static string ComboLabel(TournamentRef t)
        => t.Tnr < 0 ? $"{TsfLabel}{t.Name}  (TSF kayıt #{-t.Tnr})" : $"{t.Name}  (#{t.Tnr})";

    /// <summary>
    /// İlin TSF sitesinde kayda açık turnuvalar (chess-results'ta adıyla zaten bulunanlar hariç).
    /// Site erişilemezse boş döner (liste yine chess-results turnuvalarıyla dolar).
    /// </summary>
    private async Task<List<TournamentRef>> TsfOpenFormsAsync(string? province, IReadOnlyList<TournamentRef> chessResults, string? text = null)
    {
        var list = new List<TournamentRef>();
        var site = TsfSites.SiteFor(province, _config.TsfSites);
        if (site is null) return list;
        try
        {
            using var sites = new TsfSites();
            foreach (var o in await sites.ListOpenAsync(site))
            {
                if (chessResults.Any(t => RegistrationMatch.TitleScore(o.Title, t.Name) >= 0.6)) continue;
                if (!string.IsNullOrWhiteSpace(text) && !EventGrouping.Fold(o.Title).Contains(EventGrouping.Fold(text))) continue;
                _tsfFormSites[o.Tid] = site;
                list.Add(new TournamentRef(-o.Tid, o.Title));
            }
        }
        catch (Exception ex) { Log("Uyarı: TSF kayıt formları alınamadı — " + FriendlyNet(ex)); }
        return list;
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
                catch (Exception ex) { problem = FriendlyNet(ex); }
                if (page is null && problem is null) problem = "form kapanmış ya da bulunamadı";
            }
            else problem = "ilin TSF sitesi bilinmiyor";

            EventRegistration reg;
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
                var msg = $"Kayıt formu #{tid} okunamadı ({problem ?? "kayıt yok"}).";
                if (silent) { SetStatus(msg, ok: false); Log("Uyarı: " + msg); }
                else Fail("Kayıt formu okunamadı", msg);
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
            _config.Save(_configPath);

            _syncedTournament = null;
            ShowPairings(null);
            var summary = string.Join(" · ", counts.Select(c => $"{c.Name} {c.Count}"));
            SetStatus($"Turnuva henüz chess-results'ta yok: kategoriler ve sayılar TSF kayıt formundan ({reg.Entries.Count} sporcu). Masa kartları bu sayılarla basılabilir.", ok: true);
            Log($"📋 Kayıttaki turnuva: {name} (TSF #{tid}) — {reg.Entries.Count} sporcu: {summary}" +
                (page is null ? $" • site okunamadı ({problem}), {reg.FetchedAt:dd.MM HH:mm} tarihli liste kullanılıyor" : ""));
        }
        finally { SetBusy(false); }
    }

    /// <summary>Kayıttaki turnuvada eşleştirme isteyen işlem: kullanıcıya neden yapılamadığını söyler.</summary>
    private bool BlockedForTsfEvent(string what)
    {
        if (!IsTsfEvent) return false;
        SetStatus($"{what}: turnuva henüz chess-results'ta yok (yalnız TSF kayıt formu var). Turnuva yayımlanınca listeden seçin.", ok: false);
        return true;
    }
}
