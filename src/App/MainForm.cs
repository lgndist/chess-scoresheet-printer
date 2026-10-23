using System.Diagnostics;
using System.Text;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using NotasyonOtomasyonu.Parsers;
using NotasyonOtomasyonu.Render;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Tek pencereli arayüz. Kaynak: chess-results (online) veya dosya.
/// Online akış: ara/seç (il+ad) → kategori+tur (yan yana butonlar) → Senkron Et → Oluştur/Yazdır.
/// </summary>
public partial class MainForm : Form
{
    private readonly string _configPath;
    private readonly string _outputDir;
    private AppConfig _config;
    private readonly ChessResultsService _online = new();

    private Tournament? _syncedTournament;   // online: son çekilen kategori+tur
    private Tournament? _fileTournament;     // dosya: son okunan dosya
    private Tournament? _gridTournament;     // sağdaki listede gösterilen (basılacak olan)
    private int? _eventTnr;
    private bool _loading;
    private int _syncSeq;                    // üst üste tıklamada eski çekim sonucunu atmak için

    /// <summary>
    /// Kategori (Tnr) → (toplam tur, eşlenmiş son tur, sistem). Her kategori farklı turda ve
    /// farklı sistemde (İsviçre / Berger / takım) olabilir.
    /// </summary>
    private readonly Dictionary<int, (int Max, int Current, TournamentSystem System)> _roundCache = new();

    /// <summary>
    /// (kategori, tur) → işareti KALDIRILMIŞ masalar. Seçim her kategori/tur için ayrı tutulur:
    /// başka kategoriye geçip dönünce kaybolmaz; "Tüm Kategoriler" de bu seçime uyar.
    /// </summary>
    private readonly Dictionary<(int Cat, int Round), HashSet<int>> _unchecked = new();
    private (int Cat, int Round)? _gridKey;  // listede gösterilen kategori/tur (dosya modunda null)

