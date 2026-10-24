using NotasyonOtomasyonu.App.Badges;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Registration;
using NotasyonOtomasyonu.App.Reports;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App;

/// <summary>Başlıktaki ek araçlar: yönerge/rapor/tutanak, kategori masa kartları, hakem yaka kartları.</summary>
public partial class MainForm
{
    private Button? _btnReport;
    private Button? _btnCards;
    private Button? _btnBadges;

    private void InitToolButtons()
    {
        _btnReport = HeaderButton("📄 Yönerge / Tutanak", "Turnuva yönergesi, raporu ya da teknik toplantı tutanağını chess-results ve TSF il sitesindeki yönergeyle doldurup PDF olarak kaydet.");
        _btnCards = HeaderButton("🏷 Masa Kartları", "Kategori masa kartlarını (A4 yatay, kategori renginde) bas.");
        _btnBadges = HeaderButton("🪪 Yaka Kartları", "Hakem yaka kartlarını (85×54 / 90×60 mm, A4'e dizili, kesim işaretli) bas.");
        _btnReport.Click += (_, _) => OpenReport();
        _btnCards.Click += (_, _) => OpenCards();
        _btnBadges.Click += (_, _) => OpenBadges();
    }

    private Button HeaderButton(string text, string tip)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Height = btnSettings.Height,
            MinimumSize = new Size(0, btnSettings.Height),
            Padding = new Padding(6, 0, 6, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.White,
            ForeColor = ThGreenDark,
            Font = btnSettings.Font,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = ThGreenDark;
        new ToolTip().SetToolTip(b, tip);
        pnlHeader.Controls.Add(b);
        return b;
    }

    /// <summary>Ayarlar'ın soluna araç butonlarını dizer (PositionHeader'dan çağrılır).</summary>
    private int PositionToolButtons(int right)
    {
        foreach (var b in new[] { _btnBadges, _btnCards, _btnReport, _btnUpdate })
        {
            if (b is null) continue;
            b.Top = (pnlHeader.Height - b.Height) / 2;
            b.Left = right - b.Width - 8;
            b.BringToFront();
            right = b.Left;
        }
        return right;
    }

    /// <summary>Etkinliğin kategorileri; kategori menüsü yoksa etkinliğin kendisi tek kategori sayılır.</summary>
    private List<CategoryRef>? ToolCategories(IWin32Window? owner = null)
    {
        if (!rbOnline.Checked || _eventTnr is not int tnr)
        {
            MessageBox.Show(owner ?? this, "Önce chess-results'tan bir turnuva seçin.", "Turnuva seçilmedi",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }
        var cats = CurrentCategories();
        if (cats.Count == 0)
        {
            var name = _config.Online.SelectedTournamentName ?? $"Turnuva {tnr}";
            cats.Add(new CategoryRef(tnr, EventGrouping.CategoryFromEventName(name) is { Length: > 0 } c ? c : "Genel", true));
        }
        return cats;
    }

    private string CurrentEventName()
        => _config.Online.SelectedTournamentName ?? _config.Tournament.Name ?? "";

    private async Task<(int Max, int Current, TournamentSystem System)> RoundsOfAsync(int catTnr)
    {
        if (_roundCache.TryGetValue(catTnr, out var r)) return r;
        var info = await _online.GetEventAsync(catTnr); _ranked[catTnr] = info.RankedRound;
        r = (info.MaxRound, info.CurrentRound, info.System);
        _roundCache[catTnr] = r;
        return r;
    }

    /// <summary>Turnuva ili: seçili il; yoksa turnuva adında geçen il.</summary>
    private string? CurrentProvince()
    {
        var sel = cboProvince.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(sel) && sel != Provinces.All) return sel;
        return ReportBuilder.DetectProvince(CurrentEventName()); // yoksa rapor turnuva yerinden de dener
    }

    private void OpenReport()
    {
        if (BlockedForTsfEvent("Yönerge / tutanak")) return;
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new ReportContext(
            _online, _eventTnr!.Value, CurrentEventName(), cats, CurrentProvince(),
            async t => { var r = await RoundsOfAsync(t); return (r.Max, r.System); },
            _config, () => SaveConfig(), AppPaths.DataDir, _outputDir, CategoryStatsAsync);
        using var f = new ReportForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
    }

