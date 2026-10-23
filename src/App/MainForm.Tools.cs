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
    private Button? _btnRegistration;

    private void InitToolButtons()
    {
        _btnReport = HeaderButton("📄 Yönerge / Tutanak", "Turnuva yönergesi, raporu ya da teknik toplantı tutanağını chess-results ve TSF il sitesindeki yönergeyle doldurup PDF olarak kaydet.");
        _btnCards = HeaderButton("🏷 Masa Kartları", "Kategori masa kartlarını (A4 yatay, kategori renginde) bas.");
        _btnBadges = HeaderButton("🪪 Yaka Kartları", "Hakem yaka kartlarını (85×54 / 90×60 mm, A4'e dizili, kesim işaretli) bas.");
        _btnRegistration = HeaderButton("📋 Kayıt / İl", "TSF il sitesindeki turnuva kayıt listesini (turnuva başında bir kez) alıp sporcuların illerini bul; notasyondaki İl kutusuna basılır.");
        _btnRegistration.Click += (_, _) => OpenRegistration();
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
        foreach (var b in new[] { _btnBadges, _btnCards, _btnReport, _btnRegistration })
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
    private List<CategoryRef>? ToolCategories()
    {
        if (!rbOnline.Checked || _eventTnr is not int tnr)
        {
            MessageBox.Show(this, "Önce chess-results'tan bir turnuva seçin.", "Turnuva seçilmedi",
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
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new ReportContext(
            _online, _eventTnr!.Value, CurrentEventName(), cats, CurrentProvince(),
            async t => { var r = await RoundsOfAsync(t); return (r.Max, r.System); },
            _config, () => _config.Save(_configPath), AppContext.BaseDirectory, _outputDir, CategoryStatsAsync);
        using var f = new ReportForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
    }

    private void OpenRegistration()
    {
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new RegistrationContext(_online, _eventTnr!.Value, CurrentEventName(), cats, CurrentProvince(),
            _config, () => _config.Save(_configPath));
        using var f = new RegistrationForm(ctx) { Icon = Icon };
        f.ShowDialog(this);
        ShowPairings(rbOnline.Checked ? _syncedTournament : _fileTournament); // listedeki İl sütunu güncellensin
    }

    /// <summary>Basılacak eşleştirmeye sporcuların illerini ekler (kayıt listesi / elle / öğrenilen).</summary>
    private Tournament WithProvinces(Tournament t)
        => ProvinceBook.Apply(t, _config, rbOnline.Checked ? _eventTnr : null, rbOnline.Checked ? CurrentEventName() : null);

    private void OpenCards()
    {
        var cats = ToolCategories();
        if (cats is null) return;
        UpdateConfigFromUi();
        var ctx = new CardsContext(CurrentEventName(), cats, BoardCountAsync,
            _config, () => _config.Save(_configPath), AppContext.BaseDirectory, _outputDir);
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
        if (!rbOnline.Checked || _eventTnr is not int tnr)
        {
            MessageBox.Show(this, "Önce chess-results'tan bir turnuva seçin.", "Turnuva seçilmedi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        UpdateConfigFromUi();
        var ctx = new BadgesContext(CurrentEventName(), async () => await _online.GetInfoTableAsync(tnr),
            _config, () => _config.Save(_configPath), AppContext.BaseDirectory, _outputDir);
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
        if (r.Current <= 0) return new CategoryStats(0, 0, tc);
        var t = await _online.GetPairingsAsync(c.Tnr, 1, r.System);
        int maxRating = t.Pairings.SelectMany(p => new[] { p.White.Rating, p.Black?.Rating }).Max(x => x ?? 0);
        int players = r.System.IsTeam()
            ? t.Pairings.SelectMany(p => new[] { p.WhiteTeam, p.BlackTeam }).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Count()
            : t.Pairings.Sum(p => p.IsBye ? 1 : 2);
        return new CategoryStats(players, maxRating, tc);
    }

    /// <summary>Kategorinin masa sayısı: son eşlenen turdaki (başlamadıysa 1. tur) BAY dışı masalar.</summary>
    private async Task<int> BoardCountAsync(CategoryRef c)
    {
        var r = await RoundsOfAsync(c.Tnr);
        int round = r.Current > 0 ? r.Current : 1;
        var t = await _online.GetPairingsAsync(c.Tnr, round, r.System);
        return t.Pairings.Count(p => !p.IsBye);
    }
}