    public MainForm()
    {
        InitializeComponent();
        InitToolButtons();
        InitListButtons();
        InitRegistrationView();
        InitUx();
        LoadBranding();
        var baseDir = AppContext.BaseDirectory;
        _configPath = Path.Combine(baseDir, "config.json");
        _outputDir = Path.Combine(baseDir, "outputs");
        Directory.CreateDirectory(_outputDir);

        _config = AppConfig.Load(_configPath);
        foreach (var (k, boards) in _config.Online.Selections) // kalıcı masa seçimleri
        {
            var parts = k.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var c) && int.TryParse(parts[1], out var r))
                _unchecked[(c, r)] = boards.ToHashSet();
        }
        OverlayDefaults.EnsureDefaults(_config, baseDir); // Ana Örnek varsayılan şablonu hazırla
        Overlay.PrintRouter.Config = _config.Printing;     // hedef yazıcı / tepsi / yazıcı tercihleri / doğrudan yazdır
        ApplyConfigToUi();

        // Başlık çubuğundaki "⚙ Ayarlar" butonunu her boyutta sağ üste sabitle
        // (docked panel + anchor, DPI ölçeklemede butonu ekran dışına itebiliyordu).
        pnlHeader.Resize += (_, _) => PositionHeader();
        PositionHeader();
        // Yazı tipi ölçeklemesi (DPI) dikeyde büyütünce alttan çapalı günlük kutusu pencereden
        // taşıyordu; yüksekliği her boyut değişiminde pencereye göre hesapla.
        Load += (_, _) => { CaptureLayout(); RelayoutLeft(); };
        Resize += (_, _) => FitLog();

        var tip = new ToolTip();
        tip.SetToolTip(btnSync, "Eşleştirmeleri yeniden çek (F5). Kategori/tur seçince zaten otomatik çekilir.");
        tip.SetToolTip(btnPdf, "İşaretli masaları PDF olarak kaydedip açar.");
        tip.SetToolTip(numCopies, "Her masa için kaç kağıt (2 = iki oyuncuya birer notasyon).");

        dgvPairings.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (dgvPairings.IsCurrentCellDirty) dgvPairings.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        dgvPairings.CellValueChanged += (_, e) => { if (e.ColumnIndex == 0) UpdateSelectionSummary(); };
        dgvPairings.KeyDown += dgvPairings_KeyDown;
        // İl sütununa çift tıklama: kayıt listesi / il düzeltme penceresi
        dgvPairings.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && dgvPairings.Columns[e.ColumnIndex].Name is "colWIl" or "colBIl")
                OpenRegistration();
        };
        ShowPairings(null);

        ApplyTheme(); // genel görsel cila (hover/flat, başlıklar, palet)
        ComboKeySearch.AttachAll(this); // il/turnuva listelerinde harfle seçim (I → Iğdır, Isparta …)
        UpdatePrintButtonsForSilent();
        UpdateProvinceLogo();

        // İlk açılışta: hatırlanan turnuva varsa otomatik yükle + eşleştirmeleri çek.
        Shown += MainForm_Shown;
    }

    // ================= Görsel tema =================
    private static readonly Color ThGreen     = Color.FromArgb(118, 150, 86);
    private static readonly Color ThGreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color ThGreenHover= Color.FromArgb(134, 168, 100);
    private static readonly Color ThBorder    = Color.FromArgb(208, 208, 198);
    private static readonly Color ThHoverLite = Color.FromArgb(236, 241, 231);
    private static readonly Color ThDownLite  = Color.FromArgb(224, 231, 216);

    /// <summary>Tüm butonlara tutarlı flat + hover/pressed stilini ve grup başlık rengini uygular.</summary>
    private void ApplyTheme()
    {
        ThemeRecursive(this);
    }

    private void ThemeRecursive(Control parent)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case Button b: StyleButton(b); break;
                case GroupBox g: g.ForeColor = ThGreenDark; break;
            }
            if (c.HasChildren) ThemeRecursive(c);
        }
    }

    /// <summary>Yeşil zeminli butonlar = birincil; diğerleri = beyaz/nötr. İkisine de hover/pressed.</summary>
    private void StyleButton(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 1;
        b.Cursor = Cursors.Hand;

        bool primary = b.BackColor == ThGreen;
        if (primary)
        {
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = ThGreenDark;
            b.FlatAppearance.MouseOverBackColor = ThGreenHover;
            b.FlatAppearance.MouseDownBackColor = ThGreenDark;
        }
        else
        {
            if (b.BackColor == SystemColors.Control) b.BackColor = Color.White;
            b.FlatAppearance.BorderColor = ThBorder;
            b.FlatAppearance.MouseOverBackColor = ThHoverLite;
            b.FlatAppearance.MouseDownBackColor = ThDownLite;
        }
    }

    private void FitLog()
        => txtLog.Height = Math.Max(40, ClientSize.Height - txtLog.Top - 12);

    /// <summary>Ayarlar butonunu sağ üste, başlığı da ona değmeyecek genişliğe ayarlar.</summary>
    private void PositionHeader()
    {
        int margin = 12;
        btnSettings.Top = (pnlHeader.Height - btnSettings.Height) / 2;
        btnSettings.Left = pnlHeader.ClientSize.Width - btnSettings.Width - margin;
        btnSettings.BringToFront();
        int right = PositionToolButtons(btnSettings.Left);
        lblTitle.Width = Math.Max(80, right - lblTitle.Left - 8);
    }

    /// <summary>Gömülü ikon ve başlık logosunu yükler (tek dosya exe'de de çalışır).</summary>
    private void LoadBranding()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        try
        {
            using var ico = asm.GetManifestResourceStream("NotasyonOtomasyonu.App.app.ico");
            if (ico is not null) Icon = new System.Drawing.Icon(ico);
        }
        catch { /* ikon yoksa varsayılan */ }
        pbLogo.SetBounds(8, 5, 46, 46);
        // Logo kutunun gerçek piksel boyutunda çizilir (yüksek DPI'da da keskin; ölçekleme yok).
        pbLogo.SizeMode = PictureBoxSizeMode.CenterImage;
        pbLogo.SizeChanged += (_, _) => { _logoProvince = null; if (_config is not null) UpdateProvinceLogo(); };
        _logoTip.SetToolTip(pbLogo, "Notasyon Kâğıdı Yazdırıcı");
    }

    private readonly ToolTip _logoTip = new();
    private string? _logoProvince;

    /// <summary>
    /// Başlıktaki logo: Ayarlar'daki ilin TSF logosu; Ayarlar'da "TSF genel logosu" seçiliyse genel TSF
    /// logosu; il seçilmemişse uygulama logosu.
    /// </summary>
    private void UpdateProvinceLogo()
    {
        var prov = _config.LogoProvince();
        bool general = prov is null && _config.GeneralTsfLogo;
        int px = Math.Min(pbLogo.ClientSize.Width, pbLogo.ClientSize.Height);
        string key = (prov ?? (general ? "(genel)" : "")) + "@" + px;
        if (key == _logoProvince && pbLogo.Image is not null) return;
        _logoProvince = key;
        var old = pbLogo.Image;
        try
        {
            pbLogo.Image = prov is null && !general ? Branding.OfficialLogos.AppLogo(px) : Branding.OfficialLogos.Tsf(prov, px);
            _logoTip.SetToolTip(pbLogo, prov is null && !general ? "Notasyon Kâğıdı Yazdırıcı" : Branding.OfficialLogos.Caption(prov));
        }
        catch { pbLogo.Image = null; }
        if (old is not null && old != pbLogo.Image) old.Dispose();
    }

    private async void MainForm_Shown(object? sender, EventArgs e)
    {
        Shown -= MainForm_Shown; // yalnızca ilk açılışta
        _ = CheckForUpdateOnStartAsync(); // arka planda; turnuva yüklemesini bekletmez
        try { if (rbOnline.Checked) await AutoSelectLatestAsync(); }
        finally { AnnounceUpdate(); }
    }

    /// <summary>
    /// İlk açılışta: seçili ildeki EN GÜNCEL turnuvayı açar; il yoksa/bulunamazsa hatırlanan
    /// turnuvaya düşer.
    /// </summary>
    private async Task AutoSelectLatestAsync()
    {
        string? prov = cboProvince.SelectedItem?.ToString();
        bool hasProvince = !string.IsNullOrWhiteSpace(prov) && prov != Provinces.All;
        if (hasProvince && await SelectLatestInProvinceAsync(prov!)) return;

        // Düşüş: il yoksa veya bulunamadıysa, en son kullanılan turnuvayı dene.
        if (_config.Online.SelectedTnr is int tnr) await LoadEventAsync(tnr, silent: true);
    }

    /// <summary>
    /// İldeki EN GÜNCEL turnuvayı (en yüksek Tnr ≈ en son eklenen) bulur, listeyi doldurur,
    /// onu seçer ve eşleştirmeleri çeker. Bulduysa true. Çevrimdışıysa sessizce geçer.
    /// </summary>
    private async Task<bool> SelectLatestInProvinceAsync(string prov)
    {
        try
        {
            {
                SetBusy(true);
                SetStatus($"{prov} ilindeki güncel turnuvalar yükleniyor…", ok: true);
                var list = await _online.SearchTurkeyAsync("", prov);
                var tsf = await TsfOpenFormsAsync(prov, list);
                if (list.Count > 0 || tsf.Count > 0)
                {
                    var ordered = list.OrderByDescending(t => t.Tnr).ToList();
                    _loading = true;
                    cboTournament.Items.Clear();
                    foreach (var tr in tsf.Concat(ordered))
                        cboTournament.Items.Add(new Item<TournamentRef>(ComboLabel(tr), tr));
                    // Kullanıcı en son kayıttaki bir turnuvayla çalışıyorsa (form hâlâ açık) o; yoksa en güncel chess-results.
                    var latest = tsf.FirstOrDefault(t => t.Tnr == _config.Online.SelectedTnr) ?? ordered.FirstOrDefault() ?? tsf[0];
                    cboTournament.SelectedIndex = cboTournament.Items.Cast<Item<TournamentRef>>().ToList().FindIndex(i => i.Value.Tnr == latest.Tnr);
                    _loading = false;

                    SetStatus($"{prov} — en güncel: {latest.Name}", ok: true);
                    SetBusy(false);
                    await LoadEventAsync(latest.Tnr, silent: true, preferRememberedCategory: false);
                    return true;
                }
                SetStatus($"{prov} için turnuva bulunamadı. Ara/Getir ile deneyin.", ok: false);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Otomatik yükleme yapılamadı (çevrimdışı?).", ok: false);
            Log("Uyarı: " + FriendlyNet(ex));
        }
        finally { SetBusy(false); }
        return false;
    }

    // ================= Config <-> UI =================
    private void ApplyConfigToUi()
    {
        _loading = true;

        cboProvince.Items.Clear();
        cboProvince.Items.AddRange(Provinces.List);
        // Açılışta yalnız Ayarlar'daki varsayılan il seçili gelir; varsayılan yoksa boş.
        if (HasDefaultProvince) cboProvince.SelectedItem = _config.Online.Province;
        else cboProvince.SelectedIndex = -1;

        // Eski config'te ada kategori eki sızmış olabilir → temizle (kalıcı düzelir).
        _config.Tournament.Name = EventGrouping.BaseName(_config.Tournament.Name);

        numRound.Value = Clamp(_config.Tournament.LastRound, (int)numRound.Minimum, (int)numRound.Maximum);
        numCopies.Value = Clamp(_config.Layout.CopiesPerBoard, (int)numCopies.Minimum, (int)numCopies.Maximum);
        UpdateInfoLabel();

        txtSearch.Text = _config.Online.CityFilter;

        var o = _config.Online;
        if (o.SelectedTnr is int tnr)
        {
            _eventTnr = tnr;
            cboTournament.Items.Clear();
            var label = o.SelectedTournamentName ?? $"Turnuva {tnr}";
            var tr = new TournamentRef(tnr, label);
            cboTournament.Items.Add(new Item<TournamentRef>(tnr < 0 ? ComboLabel(tr) : label, tr));
            cboTournament.SelectedIndex = 0;
        }
        if (o.SelectedCategoryTnr is int ctnr)
        {
            // Tek kategori etiketini geri yükle (Senkron Et ile güncellenecek).
            BuildCategoryButtons(new[] { new CategoryRef(ctnr, o.SelectedCategoryName ?? $"Kategori {ctnr}", true) }, ctnr);
        }

        if (string.Equals(_config.Online.Source, "Dosya", StringComparison.OrdinalIgnoreCase)) rbFile.Checked = true;
        else rbOnline.Checked = true;

        _loading = false;
        UpdateSourceVisibility();
    }

    private void UpdateConfigFromUi()
    {
        // Ad/zaman config'te tutulur (oto-doldurulur ya da Düzenle ile); burada UI'dan okunmaz.
        _config.Online.CityFilter = txtSearch.Text.Trim();
        _config.Online.Source = rbFile.Checked ? "Dosya" : "Online";
        if (rbFile.Checked) _config.Tournament.LastRound = (int)numRound.Value;
    }

    private void UpdateInfoLabel()
    {
        // Sadece notasyon kağıdına basılan ana bilgiyi göster: turnuva adı (kategori eki olmadan).
        // Tarih önce: uzun turnuva adı iki satıra taşınca tarih satırı kutudan taşıp görünmüyordu.
        var n = string.IsNullOrWhiteSpace(_config.Tournament.Name) ? "—" : EventGrouping.BaseName(_config.Tournament.Name);
        var d = string.IsNullOrWhiteSpace(_config.Tournament.Date) ? "" : "Tarih: " + _config.Tournament.Date + "\n";
        lblInfo.AutoEllipsis = true;
        lblInfo.Text = $"{d}Turnuva: {n}";
    }

    private void btnEditInfo_Click(object? sender, EventArgs e)
    {
        using var dlg = new MetaEditForm(_config);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _config.Tournament.ManualOverride = true; // artık senkronda ezilmesin
        _config.Save(_configPath);
        UpdateInfoLabel();
        SetStatus("Turnuva bilgisi güncellendi (kalıcı).", ok: true);
    }

    private void btnExclude_Click(object? sender, EventArgs e)
    {
        // Mevcut çekili masaları önizleme için topla (varsa).
        IReadOnlyList<int> boards = Array.Empty<int>();
        try
        {
            if (rbOnline.Checked && _syncedTournament is not null)
                boards = _syncedTournament.Pairings.Select(p => p.Board).Distinct().OrderBy(x => x).ToList();
        }
        catch { /* önizleme opsiyonel */ }

        using var dlg = new ExclusionsForm(_config, boards);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _config.Save(_configPath);
        _unchecked.Clear();            // seçimler yeni hariç tutma kuralına göre yeniden kurulsun
        _config.Online.Selections.Clear();
        ShowPairings(_gridTournament); // varsayılan işaretler yeni hariç tutmaya göre
        var ex = BoardRange.Parse(_config.Layout.ExcludedBoards);
        var parts = new List<string>();
        if (ex.Count > 0) parts.Add($"{ex.Count} masa hariç");
        SetStatus(parts.Count > 0 ? "Hariç tutma kaydedildi: " + string.Join(", ", parts) + "." : "Hariç tutma temizlendi.", ok: true);
    }

    private void source_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        UpdateSourceVisibility();
    }

    private void UpdateSourceVisibility()
    {
        grpOnline.Visible = rbOnline.Checked;
        grpFile.Visible = !rbOnline.Checked;
        grpQuick.Visible = rbOnline.Checked;
        if (_grpLists is not null) _grpLists.Visible = rbOnline.Checked;
        RelayoutLeft();
        ShowPairings(rbOnline.Checked ? _syncedTournament : _fileTournament);
    }

    // İl seçilince o ilin en son eklenen turnuvası kendiliğinden açılır.
    // Ana ekrandaki seçim varsayılanı değiştirmez (varsayılan il Ayarlar'dandır). Klavyeyle illerde hızlı
    // gezinirken her harfte arama başlamasın: seçim yarım saniye sabit kalınca yüklenir.
    private int _provinceSeq;
    private async void cboProvince_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        int seq = ++_provinceSeq;
        await Task.Delay(500);
        if (seq != _provinceSeq) return;
        var prov = cboProvince.SelectedItem?.ToString() ?? Provinces.All;
        if (rbOnline.Checked && prov != Provinces.All) await SelectLatestInProvinceAsync(prov);
    }

    /// <summary>Ayarlar'da varsayılan il seçilmiş mi.</summary>
    private bool HasDefaultProvince => !string.IsNullOrWhiteSpace(_config.Online.Province) && _config.Online.Province != Provinces.All
                                       && Provinces.List.Contains(_config.Online.Province);

    // ================= Online: arama =================
    private async void btnSearch_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        SetStatus("Türkiye turnuvaları aranıyor…", ok: true);
        try
        {
            string? prov = cboProvince.SelectedItem?.ToString();
            if (prov == Provinces.All) prov = null;
            var list = await _online.SearchTurkeyAsync(txtSearch.Text, prov);
            var tsf = prov is null ? new List<TournamentRef>() : await TsfOpenFormsAsync(prov, list, txtSearch.Text);

            _loading = true;
            cboTournament.Items.Clear();
            foreach (var tr in tsf.Concat(list))
                cboTournament.Items.Add(new Item<TournamentRef>(ComboLabel(tr), tr));
            _loading = false;

            if (cboTournament.Items.Count == 0)
                SetStatus("Eşleşen turnuva yok. Aramayı değiştirin ya da link/no kullanın.", ok: false);
            else { cboTournament.DroppedDown = true; SetStatus($"{cboTournament.Items.Count} turnuva listelendi.", ok: true); }
            UpdateConfigFromUi();
        }
        catch (Exception ex) { Fail("Arama başarısız", FriendlyNet(ex)); }
        finally { SetBusy(false); }
    }

    private async void cboTournament_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (cboTournament.SelectedItem is Item<TournamentRef> item) await LoadEventAsync(item.Value.Tnr);
    }

    // "Gelişmiş: link/no ile getir" — küçük bir giriş penceresi açar (akışı bölmesin diye gizli).
    private async void lnkAdvanced_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        var input = ShowLinkPrompt();
        if (string.IsNullOrWhiteSpace(input)) return;
        var tnr = ChessResultsClient.ParseTnr(input);
        if (tnr is null)
        {
            MessageBox.Show("Geçerli bir chess-results linki veya numarası girin.\nÖrn: 1437263",
                "Geçersiz giriş", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        await LoadEventAsync(tnr.Value);
    }

    private static string? ShowLinkPrompt()
    {
        using var f = new Form
        {
            Text = "Link / No ile Getir", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
            ClientSize = new System.Drawing.Size(420, 120), Font = new System.Drawing.Font("Segoe UI", 9.75F)
        };
        f.Controls.Add(new Label { Text = "chess-results linki veya turnuva no:", AutoSize = true, Location = new System.Drawing.Point(14, 14) });
        var tb = new TextBox();
        tb.SetBounds(14, 40, 392, 25);
        f.Controls.Add(new Label { Text = "Örn: 1437263  •  https://chess-results.com/Tnr1437263.aspx", AutoSize = true, ForeColor = System.Drawing.Color.Gray, Location = new System.Drawing.Point(14, 68) });
        f.Controls.Add(tb);
        var ok = new Button { Text = "Getir", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(232, 84), Size = new System.Drawing.Size(84, 28) };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(322, 84), Size = new System.Drawing.Size(84, 28) };
        f.Controls.Add(ok); f.Controls.Add(cancel);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
    }

    private async Task LoadEventAsync(int tnr, bool silent = false, bool preferRememberedCategory = true)
    {
        if (tnr < 0) { await LoadRegistrationEventAsync(tnr, silent); return; } // TSF kayıttaki turnuva
        int currentRound;
        bool detailsHidden;
        IReadOnlyList<CategoryRef> cats;
        SetBusy(true);
        SetStatus("Turnuva bilgisi çekiliyor…", ok: true);
        try
        {
            var info = await _online.GetEventAsync(tnr); _ranked[tnr] = info.RankedRound;
            bool isNewEvent = _config.Online.SelectedTnr != tnr;
            _eventTnr = tnr;
            _roundCache[tnr] = (info.MaxRound, info.CurrentRound, info.System); // bu kategorinin turları
            // Eski/bitmiş turnuvada chess-results ayrıntıları gizleyebilir (tarih, tip, tur menüsü
            // yok); o zaman "yayımlanmamış" demek yerine eşleştirmeyi yine de çekmeyi dene.
            detailsHidden = string.IsNullOrWhiteSpace(info.Dates) && info.System == TournamentSystem.Unknown;
            cats = info.Categories;

            int? preferCat = preferRememberedCategory ? _config.Online.SelectedCategoryTnr : null;
            BuildCategoryButtons(info.Categories, preferCat);
            // Seçili kategorinin GERÇEK turlarına göre tur butonları (kategoriye göre değişebilir).
            currentRound = await UpdateRoundsForCategoryAsync(SelectedCategory()?.Tnr ?? tnr);

            // Farklı turnuva seçildiyse elle override sıfırlanır → çekilen veri kullanılır.
            if (isNewEvent) _config.Tournament.ManualOverride = false;
            var baseName = EventGrouping.BaseName(info.Name); // "… A Kategorisi" ekini at
            ApplyAutoDate(info.Dates);                        // tek tarih (bugün/turnuva günü)
            if (!_config.Tournament.ManualOverride)
            {
                _config.Tournament.Name = baseName;
                UpdateInfoLabel();
            }

            _config.Online.SelectedTnr = tnr;
            _config.Online.SelectedTournamentName = baseName;
            ShowTournamentInCombo(tnr, baseName); // link/no ile açılınca listede eski ad kalmasın

            Log($"Etkinlik yüklendi: {info.Name} (#{tnr})");
        }
        catch (Exception ex)
        {
            if (silent) { SetStatus("Turnuva otomatik yüklenemedi (çevrimdışı?). Ara/Getir ile deneyin.", ok: false); Log("Uyarı: " + FriendlyNet(ex)); }
            else Fail("Turnuva bilgisi alınamadı", FriendlyNet(ex));
            return;
        }
        finally { SetBusy(false); }

        // Kısayollar için diğer kategorilerin sistem/son turunu arka planda öğren.
        _ = PrefetchCategoryRoundsAsync(cats, tnr);
        // Turnuvanın TSF kayıt listesi (il ve kategori sayıları) arka planda getirilir/yenilenir.
        _ = SyncRegistrationAsync(tnr);

        if (currentRound == 0 && !detailsHidden)
        {
            // Başlamamış turnuva: çekilecek eşleştirme yok, hata gibi göstermeyelim.
            _syncedTournament = null;
            ShowPairings(null);
            SetStatus("Bu turnuvada henüz eşleştirme yayımlanmamış (turnuva başlamamış olabilir).", ok: false);
            return;
        }

        // Turnuva seçilir seçilmez varsayılan kategori+turu OTOMATİK çek (kullanıcı butona basmasın).
        await SyncPairingsAsync(showWarnings: false);
    }

    /// <summary>
    /// Etkinliğin tüm kategorilerinin turlarını/sistemini sırayla öğrenip kısayol düğmelerine
    /// yazar ("7 Yaş · T2"). Bu arada başka turnuva açılırsa bırakır.
    /// </summary>
    private async Task PrefetchCategoryRoundsAsync(IReadOnlyList<CategoryRef> cats, int eventTnr)
    {
        foreach (var c in cats)
        {
            if (_eventTnr != eventTnr) return;
            if (!_roundCache.ContainsKey(c.Tnr))
            {
                try
                {
                    var info = await _online.GetEventAsync(c.Tnr); _ranked[c.Tnr] = info.RankedRound;
                    _roundCache[c.Tnr] = (info.MaxRound, info.CurrentRound, info.System);
                }
                catch { continue; }
            }
            if (_eventTnr == eventTnr) UpdateQuickButton(c);
        }
    }

    /// <summary>Kategorinin sistemi (önbellekte yoksa bilinmiyor → sayfadan anlaşılır).</summary>
    private TournamentSystem SystemOf(int catTnr)
        => _roundCache.TryGetValue(catTnr, out var r) ? r.System : TournamentSystem.Unknown;

    /// <summary>Turnuva kutusunda yüklü etkinliği gösterir (listede yoksa ekler); seçim olayı tetiklenmez.</summary>
    private void ShowTournamentInCombo(int tnr, string name)
    {
        // Aynı etkinliğin başka kategori tnr'siyle listelenmiş olabilir: ada göre de eşleştir.
        var existing = cboTournament.Items.OfType<Item<TournamentRef>>()
            .FirstOrDefault(i => i.Value.Tnr == tnr || i.Value.Name == name);
        bool prev = _loading;
        _loading = true;
        if (existing is null)
        {
            var tr = new TournamentRef(tnr, name);
            existing = new Item<TournamentRef>(ComboLabel(tr), tr);
            cboTournament.Items.Insert(0, existing);
        }
        cboTournament.SelectedItem = existing;
        _loading = prev;
    }

    // ---- kategori / tur buton grupları ----
    private void BuildCategoryButtons(IReadOnlyList<CategoryRef> cats, int? preferTnr)
    {
        _loading = true;
        flowCategories.Controls.Clear();
        int idx = 0, sel = 0;
        foreach (var c in cats)
        {
            var rb = MakeToggle(ButtonText(c), c);
            rb.Tag = c;
            rb.CheckedChanged += category_CheckedChanged;
            flowCategories.Controls.Add(rb);
            if (preferTnr is int p && c.Tnr == p) sel = idx;
            idx++;
        }
        // Varsayılan: tercih edilen ya da ilk kategori
        var buttons = flowCategories.Controls.OfType<RadioButton>().ToList();
        if (buttons.Count > 0) buttons[Math.Min(sel, buttons.Count - 1)].Checked = true;
        _loading = false;

        BuildQuickButtons(cats); // kategori · son tur kısayolları
        RelayoutLeft();
    }

    /// <summary>
    /// Her kategori için TEK TIKLA BASKI düğmesi: o kategorinin EN SON eşlenmiş turunu gösterir
    /// ("7 Yaş · T2", başka kategoride "A · T3") ve tıklanınca o turun işaretli masalarını basar
    /// (BAY, kullanıcı listede özellikle işaretlemedikçe basılmaz). Arayüzdeki seçim değişmez.
    /// </summary>
    private void BuildQuickButtons(IReadOnlyList<CategoryRef> cats)
    {
        flowQuick.Controls.Clear();
        foreach (var c in cats)
        {
            var b = new Button
            {
                Text = QuickText(c),
                Tag = c,
                AutoSize = true,
                // Kompakt: küçük ekranlı dizüstünde de sol sütun kaydırmasız sığsın.
                MinimumSize = new System.Drawing.Size(60, 30),
                Margin = new Padding(2),
                Padding = new Padding(6, 1, 6, 1),
                FlatStyle = FlatStyle.Flat,
                BackColor = ThGreen,
                Font = new System.Drawing.Font("Segoe UI", 9.5F, System.Drawing.FontStyle.Bold)
            };
            b.Click += quickCategory_Click;
            StyleButton(b); // birincil (yeşil) stil + hover/pressed
            _quickTip.SetToolTip(b, QuickTip(c));
            flowQuick.Controls.Add(b);
        }
        btnPrintAll.Enabled = cats.Count > 0;
        BuildListButtons(cats);
        RelayoutLeft();
    }

    private readonly ToolTip _quickTip = new();

    private string QuickText(CategoryRef c)
    {
        if (!_roundCache.TryGetValue(c.Tnr, out var r)) return $"🖨 {ButtonText(c)} · …";
        return r.Current > 0 ? $"🖨 {ButtonText(c)} · T{r.Current}" : $"{ButtonText(c)} · —";
    }

    private string QuickTip(CategoryRef c)
    {
        if (!_roundCache.TryGetValue(c.Tnr, out var r)) return $"{c.Name} — tur bilgisi alınıyor…";
        var sys = r.System == TournamentSystem.Unknown ? "" : $" • {r.System.DisplayName()}";
        return r.Current > 0
            ? $"{c.Name}{sys} — son eşlenen tur {r.Current}/{r.Max}. Tek tık: bu turun işaretli masalarını yazdır (BAY hariç)."
            : $"{c.Name}{sys} — henüz eşleştirme yok.";
    }

    private void UpdateQuickButton(CategoryRef c)
    {
        var b = flowQuick.Controls.OfType<Button>().FirstOrDefault(x => x.Tag is CategoryRef k && k.Tnr == c.Tnr);
        if (b is null) return;
        b.Text = QuickText(c);
        _quickTip.SetToolTip(b, QuickTip(c));
        UpdateListButtons(c);
        RelayoutLeft(); // yazı uzadıysa satır sayısı değişebilir
    }

    // Kategori değişince: o kategorinin turlarını kur ve son eşlenmiş turu KENDİLİĞİNDEN çek.
    private async void category_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        if (sender is not RadioButton { Checked: true, Tag: CategoryRef c }) return;
        _syncedTournament = null; // kategori değişti → yeniden çekilmeli
        ShowPairings(null);
        if (IsTsfEvent) return; // kayıt önizlemesi kategorinin grubuna gitti; eşleştirme henüz yok
        int current = await UpdateRoundsForCategoryAsync(c.Tnr);
        if (current == 0)
        {
            SetStatus($"“{ButtonText(c)}”: henüz eşleştirme yayımlanmamış.", ok: false);
            return;
        }
        await SyncPairingsAsync(showWarnings: false);
    }

    // Tur değişince o turu kendiliğinden çek (ayrıca "çek" butonuna basmak gerekmesin).
    private async void round_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loading || sender is not RadioButton { Checked: true }) return;
        await SyncPairingsAsync(showWarnings: false);
    }

    /// <summary>
    /// Turnuva tarih ifadesini saklar ve (elle değiştirilmediyse) notasyona basılacak TEK tarihi
    /// varsayılan seçer: bugün turnuva günlerindense bugün, değilse ilk gün.
    /// </summary>
    private void ApplyAutoDate(string? rangeStr)
    {
        _config.Tournament.DateRange = rangeStr;
        if (!_config.Tournament.ManualOverride)
            _config.Tournament.Date = TournamentDates.Format(
                TournamentDates.DefaultPick(TournamentDates.Parse(rangeStr)));
    }

    /// <summary>
    /// Seçili kategorinin turlarını (gerekirse online) bulup tur butonlarını kurar.
    /// Eşlenmiş son turu döndürür (henüz yoksa 0; bilgi alınamadıysa -1).
    /// </summary>
    private async Task<int> UpdateRoundsForCategoryAsync(int catTnr)
    {
        bool busied = false;
        try
        {
            if (!_roundCache.TryGetValue(catTnr, out var rounds))
            {
                SetBusy(true); busied = true;
                var info = await _online.GetEventAsync(catTnr); _ranked[catTnr] = info.RankedRound;
                rounds = (info.MaxRound, info.CurrentRound, info.System);
                _roundCache[catTnr] = rounds;
                var cat = CurrentCategories().FirstOrDefault(c => c.Tnr == catTnr);
                if (cat is not null) UpdateQuickButton(cat);
            }
            BuildRoundButtons(rounds.Max, rounds.Current);
            return rounds.Current;
        }
        catch { return -1; /* tur bilgisi alınamadıysa mevcut butonlar kalsın */ }
        finally { if (busied) SetBusy(false); }
    }

    private void BuildRoundButtons(int maxRound, int currentRound)
    {
        _loading = true;
        flowRounds.Controls.Clear();
        int max = Math.Max(1, Math.Max(maxRound, currentRound));
        for (int r = 1; r <= max; r++)
        {
            var rb = MakeToggle(r.ToString(), r);
            rb.CheckedChanged += round_CheckedChanged;
            flowRounds.Controls.Add(rb);
        }
        // Varsayılan: eşleştirmesi yayımlanmış SON tur (henüz yoksa 1. tur).
        var buttons = flowRounds.Controls.OfType<RadioButton>().ToList();
        if (buttons.Count > 0) buttons[Math.Clamp(currentRound, 1, buttons.Count) - 1].Checked = true;
        _loading = false;
        RelayoutLeft();
    }

    private static RadioButton MakeToggle(string text, object tag) => new()
    {
        Appearance = Appearance.Button,
        AutoSize = true,
        MinimumSize = new System.Drawing.Size(30, 28),
        Padding = new Padding(5, 2, 5, 2),
        Margin = new Padding(2),
        Font = new System.Drawing.Font("Segoe UI", 8.5F),
        Text = text,
        Tag = tag,
        TextAlign = System.Drawing.ContentAlignment.MiddleCenter
    };

    /// <summary>Kategori butonu yazısı; tek gruplu turnuvada kategori adı boşsa "Turnuva".</summary>
    private static string ButtonText(CategoryRef c)
    {
        var s = EventGrouping.ShortCategory(c.Name);
        return string.IsNullOrWhiteSpace(s) ? "Turnuva" : s;
    }

    private CategoryRef? SelectedCategory()
        => flowCategories.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Checked)?.Tag as CategoryRef;

    private int SelectedRound()
    {
        if (rbOnline.Checked)
        {
            var r = flowRounds.Controls.OfType<RadioButton>().FirstOrDefault(x => x.Checked);
            if (r?.Tag is int v) return v;
        }
        return (int)numRound.Value;
    }

    // ================= Senkron =================
    private async void btnSync_Click(object? sender, EventArgs e) => await RefreshAllAsync();

    /// <summary>
    /// F5 / 🔄: önbelleği atıp TÜM kategorilerin tur bilgisini yeniden çeker; kısayollar en güncel
    /// turu hemen gösterir. Kullanıcı en son turdaysa ve yeni tur yayımlandıysa yeni tura geçilir;
    /// başka bir tura bakıyorsa o tur korunur. Ardından seçili tur yeniden çekilir.
    /// </summary>
    private async Task RefreshAllAsync()
    {
        if (IsTsfEvent) { await LoadEventAsync(_eventTnr!.Value); return; } // kayıt formunu yeniden oku
        var cats = CurrentCategories();
        var sel = SelectedCategory();
        if (sel is null || _eventTnr is null) { await SyncPairingsAsync(showWarnings: true); return; }

        int selRound = SelectedRound();
        int prevLatest = _roundCache.TryGetValue(sel.Tnr, out var pr) ? pr.Current : selRound;
        var before = SnapshotRounds(cats); // yeni tur gelen kategoriler vurgulansın
        foreach (var c in cats) _roundCache.Remove(c.Tnr);
        foreach (var c in cats) UpdateQuickButton(c); // "…" — yenileniyor

        SetStatus("Turlar yenileniyor…", ok: true);
        int latest = await UpdateRoundsForCategoryAsync(sel.Tnr); // yeni tur düğmeleri, son tur seçili
        if (latest > 0 && selRound != prevLatest && selRound <= latest)
        {
            // Kullanıcı eski bir tura bakıyordu: o tur kalsın.
            _loading = true;
            var rb = flowRounds.Controls.OfType<RadioButton>().FirstOrDefault(r => r.Tag is int v && v == selRound);
            if (rb is not null) rb.Checked = true;
            _loading = false;
        }
        await SyncPairingsAsync(showWarnings: true);
        await PrefetchCategoryRoundsAsync(cats, _eventTnr.Value);
        _ = SyncRegistrationAsync(_eventTnr.Value);
        FlashChangedRounds(cats, before);
        Log($"🔄 Yenilendi: {string.Join(", ", cats.Select(QuickText).Select(t => t.Replace("🖨 ", "")))}");
    }

    /// <summary>
    /// Seçili kategori+turun eşleştirmelerini çeker. <paramref name="showWarnings"/> false ise
    /// (otomatik çekimde) seçim yoksa sessiz çıkar; true ise (butona basınca) uyarı gösterir.
    /// </summary>
    private async Task SyncPairingsAsync(bool showWarnings)
    {
        int? catTnr = SelectedCategory()?.Tnr ?? _eventTnr;
        if (catTnr is null)
        {
            if (showWarnings)
                MessageBox.Show("Önce turnuva (ve kategori) seçin ya da link/no ile getirin.",
                    "Seçim yok", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (catTnr < 0) { SetStatus("Turnuva henüz chess-results'ta yok: eşleştirme yayımlanınca listeden seçin.", ok: false); return; }
        int round = SelectedRound();
        int seq = ++_syncSeq;
        SetBusy(true);
        SetStatus($"{round}. tur eşleştirmeleri çekiliyor…", ok: true);
        try
        {
            var t = await _online.GetPairingsAsync(catTnr.Value, round, SystemOf(catTnr.Value));
            if (seq != _syncSeq) return; // bu arada başka kategori/tur seçildi: eski sonucu at
            // Sistem sayfadan anlaşıldıysa (ör. ayrıntıları gizli takım turnuvası) hatırla.
            if (t.System != TournamentSystem.Unknown && SystemOf(catTnr.Value) != t.System)
            {
                var old = _roundCache.TryGetValue(catTnr.Value, out var r) ? r : (ChessResultsParser.UnknownMaxRound, 0, t.System);
                _roundCache[catTnr.Value] = (old.Item1, old.Item2, t.System);
            }
            t = StampCategory(t, EventGrouping.ShortCategory(SelectedCategory()?.Name ?? ""));
            if (t.Pairings.Count == 0)
            {
                SetStatus($"{round}. tur için eşleştirme yok (henüz oluşturulmamış olabilir).", ok: false);
                _syncedTournament = null; ShowPairings(null); return;
            }
            _syncedTournament = t;
            ShowPairings(t);
            if (!_config.Tournament.ManualOverride)
            {
                _config.Tournament.Name = EventGrouping.BaseName(t.Name); // kategori ekini at
                UpdateInfoLabel();
            }

            var cat = SelectedCategory();
            _config.Online.SelectedCategoryTnr = catTnr;
            _config.Online.SelectedCategoryName = cat?.Name;
            _config.Online.SelectedTnr ??= _eventTnr;
            _config.Tournament.LastRound = round;
            _config.Save(_configPath);

            int bye = t.Pairings.Count(p => p.IsBye);
            SetStatus($"{ButtonText(cat ?? new CategoryRef(0, "", true))} • {round}. tur: {t.Pairings.Count} masa" +
                      (bye > 0 ? $" ({bye} BAY)" : "") + ". Listeyi kontrol edip Yazdır'a basın.", ok: true);
            Log($"✓ Çekildi: {t.Name} — {round}. tur, {t.Pairings.Count} masa.");
        }
        catch (Exception ex)
        {
            if (seq != _syncSeq) return;
            if (showWarnings) Fail("Eşleştirme çekilemedi", FriendlyNet(ex));
            else { SetStatus($"{round}. tur eşleştirmeleri çekilemedi — 🔄 (F5) ile tekrar deneyin.", ok: false); Log("Uyarı: " + FriendlyNet(ex)); }
            _syncedTournament = null;
            ShowPairings(null);
        }
        finally { if (seq == _syncSeq) SetBusy(false); }
    }

    // ================= Hızlı baskı (kısayollar) =================
    /// <summary>
    /// Kategori kısayolu: o kategorinin EN SON eşlenmiş turunu tek tıkla basar. Masalar listede o
    /// kategori/tur için yapılmış seçime uyar; seçim yapılmadıysa BAY hariç hepsi basılır.
    /// Ekrandaki kategori/tur/liste seçimi değişmez.
    /// </summary>
    private async void quickCategory_Click(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: CategoryRef cat }) return;
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            if (!_roundCache.TryGetValue(cat.Tnr, out var r))
            {
                var info = await _online.GetEventAsync(cat.Tnr); _ranked[cat.Tnr] = info.RankedRound;
                r = (info.MaxRound, info.CurrentRound, info.System);
                _roundCache[cat.Tnr] = r;
                UpdateQuickButton(cat);
            }
            if (r.Current <= 0) { SetStatus($"{ButtonText(cat)}: henüz eşleştirme yayımlanmamış.", ok: false); return; }

            var t = await FetchCategoryAsync(cat, r.Current);
            if (t.Pairings.Count == 0) { SetStatus($"{ButtonText(cat)} • {r.Current}. tur: basılacak masa yok.", ok: false); return; }
            SetStatus($"{ButtonText(cat)} • {r.Current}. tur: {t.Pairings.Count} masa yazdırılıyor…", ok: true);
            PrintTournament(t, _config.Layout.CopiesPerBoard);
        }
        catch (UserMessageException ex) { Fail("Kısayol", ex.Message); }
        catch (Exception ex) { Fail("Kısayol baskısı başarısız", FriendlyNet(ex)); }
        finally { SetBusy(false); }
    }

    private async void btnPrintAll_Click(object? sender, EventArgs e)
    {
        if (BlockedForTsfEvent("Notasyon")) return;
        var cats = CurrentCategories();
        if (cats.Count == 0) { SetStatus("Önce bir turnuva yükleyin.", ok: false); return; }
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            var all = new List<Pairing>();
            Tournament? first = null;
            int okCats = 0;
            foreach (var cat in cats)
            {
                SetStatus($"Çekiliyor: {cat.Name}…", ok: true);
                Tournament t;
                try { t = await FetchCategoryAsync(cat); }
                catch (InvalidOperationException) // bu kategoride o tur henüz eşlenmemiş
                {
                    Log($"• {cat.Name}: eşleştirme yok, atlandı.");
                    continue;
                }
                if (t.Pairings.Count == 0) { Log($"• {cat.Name}: masa yok, atlandı."); continue; }
                first ??= t;
                all.AddRange(t.Pairings);
                okCats++;
            }
            if (all.Count == 0 || first is null) { SetStatus("Hiçbir kategoride yazdırılacak masa bulunamadı.", ok: false); return; }
            var combined = first with { Pairings = all };
            PrintTournament(combined, _config.Layout.CopiesPerBoard);
            Log($"★ Tüm kategoriler: {okCats} kategori, {all.Count} masa.");
        }
        catch (UserMessageException ex) { Fail("Tüm kategoriler", ex.Message); }
        catch (Exception ex) { Fail("Tüm kategoriler baskısı başarısız", FriendlyNet(ex)); }
        finally { SetBusy(false); }
    }

    private List<CategoryRef> CurrentCategories() =>
        flowCategories.Controls.OfType<RadioButton>().Select(r => r.Tag).OfType<CategoryRef>().ToList();

    /// <summary>
    /// Bir kategorinin turunu (verilmezse seçili tur, kategori o tura gelmediyse son tur) çekip
    /// normalize + kullanıcı seçimi (yoksa varsayılan: BAY ve hariç tutulanlar basılmaz) + meta uygular.
    /// </summary>
    private async Task<Tournament> FetchCategoryAsync(CategoryRef cat, int? forceRound = null)
    {
        int round = forceRound ?? await ResolveRoundForCategoryAsync(cat.Tnr);
        var raw = await _online.GetPairingsAsync(cat.Tnr, round, SystemOf(cat.Tnr));
        var t = Validation.Normalize(raw, keepByeSheets: true);
        t = StampCategory(t, EventGrouping.ShortCategory(cat.Name)); // her kağıt kendi kategorisini taşısın
        // Toplu baskıda kategoriler farklı turda olabilir → her kağıt kendi tur no'sunu taşısın.
        t = t with { Pairings = t.Pairings.Select(p => p with { Round = round }).ToList() };

        // Listede bu kategori/tur için yapılmış seçim varsa ona, yoksa varsayılan kurala uy.
        var key = (cat.Tnr, round);
        t = t with { Pairings = t.Pairings.Where(p => IsChecked(key, p)).ToList() };

        return ApplyConfigMeta(t);
    }

    /// <summary>Tüm eşleştirmelere kategori kısa adını işler (notasyonda "Kategori" alanı için).</summary>
    private static Tournament StampCategory(Tournament t, string? catShort)
    {
        if (string.IsNullOrWhiteSpace(catShort)) return t;
        return t with { Pairings = t.Pairings.Select(p => p with { Category = catShort }).ToList() };
    }

    /// <summary>
    /// Kategorinin yazdırılacak turu: kullanıcının seçtiği tur; kategori henüz o tura
    /// gelmediyse (eşleştirmesi yoksa) o kategorinin eşlenmiş son turu.
    /// </summary>
    private async Task<int> ResolveRoundForCategoryAsync(int catTnr)
    {
        int sel = SelectedRound();
        if (!_roundCache.TryGetValue(catTnr, out var rounds))
        {
            try
            {
                var info = await _online.GetEventAsync(catTnr); _ranked[catTnr] = info.RankedRound;
                rounds = (info.MaxRound, info.CurrentRound, info.System);
                _roundCache[catTnr] = rounds;
            }
            catch { return Math.Max(1, sel); }
        }
        int last = rounds.Current > 0 ? rounds.Current : rounds.Max;
        return Math.Max(1, Math.Min(sel, Math.Max(1, last)));
    }

    // ================= Dosya =================
    private void btnBrowse_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Title = "Eşleştirme dosyası seç", Filter = ParserFactory.FileDialogFilter };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        txtFile.Text = dlg.FileName;
        TryPreviewFile(dlg.FileName);
    }

    private void TryPreviewFile(string file)
    {
        try
        {
            UpdateConfigFromUi();
            var t = ParserFactory.Parse(file, _config);
            if (t.RoundNo >= numRound.Minimum && t.RoundNo <= numRound.Maximum) numRound.Value = t.RoundNo;
            _fileTournament = t;
            ShowPairings(t);
            if (!_config.Tournament.ManualOverride && !string.IsNullOrWhiteSpace(t.Name))
            {
                _config.Tournament.Name = EventGrouping.BaseName(t.Name);
                if (!string.IsNullOrWhiteSpace(t.Date)) ApplyAutoDate(t.Date); // dosyadaki tarih
                UpdateInfoLabel();
            }
            int bye = t.Pairings.Count(p => p.IsBye);
            SetStatus($"{t.Pairings.Count} masa bulundu" + (bye > 0 ? $" ({bye} BAY)" : "") + ".", ok: true);
        }
        catch (Exception ex)
        {
            _fileTournament = null;
            ShowPairings(null);
            SetStatus("Dosya okunamadı.", ok: false);
            Log("✗ " + ex.Message);
            MessageBox.Show(this, ex.Message, "Dosya okunamadı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // ================= Yazdır =================
    private void btnPrint_Click(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            UpdateConfigFromUi();
            var baseT = BuildTournament();
            PrintTournament(baseT, _config.Layout.CopiesPerBoard);
            // Baskıdan sonra kategori/tur/masa seçimleri OLDUĞU GİBİ kalır (sıfırlanmaz, ilerlemez).
            _config.Save(_configPath);
        }
        catch (UserMessageException ex) { Fail("Yazdırılamadı", ex.Message); }
        catch (ParseException ex) { Fail("Veri okunamadı", ex.Message); }
        catch (Exception ex) { Fail("Yazdırma hatası", ex.Message); }
        finally { SetBusy(false); }
    }

    // ================= PDF =================
    private async void btnPdf_Click(object? sender, EventArgs e)
    {
        try
        {
            UpdateConfigFromUi();
            var t = ExpandCopies(BuildTournament(), _config.Layout.CopiesPerBoard);
            SetBusy(true);
            SetStatus("PDF hazırlanıyor…", ok: true);
            var path = await Task.Run(() => RenderToPdf(t));
            OpenWithShell(path);
            SetStatus($"PDF açıldı: {Path.GetFileName(path)} ({t.Pairings.Count} kağıt)", ok: true);
            Log($"📄 PDF: {path}");
        }
        catch (UserMessageException ex) { Fail("PDF oluşturulamadı", ex.Message); }
        catch (Exception ex) { Fail("PDF hatası", ex.Message); }
        finally { SetBusy(false); }
    }

    // ================= Basılacak masalar listesi =================
    /// <summary>
    /// Sağdaki listeyi doldurur. Varsayılan işaret: "Hariç Tut"ta seçilmemiş ve (BAY ise) BAY kağıdı
    /// açıksa basılır. Kullanıcı işaretleri bu tur için değiştirebilir.
    /// </summary>
    private void ShowPairings(Tournament? t)
    {
        // Kayıttaki turnuva (eşleştirme yok): alan TSF kayıt listesini gösterir.
        bool regView = t is null && rbOnline.Checked && IsTsfEvent;
        SetRegistrationView(regView);
        if (regView) { ShowRegistrations(); return; }
        _gridTournament = t is null ? null : Validation.Normalize(t, keepByeSheets: true);
        var catTnr = rbOnline.Checked ? SelectedCategory()?.Tnr ?? _eventTnr : null;
        _gridKey = _gridTournament is not null && catTnr is int ct ? (ct, _gridTournament.RoundNo) : null;
        _restoringGrid = true;
        dgvPairings.SuspendLayout();
        dgvPairings.Rows.Clear();
        if (_gridTournament is not null)
        {
            // İller (kayıt listesi / elle / öğrenilen) basılmadan önce listede görünsün; ağ isteği yok.
            var withProv = WithProvinces(_gridTournament).Pairings;
            for (int k = 0; k < _gridTournament.Pairings.Count; k++)
            {
                var p = _gridTournament.Pairings[k];
                var pv = withProv[k];
                bool print = _gridKey is { } key ? IsChecked(key, p) : DefaultChecked(p);
                int i = dgvPairings.Rows.Add(print, p.BoardText, WithTeam(p.White.Name, p.WhiteTeam),
                    p.White.Rating?.ToString() ?? "", pv.White.Province ?? "",
                    p.IsBye ? "BAY" : WithTeam(p.Black!.Name, p.BlackTeam), p.Black?.Rating?.ToString() ?? "",
                    pv.Black?.Province ?? "");
                var row = dgvPairings.Rows[i];
                row.Tag = p;
                if (p.IsBye)
                {
                    row.DefaultCellStyle.ForeColor = System.Drawing.Color.Gray;
                    row.DefaultCellStyle.Font = new System.Drawing.Font(dgvPairings.Font, System.Drawing.FontStyle.Italic);
                }
            }
        }
        // İl sütunları yalnız il bilgisi varken görünür (kayıt listesi alınmamışsa boş sütun kalabalık yapmasın).
        bool anyProvince = dgvPairings.Rows.Cast<DataGridViewRow>()
            .Any(r => !string.IsNullOrEmpty(r.Cells["colWIl"].Value as string) || !string.IsNullOrEmpty(r.Cells["colBIl"].Value as string));
        dgvPairings.Columns["colWIl"]!.Visible = dgvPairings.Columns["colBIl"]!.Visible = anyProvince;
        dgvPairings.ResumeLayout();
        dgvPairings.ClearSelection();
        _restoringGrid = false;

        var cat = rbOnline.Checked ? SelectedCategory() : null;
        var sys = _gridTournament?.System is { } s0 && s0 != TournamentSystem.Unknown ? " • " + s0.DisplayName() : "";
        grpPairings.Text = _gridTournament is null
            ? "3) Basılacak masalar"
            : $"3) Basılacak masalar — " +
              (cat is null ? "" : ButtonText(cat) + " • ") +
              (rbOnline.Checked ? $"{_gridTournament.RoundNo}. tur" : $"{numRound.Value}. tur (dosya)") + sys;
        UpdateSelectionSummary();
    }

    private bool _restoringGrid; // liste doldurulurken seçim kaydı yazılmasın

    private static string WithTeam(string name, string? team)
        => string.IsNullOrWhiteSpace(team) ? name : $"{name}  ({team})";

    /// <summary>
    /// Varsayılan işaret: BAY masası HİÇBİR ZAMAN varsayılan olarak basılmaz (kullanıcı listede
    /// işaretlerse basılır); diğer masalar "Hariç Tut"ta değilse basılır.
    /// </summary>
    private bool DefaultChecked(Pairing p)
        => !p.IsBye && !BoardRange.Parse(_config.Layout.ExcludedBoards).Contains(p.Board);

    /// <summary>Bu kategori/turda masa basılacak mı: kullanıcı seçimi varsa o, yoksa varsayılan.</summary>
    private bool IsChecked((int Cat, int Round) key, Pairing p)
        => _unchecked.TryGetValue(key, out var off) ? !off.Contains(p.Board) : DefaultChecked(p);

    private HashSet<int> SelectedBoards()
    {
        var set = new HashSet<int>();
        foreach (DataGridViewRow r in dgvPairings.Rows)
            if (r.Cells[0].Value is true && r.Tag is Pairing p) set.Add(p.Board);
        return set;
    }

    private void UpdateSelectionSummary()
    {
        int total = dgvPairings.Rows.Count;
        int n = SelectedBoards().Count;
        // Seçimi bu kategori/tur için sakla (başka kategoriye geçip dönünce ve toplu baskıda kullanılır).
        if (!_restoringGrid && _gridKey is { } key && total > 0)
        {
            var off = new HashSet<int>();
            foreach (DataGridViewRow r in dgvPairings.Rows)
                if (r.Cells[0].Value is not true && r.Tag is Pairing p) off.Add(p.Board);
            _unchecked[key] = off;
            _config.Online.Selections[$"{key.Cat}:{key.Round}"] = off.OrderBy(x => x).ToList();
        }
        int copies = (int)numCopies.Value;
        int sheets = n * copies;
        if (total == 0)
        {
            lblSelection.Text = rbOnline.Checked ? "Eşleştirme yok — turnuva/kategori/tur seçin." : "Dosya seçin.";
            btnPrint.Text = "🖨  Yazdır";
        }
        else
        {
            lblSelection.Text = $"{n} / {total} masa işaretli • {sheets} kağıt";
            btnPrint.Text = n > 0 ? $"🖨  Yazdır — {sheets} kağıt" + (Overlay.PrintRouter.IsSilent ? " (doğrudan)" : "") : "🖨  Yazdır (masa seçilmedi)";
        }
    }

    private void SetAllChecks(bool value)
    {
        foreach (DataGridViewRow r in dgvPairings.Rows) r.Cells[0].Value = value;
        UpdateSelectionSummary();
    }

    private void btnSelectAll_Click(object? sender, EventArgs e) => SetAllChecks(true);
    private void btnSelectNone_Click(object? sender, EventArgs e) => SetAllChecks(false);

    /// <summary>Boşluk: seçili satırların işaretini tersine çevirir (birden çok satır seçilebilir).</summary>
    private void dgvPairings_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Space || dgvPairings.SelectedRows.Count <= 1) return;
        bool target = dgvPairings.SelectedRows.Cast<DataGridViewRow>().Any(r => r.Cells[0].Value is not true);
        foreach (DataGridViewRow r in dgvPairings.SelectedRows) r.Cells[0].Value = target;
        e.Handled = true;
        UpdateSelectionSummary();
    }

    private void numCopies_ValueChanged(object? sender, EventArgs e)
    {
        if (_loading) return;
        _config.Layout.CopiesPerBoard = (int)numCopies.Value;
        UpdateSelectionSummary();
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode == Keys.P && btnPrint.Enabled) { e.SuppressKeyPress = true; btnPrint.PerformClick(); }
        else if (e.KeyCode == Keys.F5 && rbOnline.Checked && btnSync.Enabled) { e.SuppressKeyPress = true; btnSync.PerformClick(); }
        else if (e.Control && e.KeyCode is Keys.E or Keys.R && !_busy && _gridTournament is not null)
        {
            e.SuppressKeyPress = true;
            _ = PrintGridListAsync(results: e.KeyCode == Keys.R); // Ctrl+E eşleştirme, Ctrl+R sonuç listesi
        }
        else if (e.Control && e.KeyCode == Keys.H && !_busy && rbOnline.Checked && _eventTnr is not null)
        {
            e.SuppressKeyPress = true;
            OpenArbiterSheets(); // Ctrl+H hakem eşleştirme kağıtları
        }
    }

    /// <summary>Önizleme açar; true = kullanıcı yazıcıya gönderdi.</summary>
    private bool PrintTournament(Tournament baseT, int copies)
    {
        var t = ExpandCopies(WithProvinces(baseT), copies);
        if (!_config.Overlay.IsConfigured)
            throw new UserMessageException("Önce ⚙ Ayarlar'dan hazır kağıt şablonu tasarlayın.");
        SetStatus("Önizleme açılıyor…", ok: true);
        var printer = new Overlay.SheetPrinter(p => _config.TemplateFor(p.System), t, _config.Layout.PageSize,
            _config.Layout.PrintOffsetXmm, _config.Layout.PrintOffsetYmm);
        bool printed = printer.PrintWithPreview(this);
        if (printed)
        {
            SetStatus($"✓ {t.Pairings.Count} kağıt yazıcıya gönderildi.", ok: true);
            Log($"🖨 Basıldı: {baseT.Name} — {DescribeRounds(baseT)}, {baseT.Pairings.Count} masa × {copies} = {t.Pairings.Count} kağıt.");
        }
        else SetStatus("Önizleme kapatıldı; baskı yapılmadı.", ok: true);
        return printed;
    }

    private static string DescribeRounds(Tournament t)
    {
        var rounds = t.Pairings.Select(p => p.Round ?? t.RoundNo).Distinct().OrderBy(r => r).ToList();
        return rounds.Count == 1 ? $"{rounds[0]}. tur" : $"{string.Join("/", rounds)}. turlar";
    }

    // ================= Ortak üretim =================
    /// <summary>Sağdaki listede İŞARETLİ masalardan basılacak turnuvayı kurar.</summary>
    private Tournament BuildTournament()
    {
        var src = _gridTournament ?? throw new UserMessageException(rbOnline.Checked
            ? "Önce turnuva, kategori ve tur seçin; eşleştirmeler sağdaki listede görünmeli."
            : "Lütfen önce geçerli bir eşleştirme dosyası seçin.");
        if (!rbOnline.Checked) src = src with { RoundNo = (int)numRound.Value };

        var t = ApplyConfigMeta(src);

        var validation = Validation.Validate(t);
        if (!validation.IsValid) throw new UserMessageException(string.Join("\n", validation.Errors));

        var boards = SelectedBoards();
        t = t with { Pairings = t.Pairings.Where(p => boards.Contains(p.Board)).ToList() };

        if (t.Pairings.Count == 0)
            throw new UserMessageException("Basılacak masa işaretlenmedi. Sağdaki listeden masaları işaretleyin.");
        return t;
    }

    /// <summary>Config'teki turnuva adı/tarih bilgisini (varsa) kaynağa uygular.
    /// Turnuva adından kategori eki ("… A Kategorisi") HER ZAMAN atılır.</summary>
    private Tournament ApplyConfigMeta(Tournament src) => src with
    {
        Name = EventGrouping.BaseName(string.IsNullOrWhiteSpace(_config.Tournament.Name) ? src.Name : _config.Tournament.Name),
        // Tek tarih (config'te seçilen/varsayılan); yoksa kaynaktan tek güne indir.
        Date = !string.IsNullOrWhiteSpace(_config.Tournament.Date)
            ? _config.Tournament.Date
            : TournamentDates.Format(TournamentDates.DefaultPick(TournamentDates.Parse(src.Date))),
    };

    /// <summary>Her masayı <paramref name="copies"/> kez tekrarlar (iki oyuncuya da aynı notasyon).</summary>
    private static Tournament ExpandCopies(Tournament t, int copies)
    {
        copies = Math.Max(1, copies);
        if (copies == 1) return t;
        var expanded = new List<Pairing>(t.Pairings.Count * copies);
        foreach (var p in t.Pairings)
            for (int i = 0; i < copies; i++) expanded.Add(p);
        return t with { Pairings = expanded };
    }

    private string RenderToPdf(Tournament t, string suffix = "")
    {
        if (!_config.Overlay.IsConfigured)
            throw new UserMessageException("Önce ⚙ Ayarlar'dan hazır kağıt şablonu tasarlayın.");
        var path = Path.Combine(_outputDir, BuildFileName(t, suffix + "_overlay"));
        Overlay.OverlayPdfRenderer.Render(p => _config.TemplateFor(p.System), WithProvinces(t), path, _config.Layout.PageSize);
        return path;
    }

    // ================= Ayarlar =================
    private void btnSettings_Click(object? sender, EventArgs e)
    {
        UpdateConfigFromUi();
        using var dlg = new SettingsForm(_config, owner => OpenRegistration(owner), owner => OpenUpdates(null, owner));
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            UpdateProvinceLogo();
            Overlay.PrintRouter.Config = _config.Printing;
            UpdatePrintButtonsForSilent();
            if (HasDefaultProvince && !Equals(cboProvince.SelectedItem, _config.Online.Province))
                cboProvince.SelectedItem = _config.Online.Province; // Ayarlar'da değişen varsayılan il
            _config.Save(_configPath);
            UpdateInfoLabel();
            if (!_config.Overlay.IsConfigured)
                SetStatus("Hazır kağıt şablonu henüz tanımlı değil.", ok: false);
            else SetStatus("Ayarlar kaydedildi.", ok: true);
        }
    }

    // ================= Kapat =================
    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        UpdateConfigFromUi();
        _config.Save(_configPath);
        _online.Dispose();
    }

    // ================= Yardımcılar =================
    private static void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Açılamadı: " + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static string BuildFileName(Tournament t, string suffix = "")
    {
        var slug = Slugify(t.Name);
        if (slug.Length == 0) slug = "turnuva";
        return $"{slug}_tur{t.RoundNo}_notasyon{suffix}.pdf";
    }

    private static string Slugify(string s)
    {
        s = s.Trim().ToLowerInvariant();
        var map = new Dictionary<char, char>
        { ['ç'] = 'c', ['ğ'] = 'g', ['ı'] = 'i', ['ö'] = 'o', ['ş'] = 's', ['ü'] = 'u', ['â'] = 'a', ['î'] = 'i' };
        var sb = new StringBuilder();
        foreach (var ch in s)
        {
            var c = map.TryGetValue(ch, out var r) ? r : ch;
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        return sb.ToString().Trim('_');
    }

    private static string FriendlyNet(Exception ex) => ex switch
    {
        // Sunucu yanıt verdi ama hata döndü: bağlantı değil sitenin sorunu
        HttpRequestException { StatusCode: { } code } => $"Site hata verdi (HTTP {(int)code}); site geçici olarak sorunlu olabilir, biraz sonra tekrar deneyin.",
        HttpRequestException => "İnternet bağlantısı kurulamadı ya da siteye ulaşılamıyor. Bağlantınızı kontrol edin.",
        TaskCanceledException => "İstek zaman aşımına uğradı. Tekrar deneyin.",
        _ => ex.Message
    };

    private static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;

    private void SetBusy(bool busy)
    {
        foreach (Control c in new Control[] { btnPrint, btnPdf, btnBrowse, btnSearch, btnSync, cboTournament, cboProvince, flowQuick })
            c.Enabled = !busy;
        btnPrintAll.Enabled = !busy && flowQuick.Controls.Count > 0;
        if (_grpLists is not null) _grpLists.Enabled = !busy;
        if (_btnGridList is not null) _btnGridList.Enabled = !busy;
        _busy = busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void SetStatus(string text, bool ok)
    {
        lblStatus.Text = text;
        lblStatus.ForeColor = ok ? System.Drawing.Color.FromArgb(95, 122, 70) : System.Drawing.Color.FromArgb(160, 60, 60);
    }

    private void Log(string line)
    {
        txtLog.AppendText(line + Environment.NewLine);
        OnLogged(line);
    }

    private readonly Dictionary<string, DateTime> _logOnce = new();

    /// <summary>Aynı satır son 15 dakikada yazıldıysa tekrar yazmaz (her yenilemede yığılan uyarılar için).</summary>
    private void LogOnce(string line)
    {
        if (_logOnce.TryGetValue(line, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(15)) return;
        _logOnce[line] = DateTime.Now;
        Log(line);
    }

    private void Fail(string title, string message)
    {
        SetStatus(title, ok: false);
        Log("✗ " + message);
        MessageBox.Show(message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private sealed class Item<T>
    {
        public string Text { get; }
        public T Value { get; }
        public Item(string text, T value) { Text = text; Value = value; }
        public override string ToString() => Text;
    }

    private sealed class UserMessageException : Exception
    {
        public UserMessageException(string message) : base(message) { }
    }
}