    /// <summary>
    /// Kayıt listesi ve iller penceresi. Liste turnuva açılınca kendiliğinden alındığından ana ekranda
    /// düğmesi yok; Ayarlar → Bölge'den ya da listedeki İl sütununa çift tıklayarak açılır.
    /// </summary>
    private void OpenRegistration(IWin32Window? owner = null)
    {
        owner ??= this;
        if (IsTsfEvent && _config.Registrations.GetValueOrDefault(_eventTnr!.Value.ToString()) is { } treg)
        {
            MessageBox.Show(owner,
                $"Bu turnuvanın TSF kayıt listesi alındı: {treg.Entries.Count} sporcu ({treg.FetchedAt:dd.MM.yyyy HH:mm}).\n\n" +
                string.Join("\n", RegistrationCategories.Counts(treg).Select(c => $"• {c.Name}: {c.Count} sporcu → {c.Boards} masa")) +
                "\n\nSporcular chess-results'taki eşleştirmeyle turnuva yayımlanınca eşlenir; iller notasyona o zaman basılır.",
                "Kayıt listesi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var cats = ToolCategories(owner);
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new RegistrationContext(_online, _eventTnr!.Value, CurrentEventName(), cats, CurrentProvince(),
            _config, () => SaveConfig());
        using var f = new RegistrationForm(ctx) { Icon = Icon };
        f.ShowDialog(owner);
        ShowPairings(rbOnline.Checked ? _syncedTournament : _fileTournament); // listedeki İl sütunu güncellensin
    }

    private int _regSyncTnr;
    private readonly Dictionary<int, DateTime> _regSyncMiss = new(); // formu bulunamayan turnuva → son deneme

    /// <summary>
    /// Turnuvanın TSF kayıt listesini arka planda getirir (yoksa) ya da yeniler (6 saatten eskiyse).
    /// İller ve masa kartı adetleri bu listeden gelir. Aynı turnuva için aynı anda tek eşitleme çalışır.
    /// </summary>
    private async Task SyncRegistrationAsync(int tnr, bool force = false)
    {
        if (_regSyncTnr == tnr) return;
        // Kayıt formu az önce bulunamadıysa her açılışta siteyi yeniden yoklama (30 dk).
        if (!force && _regSyncMiss.TryGetValue(tnr, out var miss) && DateTime.Now - miss < TimeSpan.FromMinutes(30)
            && ProvinceBook.For(_config, tnr) is null) return;
        _regSyncTnr = tnr;
        try
        {
            var cats = CurrentCategories();
            if (cats.Count == 0) cats.Add(new CategoryRef(tnr, CurrentEventName(), true));
            var before = _config.Registrations.GetValueOrDefault(tnr.ToString());
            var r = await RegistrationSync.EnsureAsync(_config, _online, tnr, CurrentEventName(), CurrentProvince(), cats, force);
            if (_eventTnr != tnr) return; // bu arada başka turnuva açıldı
            if (r.Changed || !ReferenceEquals(before, r.Registration)) SaveConfig();
            if (r.Message.Length > 0) LogOnce(r.Message);
            if (r.Registration is null) _regSyncMiss[tnr] = DateTime.Now; else _regSyncMiss.Remove(tnr);
            if (r.Changed) ShowPairings(rbOnline.Checked ? _syncedTournament : _fileTournament); // İl sütunu
        }
        catch (Exception ex) { LogOnce("Uyarı: kayıt listesi eşitlenemedi — " + ex.Message); }
        finally { if (_regSyncTnr == tnr) _regSyncTnr = 0; }
    }

    /// <summary>Basılacak eşleştirmeye sporcuların illerini ekler (kayıt listesi / elle / öğrenilen).</summary>
    private Tournament WithProvinces(Tournament t)
        => ProvinceBook.Apply(t, _config, rbOnline.Checked ? _eventTnr : null, rbOnline.Checked ? CurrentEventName() : null);

    private void OpenCards()
    {
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new CardsContext(CurrentEventName(), CardCategories(cats), BoardCountAsync,
            _config, () => SaveConfig(), AppPaths.DataDir, _outputDir);
        using var f = new CardsForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
    }

    private readonly ToolTip _printTip = new();

    /// <summary>"Doğrudan yazdır" açık/kapalıya göre yazdır düğmelerinin açıklaması.</summary>
    private void UpdatePrintButtonsForSilent()
    {
        bool silent = Overlay.PrintRouter.IsSilent;
        _printTip.SetToolTip(btnPrint, silent
            ? $"Doğrudan yazdır açık: önizleme açılmadan “{Overlay.PrintRouter.TargetName}” yazıcısına gönderilir (Ctrl+P)."
            : "Önizleme açılır; oradan yazıcı seçip basılır (Ctrl+P).");
        UpdateSelectionSummary();
        UpdatePrinterButton();
    }

    private void OpenBadges()
    {
        if (BlockedForTsfEvent("Yaka kartları (hakem listesi chess-results'tan gelir)")) return;
        if (!rbOnline.Checked || _eventTnr is not int tnr)
        {
            MessageBox.Show(this, "Önce chess-results'tan bir turnuva seçin.", "Turnuva seçilmedi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        UpdateConfigFromUi();
        var ctx = new BadgesContext(CurrentEventName(), async () => await _online.GetInfoTableAsync(tnr),
            _config, () => SaveConfig(), AppPaths.DataDir, _outputDir);
        using var f = new BadgesForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
    }

    /// <summary>
    /// Kategori bilgisi (TSF kontrolleri için): sporcu (takımda takım) sayısı ve en yüksek rating
    /// 1. tur eşleştirmesinden (BAY alan sporcu da sayılır), zaman kontrolü kategorinin bilgi sayfasından.
    /// </summary>
    private async Task<CategoryStats> CategoryStatsAsync(CategoryRef c)
    {
        string? tc = null;
        try
        {
            var info = await _online.GetInfoTableAsync(c.Tnr);
            tc = info.FirstOrDefault(k => k.Key.StartsWith("Zaman kontrol", StringComparison.OrdinalIgnoreCase)).Value;
        }
        catch { /* tempo bilinmiyorsa birlik kontrolü bu kategoriyi atlar */ }
        var r = await RoundsOfAsync(c.Tnr);
        if (r.Current <= 0)
        {
            // Turnuva başlamadı: sporcu sayısı TSF kayıt listesinden (EK-C sistem/tur kontrolü için).
            var reg = ProvinceBook.For(_config, _eventTnr, CurrentEventName());
            return new CategoryStats(RegistrationCategories.For(reg, c.Name)?.Count ?? 0, 0, tc);
        }
        var t = await _online.GetPairingsAsync(c.Tnr, 1, r.System);
        int maxRating = t.Pairings.SelectMany(p => new[] { p.White.Rating, p.Black?.Rating }).Max(x => x ?? 0);
        int players = r.System.IsTeam()
            ? t.Pairings.SelectMany(p => new[] { p.WhiteTeam, p.BlackTeam }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count()
            : t.Pairings.Sum(p => p.IsBye ? 1 : 2);
        return new CategoryStats(players, maxRating, tc);
    }

    /// <summary>Kategorinin masa sayısı: son eşlenen turdaki (başlamadıysa 1. tur) BAY dışı masalar.</summary>
    /// <summary>
    /// Kategorinin masa sayısı: önce turnuvanın TSF kayıt listesindeki sporcu sayısından (masa = sporcu ÷ 2;
    /// turnuva başında basılan kâğıtlar buna göre), kayıt yoksa chess-results'taki son eşlenen turdan.
    /// </summary>
    private async Task<(int Boards, string Source)> BoardCountAsync(CategoryRef c)
    {
        var reg = ProvinceBook.For(_config, _eventTnr, CurrentEventName());
        if (RegistrationCategories.For(reg, c.Name) is { Count: > 0 } rc)
            return (rc.Boards, $"TSF kayıt listesi: {rc.Name} — {rc.Count} sporcu → {rc.Boards} masa" + (rc.Count % 2 == 1 ? " (1 BAY)" : ""));
        if (c.Tnr <= 0) return (0, "Kayıt listesinde bu kategori yok");
        var r = await RoundsOfAsync(c.Tnr);
        int round = r.Current > 0 ? r.Current : 1;
        var t = await _online.GetPairingsAsync(c.Tnr, round, r.System);
        return (t.Pairings.Count(p => !p.IsBye), $"chess-results: {round}. tur eşleştirmesi (kayıt listesi yok)");
    }

    /// <summary>
    /// Kartı basılacak kategoriler: chess-results kategorileri + kayıt listesinde olup chess-results'ta
    /// (henüz) olmayanlar. Kayıt kategorilerinin kimliği adından türetilir (renk hatırlansın diye kalıcı).
    /// </summary>
    private List<CategoryRef> CardCategories(List<CategoryRef> cats)
    {
        var reg = ProvinceBook.For(_config, _eventTnr, CurrentEventName());
        if (reg is not { Entries.Count: > 0 }) return cats;
        var list = cats.ToList();
        foreach (var rc in RegistrationCategories.Counts(reg))
            if (!cats.Any(c => RegistrationCategories.Same(rc.Name, c.Name)))
                list.Add(new CategoryRef(StableId(rc.Name), rc.Name, false));
        return list;
    }

    /// <summary>Addan kalıcı negatif kimlik (FNV-1a): chess-results numaralarıyla çakışmaz.</summary>
    private static int StableId(string name)
    {
        uint h = 2166136261;
        foreach (var ch in EventGrouping.Fold(name)) { h ^= ch; h *= 16777619; }
        return -(int)(h & 0x3FFFFFFF) - 1;
    }
}
