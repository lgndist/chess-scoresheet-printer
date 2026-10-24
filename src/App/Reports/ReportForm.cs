using System.Diagnostics;
using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>Rapor penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record ReportContext(
    ChessResultsService Service,
    int EventTnr,
    string EventName,
    IReadOnlyList<CategoryRef> Categories,
    string? Province,
    Func<int, Task<(int MaxRound, TournamentSystem System)>> CategoryInfo,
    AppConfig Config,
    Action SaveConfig,
    string BaseDir,
    string OutputDir,
    Func<CategoryRef, Task<CategoryStats>>? CategoryStats = null);

/// <summary>
/// Turnuva yönergesi / raporu / teknik toplantı tutanağı. Şablon (gömülü ya da kullanıcının .docx'i)
/// analiz edilir; bilinenler chess-results'tan, ilin TSF sitesindeki yönergeden ve bu turnuva için
/// daha önce girilenlerden doldurulur. Tüm alanlar tek tabloda toplu düzenlenir. Çıktı: PDF (birincil),
/// yazıcı ya da Word belgesi; belgenin biçimi, tabloları ve kilitli alanları korunur.
/// </summary>
public sealed class ReportForm : Form
{
    public const string BuiltInResource = "NotasyonOtomasyonu.App.turnuva_yonergesi_sablon.docx";
    public const string TutanakResource = "NotasyonOtomasyonu.App.teknik_toplanti_tutanagi_sablon.docx";
    public const string SonRaporResource = "NotasyonOtomasyonu.App.turnuva_sonu_raporu_sablon.docx";

    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);
    private static readonly Color MissingBg = Color.FromArgb(253, 236, 234);
    private static readonly Color SuggestedBg = Color.FromArgb(255, 248, 225);
    private const string ManualOrigin = "elle girildi";

    private readonly ReportContext _ctx;
    private readonly string _reportsDir;

    // chess-results verisi bir kez çekilir; şablon/yönerge değişince yalnız taslak yeniden kurulur.
    private IReadOnlyDictionary<string, string>? _info;
    private List<(int Round, DateTime? Date, string Time)> _schedule = new();
    private List<(CategoryRef Cat, int Max, TournamentSystem Sys)> _cats = new();

    // TSF il sitesindeki yönerge
    private Dictionary<string, string>? _yonerge;
    private bool _yonergeSame;

    private ReportDraft? _draft;
    private EndReportData? _endReport;   // turnuva sonu raporu: final sıralamalarından (bir kez çekilir)
    private byte[]? _templateBytes;
    private bool _busy, _filling;

    private readonly ComboBox _cboTemplate = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Button _btnUpload = new() { Text = "📂 Kendi şablonumu yükle…", AutoSize = true, Height = 30 };
    private readonly Button _btnRemove = new() { Text = "Kaldır", AutoSize = true, Height = 30 };
    private readonly LinkLabel _lnkExport = new() { Text = "Seçili hazır şablonu Word belgesi olarak al", AutoSize = true };

    private readonly ComboBox _cboYonerge = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 460 };
    private readonly Button _btnYonRefresh = new() { Text = "↻", Width = 34, Height = 30 };
    private readonly Button _btnYonOpen = new() { Text = "Aç", AutoSize = true, Height = 30 };
    private readonly Button _btnYonUse = new() { Text = "Şablon olarak kullan", AutoSize = true, Height = 30 };
    private readonly Label _lblYon = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray };

    private readonly ComboBox _cboTempo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly ComboBox _cboPreset = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 360 };
    private readonly Button _btnTempo = new() { Text = "Düşünme süresine yaz", AutoSize = true, Height = 28 };

    private readonly DataGridView _gridChecks = MakeGrid();
    private readonly Button _btnSuggest = new() { Text = "⏱ Programı TSF'ye göre öner", AutoSize = true, Height = 28 };
    private readonly Button _btnSuggest2 = new() { Text = "⏱ Programı öner", AutoSize = true, Height = 28 };
    private readonly LinkLabel _lnkProcedure = new() { AutoSize = true };
    private readonly Label _lblProgramHint = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Dictionary<int, CategoryStats> _stats = new();   // kategori tnr → sporcu sayısı, rating, tempo
    private List<RuleCheck> _checks = new();
    private readonly DataGridView _gridFields = MakeGrid();
    private readonly DataGridView _gridCats = MakeGrid();
    private readonly DataGridView _gridProgram = MakeGrid();
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private readonly Button _btnNext = new() { Text = "▶ Sonraki eksik", AutoSize = true, Height = 40 };
    private readonly Button _btnDocx = new() { Text = "Word (.docx) kaydet", AutoSize = true, Height = 40 };
    private readonly Button _btnPrint = new() { Text = PrintRouter.IsSilent ? "🖨 Yazdır (doğrudan)" : "👁 Önizle ve Yazdır", Width = 170, Height = 40 };
    private readonly Button _btnPdf = new() { Text = "📄 PDF Olarak Kaydet", Width = 200, Height = 40, BackColor = Green, ForeColor = Color.White };
    private readonly Button _btnEdit = new() { Text = "✏ Önizle ve Düzenle", Width = 180, Height = 40 };

    public ReportForm(ReportContext ctx)
    {
        _ctx = ctx;
        _reportsDir = Path.Combine(ctx.BaseDir, "reports");

        Text = "Yönerge / Rapor / Tutanak — " + EventGrouping.BaseName(ctx.EventName);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1120, 780);
        MinimumSize = new Size(900, 600);
        this.FitToScreen(); // küçük ekranda / yüksek ölçekte taşmasın
        BackColor = Color.FromArgb(250, 250, 247);

        BuildLayout();
        LoadTemplateList();
        foreach (var t in Enum.GetValues<Tempo>()) _cboTempo.Items.Add(new Choice<Tempo>(t.DisplayName(), t));

        _cboTemplate.SelectedIndexChanged += async (_, _) => { if (!_busy) await RebuildAsync(); };
        _btnUpload.Click += async (_, _) => await UploadTemplateAsync(null);
        _btnRemove.Click += async (_, _) => await RemoveTemplateAsync();
        _lnkExport.LinkClicked += (_, _) => ExportBuiltIn();
        _cboYonerge.SelectedIndexChanged += async (_, _) => { if (!_filling) await LoadSelectedYonergeAsync(); };
        _btnYonRefresh.Click += async (_, _) => await LoadYonergelerAsync();
        _btnYonOpen.Click += (_, _) => OpenYonerge();
        _btnYonUse.Click += async (_, _) => await UseYonergeAsTemplateAsync();
        _cboTempo.SelectedIndexChanged += (_, _) => FillPresets();
        _btnTempo.Click += (_, _) => ApplyTempo();
        _btnNext.Click += (_, _) => GoToNextMissing();
        _btnSuggest.Click += (_, _) => SuggestProgram();
        _btnSuggest2.Click += (_, _) => SuggestProgram();
        _lnkProcedure.LinkClicked += (_, _) => OpenWithShell(TsfRules.ProcedureUrl);
        _gridProgram.CellEndEdit += (_, _) => { PullGrids(); RunChecks(); };
        _gridProgram.UserDeletedRow += (_, _) => { PullGrids(); RunChecks(); };
        _btnPdf.Click += async (_, _) => await SavePdfAsync();
        _btnPrint.Click += async (_, _) => await PreviewAsync();
        _btnEdit.Click += (_, _) => OpenEditor();
        _btnDocx.Click += (_, _) => SaveDocx();
        Shown += async (_, _) =>
        {
            var yon = LoadYonergelerAsync();      // TSF sitesi arka planda
            await RebuildAsync();
            await LoadPlayerCountsAsync();         // EK-C kontrolü için kategori sporcu sayıları
            await yon;
        };
    }

    private sealed record Choice<T>(string Label, T Value) { public override string ToString() => Label; }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(12, 10, 12, 2), WrapContents = true };
        top.Controls.Add(Caption("Şablon:"));
        top.Controls.AddRange(new Control[] { _cboTemplate, _btnUpload, _btnRemove, _lnkExport });
        _cboTemplate.Margin = new Padding(0, 3, 8, 0);
        _lnkExport.Margin = new Padding(6, 8, 0, 0);
        top.SetFlowBreak(_lnkExport, true);

        top.Controls.Add(Caption("TSF yönergesi:"));
        top.Controls.AddRange(new Control[] { _cboYonerge, _btnYonRefresh, _btnYonOpen, _btnYonUse });
        _cboYonerge.Margin = new Padding(0, 3, 6, 0);
        top.SetFlowBreak(_btnYonUse, true);
        _lblYon.Margin = new Padding(0, 2, 0, 0);
        top.Controls.Add(_lblYon);
        top.SetFlowBreak(_lblYon, true);
        _lblStatus.Margin = new Padding(0, 4, 0, 0);
        top.Controls.Add(_lblStatus);

        // Bilgiler sekmesi: tempo/süre önerici + toplu düzenleme tablosu
        var tempoBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 6, 4, 4), WrapContents = false };
        tempoBar.Controls.Add(Caption("Tempo:"));
        tempoBar.Controls.Add(_cboTempo);
        tempoBar.Controls.Add(Caption("  Resmi süre:"));
        tempoBar.Controls.Add(_cboPreset);
        tempoBar.Controls.Add(_btnTempo);
        _cboTempo.Margin = _cboPreset.Margin = new Padding(0, 2, 6, 0);
        var legend = new Label
        {
            Dock = DockStyle.Top, Height = 22, Padding = new Padding(6, 3, 0, 0), ForeColor = Color.DimGray,
            Text = "✓ chess-results    • önerilen (Kaynak sütununa bakın)    ✎ eksik — tüm alanları bu tabloda düzenleyin, Enter ile alttakine geçin"
        };
        var fieldsPage = new TabPage("Bilgiler") { Padding = new Padding(6), BackColor = Color.White };
        _gridFields.Dock = DockStyle.Fill;
        fieldsPage.Controls.Add(_gridFields);
        fieldsPage.Controls.Add(legend);
        fieldsPage.Controls.Add(tempoBar);
        _tabs.TabPages.Add(fieldsPage);
        _tabs.TabPages.Add(Page("Kategoriler", _gridCats));
        var programPage = Page("Program", _gridProgram);
        var programBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 6, 4, 4), WrapContents = false };
        programBar.Controls.Add(_btnSuggest);
        _lblProgramHint.Margin = new Padding(8, 7, 0, 0);
        programBar.Controls.Add(_lblProgramHint);
        programPage.Controls.Add(programBar);
        _tabs.TabPages.Add(programPage);

        var checksPage = Page("TSF kontrolü", _gridChecks);
        var checksBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 6, 4, 4), WrapContents = false };
        checksBar.Controls.Add(new Label { Text = "Kaynak:", AutoSize = true, Margin = new Padding(0, 7, 4, 0) });
        _lnkProcedure.Text = $"{TsfRules.ProcedureName} ({TsfRules.ProcedureVersion})";
        _lnkProcedure.Margin = new Padding(0, 7, 12, 0);
        checksBar.Controls.Add(_lnkProcedure);
        checksBar.Controls.Add(_btnSuggest2);
        checksPage.Controls.Add(checksBar);
        _tabs.TabPages.Add(checksPage);
        var center = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 2, 12, 0) };
        center.Controls.Add(_tabs);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var left = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        left.Controls.AddRange(new Control[] { _btnNext, _btnDocx });
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        right.Controls.AddRange(new Control[] { _btnEdit, _btnPrint, _btnPdf });
        bottom.Controls.Add(left);
        bottom.Controls.Add(right);

        foreach (var b in new[] { _btnUpload, _btnRemove, _btnYonRefresh, _btnYonOpen, _btnYonUse, _btnTempo, _btnNext, _btnDocx, _btnEdit, _btnPrint, _btnPdf, _btnSuggest, _btnSuggest2 }) Style(b);
        _btnEdit.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        _btnPdf.Font = new Font("Segoe UI", 11f, FontStyle.Bold);

        _gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Durum", HeaderText = "", Width = 30, ReadOnly = true });
        _gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Alan", HeaderText = "Alan", Width = 230, ReadOnly = true });
        _gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Deger", HeaderText = "Değer", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridFields.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kaynak", HeaderText = "Kaynak", Width = 190, ReadOnly = true });
        _gridFields.Columns["Deger"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _gridFields.Columns["Kaynak"]!.DefaultCellStyle.ForeColor = Color.Gray;
        _gridFields.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _gridFields.CellEndEdit += (_, e) => FieldEdited(e.RowIndex);

        _gridCats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Adi", HeaderText = "Kategori adı", Width = 320 });
        _gridCats.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kriter", HeaderText = "Kategori kriteri", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridCats.AllowUserToAddRows = _gridCats.AllowUserToDeleteRows = true;

        _gridProgram.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tarih", HeaderText = "Tarih", Width = 240 });
        _gridProgram.Columns.Add(new DataGridViewTextBoxColumn { Name = "Saat", HeaderText = "Saat", Width = 120 });
        _gridProgram.Columns.Add(new DataGridViewTextBoxColumn { Name = "Etkinlik", HeaderText = "Program", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _gridProgram.AllowUserToAddRows = _gridProgram.AllowUserToDeleteRows = true;

        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Durum", HeaderText = "", Width = 30, ReadOnly = true });
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kontrol", HeaderText = "Kontrol", Width = 380, ReadOnly = true });
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Aciklama", HeaderText = "Açıklama", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn { Name = "Madde", HeaderText = "Madde", Width = 90, ReadOnly = true });
        _gridChecks.Columns["Aciklama"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _gridChecks.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _gridChecks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridChecks.CellDoubleClick += (_, e) =>
        {
            // Programdaki satıra git
            if (e.RowIndex >= 0 && _gridChecks.Rows[e.RowIndex].Tag is int prow && prow < _gridProgram.Rows.Count)
            {
                _tabs.SelectedIndex = 2;
                _gridProgram.CurrentCell = _gridProgram.Rows[prow].Cells[1];
            }
        };

        var tip = new ToolTip();
        tip.SetToolTip(_btnYonUse, "Sitedeki yönerge Word belgesiyse kendi şablonlarınıza eklenir.");
        tip.SetToolTip(_btnTempo, "Seçili resmi süreyi \"Düşünme süresi\" alanına yazar (FIDE: 60+ dk klasik, 10–60 dk hızlı, ≤10 dk yıldırım).");
        tip.SetToolTip(_btnNext, "Doldurulmamış bir sonraki alana gider.");
        tip.SetToolTip(_btnSuggest, "Düşünme süresine göre tur saatlerini TSF prosedürü EK-B'deki en az tur arasıyla yeniden yazar (günler ve ilk tur saatleri korunur).");
        tip.SetToolTip(_btnSuggest2, "Düşünme süresine göre tur saatlerini TSF prosedürü EK-B'deki en az tur arasıyla yeniden yazar.");

        Controls.Add(center);
        Controls.Add(top);
        Controls.Add(bottom);
    }

    private Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 7, 4, 0), Font = new Font(Font, FontStyle.Bold) };

    private static TabPage Page(string title, Control content)
    {
        var p = new TabPage(title) { Padding = new Padding(6), BackColor = Color.White };
        content.Dock = DockStyle.Fill;
        p.Controls.Add(content);
        return p;
    }

    private static DataGridView MakeGrid() => new()
    {
        AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
        RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None,
        SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnEnter,
        GridColor = Color.FromArgb(230, 230, 222), ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
    };

    private static void Style(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(4, 0, 4, 0);
        bool primary = b.BackColor == Green;
        if (!primary) b.BackColor = Color.White;
        b.FlatAppearance.BorderColor = primary ? GreenDark : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(134, 168, 100) : Color.FromArgb(236, 241, 231);
    }

    // ================= Şablonlar =================
    private sealed record TemplateItem(string Label, string? Path, string? Resource)
    {
        public override string ToString() => Label;
    }

    private void LoadTemplateList(string? select = null)
    {
        _busy = true;
        _cboTemplate.Items.Clear();
        _cboTemplate.Items.Add(new TemplateItem("Turnuva Yönergesi (hazır şablon)", null, BuiltInResource));
        _cboTemplate.Items.Add(new TemplateItem("Teknik Toplantı Tutanağı (hazır şablon)", null, TutanakResource));
        _cboTemplate.Items.Add(new TemplateItem("Turnuva Sonu Raporu (hazır şablon)", null, SonRaporResource));
        foreach (var name in _ctx.Config.ReportTemplates.ToList())
        {
            var path = Path.Combine(_reportsDir, name);
            if (File.Exists(path)) _cboTemplate.Items.Add(new TemplateItem(Path.GetFileNameWithoutExtension(name), path, null));
            else _ctx.Config.ReportTemplates.Remove(name);
        }
        int idx = 0;
        for (int i = 0; i < _cboTemplate.Items.Count; i++)
            if (select is not null && ((TemplateItem)_cboTemplate.Items[i]!).Path == select) idx = i;
        _cboTemplate.SelectedIndex = idx;
        _btnRemove.Enabled = SelectedTemplate.Path is not null;
        _busy = false;
    }

    private TemplateItem SelectedTemplate => (TemplateItem)(_cboTemplate.SelectedItem ?? _cboTemplate.Items[0]!);

    public static byte[] BuiltInTemplate(string resource = BuiltInResource)
    {
        using var s = typeof(ReportForm).Assembly.GetManifestResourceStream(resource)
                      ?? throw new InvalidOperationException("Gömülü şablon bulunamadı: " + resource);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private async Task UploadTemplateAsync(string? sourcePath)
    {
        if (sourcePath is null)
        {
            using var dlg = new OpenFileDialog { Title = "Rapor/yönerge/tutanak şablonu seç", Filter = "Word belgesi (*.docx)|*.docx" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            sourcePath = dlg.FileName;
        }
        try
        {
            var bytes = await File.ReadAllBytesAsync(sourcePath);
            var a = DocxTemplate.Analyze(bytes); // bozuk/uyumsuz dosyayı baştan yakala
            Directory.CreateDirectory(_reportsDir);
            var name = Path.GetFileName(sourcePath);
            var dest = Path.Combine(_reportsDir, name);
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                File.Copy(sourcePath, dest, overwrite: true);
            if (!_ctx.Config.ReportTemplates.Contains(name, StringComparer.OrdinalIgnoreCase)) _ctx.Config.ReportTemplates.Add(name);
            _ctx.SaveConfig();
            LoadTemplateList(dest);

            if (a.Fields.Count == 0 && !a.HasCategoryTable && !a.HasProgramTable)
                MessageBox.Show(this,
                    "Bu belgede doldurulabilecek bir yer tanınamadı.\n\n" +
                    "Değerlerin yazılacağı yerler için şunlardan birini kullanın:\n" +
                    " • {{IL}}, {{YER}}, {{TARIH_ARALIGI}} gibi işaretler,\n" +
                    " • Word'de Ekle → Yer İşareti (adı: IL, YER, TARIH_ARALIGI …),\n" +
                    " • Geliştirici → İçerik Denetimi (Etiket/Başlık: alan adı),\n" +
                    " • \"İLİ | …\", \"YERİ | …\" gibi etiketli tablo hücreleri.\n\n" +
                    "Belgenin biçimi, tabloları ve kilitli alanları olduğu gibi korunur.",
                    "Şablon", MessageBoxButtons.OK, MessageBoxIcon.Information);
            await RebuildAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Şablon okunamadı: " + ex.Message, "Şablon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task RemoveTemplateAsync()
    {
        var t = SelectedTemplate;
        if (t.Path is null) return;
        if (MessageBox.Show(this, $"“{t.Label}” şablonu listeden kaldırılsın mı?", "Şablon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _ctx.Config.ReportTemplates.RemoveAll(n => string.Equals(Path.Combine(_reportsDir, n), t.Path, StringComparison.OrdinalIgnoreCase));
        try { File.Delete(t.Path); } catch { /* kullanımda olabilir; listeden çıkması yeter */ }
        _ctx.SaveConfig();
        LoadTemplateList();
        await RebuildAsync();
    }

    private void ExportBuiltIn()
    {
        var t = SelectedTemplate;
        var res = t.Resource ?? BuiltInResource;
        using var dlg = new SaveFileDialog
        {
            Filter = "Word belgesi (*.docx)|*.docx",
            FileName = res == TutanakResource ? "teknik_toplanti_tutanagi_sablon.docx" : res == SonRaporResource ? "turnuva_sonu_raporu_sablon.docx" : "turnuva_yonergesi_sablon.docx"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        File.WriteAllBytes(dlg.FileName, BuiltInTemplate(res));
        OpenWithShell(dlg.FileName);
    }

    // ================= TSF il sitesindeki yönerge =================
    private string? Province => !string.IsNullOrWhiteSpace(_ctx.Province) ? _ctx.Province
        : _ctx.Config.Online.Province is { } p && p != Provinces.All ? p : null;

    private async Task LoadYonergelerAsync()
    {
        _filling = true;
        _cboYonerge.Items.Clear();
        _cboYonerge.Items.Add("(kullanma)");
        _cboYonerge.SelectedIndex = 0;
        _filling = false;
        _btnYonOpen.Enabled = _btnYonUse.Enabled = false;
        var site = TsfSites.SiteFor(Province, _ctx.Config.TsfSites);
        if (site is null)
        {
            _lblYon.Text = "İl bilinmediği için TSF sitesi aranmadı (Ayarlar → Varsayılan il).";
            return;
        }
        _lblYon.Text = $"{site} taranıyor…";
        List<TsfDocument> docs;
        try
        {
            using var tsf = new TsfSites();
            docs = await tsf.ListYonergelerAsync(site);
        }
        catch (Exception ex)
        {
            _lblYon.Text = $"{site} açılamadı ({(ex is HttpRequestException or TaskCanceledException ? "bağlantı yok" : ex.Message)}).";
            return;
        }
        if (IsDisposed) return;
        if (docs.Count == 0) { _lblYon.Text = $"{site} ana sayfasında yönerge bulunamadı."; return; }

        // Bu turnuvanın yönergesi öne; yoksa sitedeki en yeni (sayfadaki ilk) seçili gelir.
        var ranked = docs.Select((d, i) => (Doc: d, Score: TsfSites.MatchScore(d, _ctx.EventName), Index: i))
                         .OrderByDescending(x => x.Score >= 0.5 ? x.Score : 0).ThenBy(x => x.Index).ToList();
        _filling = true;
        foreach (var r in ranked) _cboYonerge.Items.Add(new Choice<(TsfDocument Doc, double Score)>(
            (r.Score >= 0.5 ? "★ " : "") + r.Doc, (r.Doc, r.Score)));
        _cboYonerge.SelectedIndex = 1;
        _filling = false;
        await LoadSelectedYonergeAsync();
    }

    private (TsfDocument Doc, double Score)? SelectedYonerge
        => _cboYonerge.SelectedItem is Choice<(TsfDocument Doc, double Score)> c ? c.Value : null;

    private string CachePath(TsfDocument d)
        => Path.Combine(_reportsDir, "tsf", Province ?? "il", d.FileName);

    private async Task LoadSelectedYonergeAsync()
    {
        _btnYonOpen.Enabled = _btnYonUse.Enabled = false;
        if (SelectedYonerge is not { } sel)
        {
            _yonerge = null; _yonergeSame = false;
            _lblYon.Text = "TSF yönergesi kullanılmıyor.";
            RebuildDraft();
            return;
        }
        _lblYon.Text = $"İndiriliyor: {sel.Doc.Title}…";
        try
        {
            var path = CachePath(sel.Doc);
            byte[] bytes;
            if (File.Exists(path)) bytes = await File.ReadAllBytesAsync(path);
            else
            {
                using var tsf = new TsfSites();
                bytes = await tsf.DownloadAsync(sel.Doc.Url);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, bytes);
            }
            var text = sel.Doc.IsWord ? YonergeExtractor.DocxText(bytes) : await Task.Run(() => YonergeExtractor.PdfText(bytes));
            _yonerge = YonergeExtractor.Extract(text);
            var title = YonergeExtractor.Title(text);
            _yonergeSame = Math.Max(sel.Score, TsfSites.MatchScore(title, _ctx.EventName)) >= 0.5;
            _lblYon.Text = _yonergeSame
                ? $"Bu turnuvanın yönergesi: {_yonerge.Count} bilgi okundu (son başvuru, iletişim, program saatleri …)."
                : $"Başka bir turnuvanın yönergesi: yalnız değişmeyen bilgiler (iletişim, organizasyon, saatler) öneri olarak kullanılır.";
            _btnYonOpen.Enabled = true;
            _btnYonUse.Enabled = sel.Doc.IsWord;
        }
        catch (Exception ex)
        {
            _yonerge = null; _yonergeSame = false;
            _lblYon.Text = "Yönerge okunamadı: " + (ex is HttpRequestException or TaskCanceledException ? "bağlantı yok." : ex.Message);
        }
        RebuildDraft();
    }

    private void OpenYonerge()
    {
        if (SelectedYonerge is not { } sel) return;
        var path = CachePath(sel.Doc);
        OpenWithShell(File.Exists(path) ? path : sel.Doc.Url);
    }

    private async Task UseYonergeAsTemplateAsync()
    {
        if (SelectedYonerge is not { } sel || !sel.Doc.IsWord) return;
        var path = CachePath(sel.Doc);
        if (File.Exists(path)) await UploadTemplateAsync(path);
    }

    // ================= Taslak =================
    private async Task RebuildAsync()
    {
        if (_busy) return;
        _busy = true;
        SetEnabled(false);
        try
        {
            if (_info is null)
            {
                _lblStatus.Text = "Turnuva bilgileri chess-results'tan çekiliyor (tarih, yer, hakemler, tur programı)…";
                _info = await _ctx.Service.GetInfoTableAsync(_ctx.EventTnr);
                try { _schedule = await _ctx.Service.GetScheduleAsync(_ctx.EventTnr); } catch { _schedule = new(); }
                _cats.Clear();
                foreach (var c in _ctx.Categories)
                {
                    _lblStatus.Text = $"Kategori bilgisi: {c.Name}…";
                    try { var (max, sys) = await _ctx.CategoryInfo(c.Tnr); _cats.Add((c, max, sys)); }
                    catch { _cats.Add((c, 0, TournamentSystem.Unknown)); }
                }
            }
            var tpl = SelectedTemplate;
            _btnRemove.Enabled = tpl.Path is not null;
            _templateBytes = tpl.Path is null ? BuiltInTemplate(tpl.Resource!) : await File.ReadAllBytesAsync(tpl.Path);
            if (_endReport is null && NeedsEndReport(DocxTemplate.Analyze(_templateBytes))) await LoadEndReportAsync();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Bilgiler alınamadı: " + (ex is HttpRequestException ? "internet bağlantısını kontrol edin." : ex.Message);
        }
        finally
        {
            _busy = false;
            SetEnabled(true);
        }
        RebuildDraft();
    }

    /// <summary>Şablon turnuva sonu tablolarını ya da sayılarını istiyor mu (katılım, il, derece, sıralama).</summary>
    private static bool NeedsEndReport(TemplateAnalysis a)
        => a.Lists.Any(l => l is "katilim" or "il" or "derece" or "siralama")
           || a.Fields.Any(f => f is "SPORCU_SAYISI" or "IL_SAYISI");

    /// <summary>
    /// Her kategorinin final sıralamasını (sonucu girilmiş son turdan sonra) çeker; illeri TSF kayıt
    /// listesiyle eşleştirip katılım, il dağılımı, dereceye girenler ve sıralama tablolarını kurar.
    /// </summary>
    private async Task LoadEndReportAsync()
    {
        var list = new List<CategoryStandings>();
        foreach (var (cat, max, sys) in _cats)
        {
            _lblStatus.Text = $"Final sıralaması alınıyor: {cat.Name}…";
            int ranked = 0;
            ListTable? st = null;
            try
            {
                ranked = (await _ctx.Service.GetEventAsync(cat.Tnr)).RankedRound;
                if (ranked > 0) st = await _ctx.Service.GetStandingsAsync(cat.Tnr, ranked);
            }
            catch { /* bu kategori boş kalır, kullanıcı tablodan düzeltebilir */ }
            list.Add(new CategoryStandings(cat.Name, st, ranked, max, sys.IsTeam()));
        }
        var roster = list.Where(c => c.Standings is not null && !c.IsTeam).SelectMany(c => EndReport.Players(c.Standings!, c.Category));
        var provinceOf = Registration.ProvinceBook.Resolver(_ctx.Config, _ctx.EventTnr, roster, _ctx.EventName);
        _endReport = EndReport.Build(list, provinceOf, DateTime.Today);
    }

    /// <summary>Ağa çıkmadan taslağı yeniden kurar (şablon/yönerge değişince); elle girilenler korunur.</summary>
    private void RebuildDraft()
    {
        if (_info is null || _templateBytes is null) return;
        var manual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (_draft is not null)
        {
            PullGrids();
            foreach (var f in _draft.Fields) if (f.Origin == ManualOrigin) manual[f.Key] = f.Value;
        }
        var analysis = DocxTemplate.Analyze(_templateBytes);
        _draft = ReportBuilder.Build(_info, _schedule, _cats, _ctx.EventName, _ctx.Province, analysis, new ReportSources
        {
            Remembered = _ctx.Config.ReportAnswers,
            EventMemory = _ctx.Config.ReportEventValues.GetValueOrDefault(_ctx.EventTnr.ToString()),
            Yonerge = _yonerge,
            YonergeSameEvent = _yonergeSame,
            EndReport = _endReport
        });
        foreach (var f in _draft.Fields)
            if (manual.TryGetValue(f.Key, out var v))
            { f.Value = v; f.Origin = ManualOrigin; f.Source = v.Length > 0 ? ReportFieldSource.Suggested : ReportFieldSource.Missing; }

        FillGrids();
        SyncTempo();
        RunChecks();
        int missing = _draft.Fields.Count(f => f.Source == ReportFieldSource.Missing && !f.Optional);
        _lblStatus.Text = $"{_draft.Fields.Count(f => f.Source == ReportFieldSource.Automatic)} alan chess-results'tan, " +
                          $"{_draft.Fields.Count(f => f.Source == ReportFieldSource.Suggested)} alan öneriyle dolduruldu" +
                          (missing > 0 ? $" • {missing} eksik bilgi (kırmızı satırlar)" : " • eksik yok, kaydetmeye hazır") +
                          (_draft.HasCategoryTable ? $" • {_draft.Categories.Count} kategori" : "") +
                          (_draft.HasProgramTable ? $" • {_draft.Program.Count} tur programı" : "");
    }

    private void FillGrids()
    {
        if (_draft is null) return;
        _gridFields.Rows.Clear();
        foreach (var f in _draft.Fields)
        {
            int i = _gridFields.Rows.Add(Mark(f.Source), f.Label, f.Value, f.Origin);
            var row = _gridFields.Rows[i];
            row.Tag = f;
            PaintRow(row, f);
            if (!string.IsNullOrEmpty(f.Hint)) row.Cells["Alan"].ToolTipText = f.Hint;
        }
        _gridCats.Rows.Clear();
        foreach (var c in _draft.Categories) _gridCats.Rows.Add(c.Name, c.Criteria);
        _gridProgram.Rows.Clear();
        foreach (var p in _draft.Program) _gridProgram.Rows.Add(p.Date, p.Time, p.Event);

        _tabs.TabPages[1].Text = _draft.HasCategoryTable ? $"Kategoriler ({_draft.Categories.Count})" : "Kategoriler (şablonda yok)";
        _tabs.TabPages[2].Text = _draft.HasProgramTable ? $"Program ({_draft.Program.Count})" : "Program (şablonda yok)";
        UpdateNextButton();
    }

    private static string Mark(ReportFieldSource s) => s switch
    {
        ReportFieldSource.Automatic => "✓",
        ReportFieldSource.Suggested => "•",
        _ => "✎"
    };

    private static void PaintRow(DataGridViewRow row, ReportField f)
    {
        row.DefaultCellStyle.BackColor = f.Source switch
        {
            ReportFieldSource.Missing when !f.Optional => MissingBg,
            ReportFieldSource.Suggested => SuggestedBg,
            _ => Color.White
        };
        row.Cells[0].Style.ForeColor = f.Source == ReportFieldSource.Automatic ? GreenDark : Color.DarkGoldenrod;
    }

    private void FieldEdited(int rowIndex)
    {
        if (rowIndex < 0 || _gridFields.Rows[rowIndex].Tag is not ReportField f) return;
        var v = _gridFields.Rows[rowIndex].Cells["Deger"].Value?.ToString()?.Trim() ?? "";
        if (v == f.Value) return;
        SetField(f, v, ManualOrigin);
    }

    private void SetField(ReportField f, string value, string origin)
    {
        f.Value = value;
        f.Origin = origin;
        f.Source = value.Length == 0 ? ReportFieldSource.Missing : ReportFieldSource.Suggested;
        foreach (DataGridViewRow r in _gridFields.Rows)
        {
            if (r.Tag != f) continue;
            r.Cells["Deger"].Value = value;
            r.Cells["Kaynak"].Value = origin;
            r.Cells[0].Value = Mark(f.Source);
            PaintRow(r, f);
        }
        UpdateNextButton();
        if (f.Key is "DUSUNME_SURESI" or "PROGRAM_KAYIT" or "PROGRAM_TEKNIK" or "PROGRAM_ILAN") RunChecks();
    }

    private void UpdateNextButton()
    {
        int n = _draft?.Fields.Count(f => f.Source == ReportFieldSource.Missing && !f.Optional) ?? 0;
        _btnNext.Text = n > 0 ? $"▶ Sonraki eksik ({n})" : "▶ Sonraki eksik";
        _btnNext.Enabled = (_draft?.Fields.Any(f => f.Source == ReportFieldSource.Missing) ?? false);
    }

    /// <summary>Toplu düzenleme: imleci bir sonraki doldurulmamış alana götürüp düzenlemeyi açar.</summary>
    private void GoToNextMissing()
    {
        _tabs.SelectedIndex = 0;
        int start = _gridFields.CurrentCell?.RowIndex ?? -1;
        int n = _gridFields.Rows.Count;
        // Önce zorunlu eksikler, kalmadıysa isteğe bağlı boş alanlar.
        foreach (var requiredOnly in new[] { true, false })
            for (int k = 1; k <= n; k++)
            {
                var row = _gridFields.Rows[(start + k + n) % n];
                if (row.Tag is ReportField { Source: ReportFieldSource.Missing } f && (!requiredOnly || !f.Optional))
                {
                    _gridFields.CurrentCell = row.Cells["Deger"];
                    _gridFields.BeginEdit(true);
                    return;
                }
            }
    }

    // ================= TSF prosedürü kontrolü =================
    private (int Minutes, int Increment)? CurrentTempo()
        => TimeControls.Parse(_draft?.Fields.FirstOrDefault(f => f.Key == "DUSUNME_SURESI")?.Value)
           ?? TimeControls.Parse(_info is null ? null : _info.FirstOrDefault(k => k.Key.StartsWith("Zaman kontrol", StringComparison.OrdinalIgnoreCase)).Value);

    private bool IsElo => TsfRules.IsElo(_info?.FirstOrDefault(k => EventGrouping.Fold(k.Key).StartsWith("rating hesap")).Value);

    /// <summary>Rapordaki programın satırları (açılış kalemleri ilk güne), belge sırasıyla.</summary>
    private (List<ScheduleItem> Items, int PreCount) ScheduleItems()
    {
        var items = new List<ScheduleItem>();
        if (_draft is null) return (items, 0);
        var data = _draft.ToReportData();
        foreach (var row in data.Program)
            items.Add(new ScheduleItem(row.GetValueOrDefault("tarih") ?? "", row.GetValueOrDefault("saat") ?? "", row.GetValueOrDefault("etkinlik") ?? ""));
        return (items, data.Program.Count - _draft.Program.Count);
    }

    private async Task LoadPlayerCountsAsync()
    {
        if (_ctx.CategoryStats is null) return;
        foreach (var (cat, _, _) in _cats.ToList())
        {
            if (IsDisposed) return;
            try { _stats[cat.Tnr] = await _ctx.CategoryStats(cat); }
            catch { _stats[cat.Tnr] = new CategoryStats(0, 0, null); }
        }
        if (!IsDisposed) RunChecks();
    }

    /// <summary>Program ve kategorileri TSF prosedürüne göre kontrol edip "TSF kontrolü" sekmesini doldurur.</summary>
    private void RunChecks()
    {
        if (_draft is null) return;
        var tempo = CurrentTempo();
        var (items, pre) = ScheduleItems();
        var checkIn = _draft.Fields.FirstOrDefault(f => f.Key == "PROGRAM_KAYIT")?.Value;
        // EK-A: ELO etkinliğinde 2400+ sporcu varsa günlük tur sınırı 2
        var eloCheck = TsfRules.CheckEloTempo(IsElo, _stats.Values.Select(s => s.MaxRating).DefaultIfEmpty(0).Max(), tempo);
        _checks = _draft.HasProgramTable || items.Count > 0
            ? TsfRules.CheckSchedule(items, tempo, IsElo, checkIn, eloCheck?.MaxPerDay, eloCheck is null ? null : "EK-A")
            : new List<RuleCheck>();
        if (eloCheck is { } ec) _checks.Add(ec.Check);
        foreach (var (cat, max, sys) in _cats)
        {
            var name = EventGrouping.ShortCategory(cat.Name);
            _checks.Add(TsfRules.CheckCategory(name, _stats.GetValueOrDefault(cat.Tnr)?.Players ?? 0, sys, max));
            if (TsfRules.CheckAgeCategory(cat.Name) is { } age) _checks.Add(age);
        }
        if (TsfRules.CheckTempoUnity(_cats.Select(c => (EventGrouping.ShortCategory(c.Cat.Name), _stats.GetValueOrDefault(c.Cat.Tnr)?.TimeControl)).ToList()) is { } unity)
            _checks.Add(unity);

        _gridChecks.Rows.Clear();
        foreach (var c in _checks.OrderByDescending(c => c.Level))
        {
            int i = _gridChecks.Rows.Add(c.Level switch { RuleLevel.Ok => "✓", RuleLevel.Info => "ℹ", RuleLevel.Warning => "⚠", _ => "✖" },
                                         c.Title, c.Detail, c.Source);
            var row = _gridChecks.Rows[i];
            row.DefaultCellStyle.BackColor = c.Level switch
            {
                RuleLevel.Error => MissingBg,
                RuleLevel.Warning => SuggestedBg,
                _ => Color.White
            };
            row.Cells[0].Style.ForeColor = c.Level switch { RuleLevel.Ok => GreenDark, RuleLevel.Error => Color.Firebrick, _ => Color.DarkGoldenrod };
            if (c.ProgramRow is int pr && pr - pre >= 0) row.Tag = pr - pre;
        }

        // Program tablosunda hatalı satırları işaretle
        var bad = _checks.Where(c => c.Level == RuleLevel.Error && c.ProgramRow is int).Select(c => c.ProgramRow!.Value - pre).ToHashSet();
        foreach (DataGridViewRow r in _gridProgram.Rows)
            if (!r.IsNewRow) r.DefaultCellStyle.BackColor = bad.Contains(r.Index) ? MissingBg : Color.White;

        int errors = _checks.Count(c => c.Level == RuleLevel.Error), warnings = _checks.Count(c => c.Level == RuleLevel.Warning);
        _tabs.TabPages[3].Text = errors + warnings == 0 ? "TSF kontrolü ✓" : $"TSF kontrolü ({errors + warnings} uyarı)";
        if (tempo is { } t)
        {
            var (gap, exact, basis) = TsfRules.MinRoundGap(t.Minutes, t.Increment);
            _lblProgramHint.Text = $"{TimeControls.Short(t.Minutes, t.Increment)} için iki tur başlangıcı arası en az {gap} dk" +
                                   (exact ? "" : $" ({TimeControls.Short(basis.Minutes, basis.Increment)} esas)") +
                                   $" • günde en fazla {TsfRules.MaxRoundsPerDay(IsElo)} tur ({(IsElo ? "ELO" : "UKD")}) • gün 12 saati aşamaz";
        }
        else _lblProgramHint.Text = "Düşünme süresi girilince tur aralıkları kontrol edilir.";
    }

    /// <summary>Tur saatlerini EK-B'ye göre yeniden yazar; günler ve her günün ilk tur saati korunur.</summary>
    private void SuggestProgram()
    {
        if (_draft is null) return;
        PullGrids();
        if (CurrentTempo() is not { } tempo)
        {
            MessageBox.Show(this, "Önce \"Düşünme süresi\" alanını doldurun (ör. 35 DAKİKA + HAMLE BAŞINA 30 SANİYE).", "Program", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        // Günler: programdaki tarihler; yoksa tarih aralığı
        var days = _draft.Program.Select(p => p.Date).Where(d => d.Length > 0).Distinct().ToList();
        if (days.Count == 0) days.Add("");
        int rounds = Math.Max(_cats.Count > 0 ? _cats.Max(c => c.Max) : 0, _draft.Program.Count(p => p.Event.Contains("Tur")));
        if (rounds <= 0) rounds = 5;
        int Start(string day, int fallback) => _draft.Program.Where(p => p.Date == day).Select(p => TsfRules.ParseTime(p.Time)?.Start)
                                                           .FirstOrDefault(x => x is not null) ?? fallback;
        int first = Start(days[0], 10 * 60);
        var plan = TsfRules.SuggestSchedule(rounds, days.Count, first, tempo, IsElo);
        // Sonraki günler kendi ilk saatleriyle başlar
        var rows = new List<ReportProgramRow>();
        foreach (var g in plan.GroupBy(p => p.Day))
        {
            int dayStart = Start(days[Math.Min(g.Key, days.Count - 1)], first);
            int shift = dayStart - g.First().Start;
            foreach (var (day, round, start) in g)
                rows.Add(new ReportProgramRow { Date = days[Math.Min(day, days.Count - 1)], Time = TsfRules.FormatTime(start + shift), Event = $"{round}. Tur" });
        }
        var preview = string.Join("\n", rows.GroupBy(r => r.Date).Select(g => $"{(g.Key.Length > 0 ? g.Key : "Gün")}: {string.Join(", ", g.Select(r => $"{r.Event} {r.Time}"))}"));
        var gap = TsfRules.MinRoundGap(tempo.Minutes, tempo.Increment).Gap;
        if (MessageBox.Show(this, $"{TimeControls.Short(tempo.Minutes, tempo.Increment)} için iki tur arası en az {gap} dk (TSF prosedürü EK-B).\n\n{preview}\n\nProgram bu saatlerle değiştirilsin mi?",
                            "Program önerisi", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        _draft.Program.Clear();
        _draft.Program.AddRange(rows);
        _gridProgram.Rows.Clear();
        foreach (var p in _draft.Program) _gridProgram.Rows.Add(p.Date, p.Time, p.Event);
        _tabs.TabPages[2].Text = $"Program ({_draft.Program.Count})";
        RunChecks();
    }

    // ================= Tempo / resmi süre =================
    private void SyncTempo()
    {
        bool has = _draft?.Fields.Any(f => f.Key == "DUSUNME_SURESI") ?? false;
        _cboTempo.Enabled = _cboPreset.Enabled = _btnTempo.Enabled = has;
        if (!has || _draft is null) return;
        var current = _draft.Fields.First(f => f.Key == "DUSUNME_SURESI").Value;
        var tempo = _draft.Tempo ?? (TimeControls.Parse(current) is { } tc ? TimeControls.Classify(tc.Minutes, tc.Increment) : Tempo.Klasik);
        _cboTempo.SelectedIndex = (int)tempo;
        FillPresets(current);
    }

    private void FillPresets(string? current = null)
    {
        if (_cboTempo.SelectedItem is not Choice<Tempo> t) return;
        _cboPreset.Items.Clear();
        foreach (var (mn, inc) in TimeControls.Presets(t.Value))
            _cboPreset.Items.Add(new Choice<(int, int)>($"{TimeControls.Short(mn, inc)}   —   {TimeControls.Format(mn, inc)}", (mn, inc)));
        var parsed = TimeControls.Parse(current);
        int idx = 0;
        for (int i = 0; i < _cboPreset.Items.Count; i++)
            if (parsed is { } p && ((Choice<(int, int)>)_cboPreset.Items[i]!).Value == p) idx = i;
        _cboPreset.SelectedIndex = idx;
    }

    private void ApplyTempo()
    {
        if (_draft is null || _cboPreset.SelectedItem is not Choice<(int Mn, int Inc)> p || _cboTempo.SelectedItem is not Choice<Tempo> t) return;
        var f = _draft.Fields.FirstOrDefault(x => x.Key == "DUSUNME_SURESI");
        if (f is null) return;
        SetField(f, TimeControls.Format(p.Value.Mn, p.Value.Inc), $"{t.Label} önerisi");
    }

    /// <summary>Izgaralardaki düzenlemeleri taslağa geri yazar.</summary>
    private void PullGrids()
    {
        if (_draft is null) return;
        _gridFields.EndEdit(); _gridCats.EndEdit(); _gridProgram.EndEdit();
        foreach (DataGridViewRow r in _gridFields.Rows)
            if (r.Tag is ReportField f)
            {
                var v = r.Cells["Deger"].Value?.ToString()?.Trim() ?? "";
                if (v != f.Value) { f.Value = v; f.Origin = ManualOrigin; }
            }

        _draft.Categories.Clear();
        foreach (DataGridViewRow r in _gridCats.Rows)
        {
            if (r.IsNewRow) continue;
            var name = r.Cells[0].Value?.ToString()?.Trim() ?? "";
            var crit = r.Cells[1].Value?.ToString()?.Trim() ?? "";
            if (name.Length + crit.Length > 0) _draft.Categories.Add(new ReportCategoryRow { Name = name, Criteria = crit });
        }
        _draft.Program.Clear();
        foreach (DataGridViewRow r in _gridProgram.Rows)
        {
            if (r.IsNewRow) continue;
            var row = new ReportProgramRow
            {
                Date = r.Cells[0].Value?.ToString()?.Trim() ?? "",
                Time = r.Cells[1].Value?.ToString()?.Trim() ?? "",
                Event = r.Cells[2].Value?.ToString()?.Trim() ?? ""
            };
            if (row.Date.Length + row.Time.Length + row.Event.Length > 0) _draft.Program.Add(row);
        }
    }

    /// <summary>
    /// Cevapları saklar: iletişim/organizasyon gibi sabit bilgiler sonraki turnuvalar için, tüm değerler
    /// de bu turnuva için (aynı turnuvanın tutanağı/raporu bunlarla dolar).
    /// </summary>
    private void RememberAnswers()
    {
        if (_draft is null) return;
        foreach (var f in _draft.Fields)
            if (ReportBuilder.Remembered.Contains(f.Key) && f.Source != ReportFieldSource.Automatic && f.Value.Trim().Length > 0)
                _ctx.Config.ReportAnswers[f.Key] = f.Value.Trim();
        var key = _ctx.EventTnr.ToString();
        var mem = _ctx.Config.ReportEventValues.GetValueOrDefault(key) ?? new Dictionary<string, string>();
        foreach (var f in _draft.Fields)
            if (f.Source != ReportFieldSource.Automatic && f.Value.Trim().Length > 0) mem[f.Key] = f.Value.Trim();
        _ctx.Config.ReportEventValues.Remove(key);
        _ctx.Config.ReportEventValues[key] = mem; // en sona: budamada en yeni kalır
        _ctx.SaveConfig();
    }

    // ================= Çıktı =================
    /// <summary>Doldurulmuş belgeyi üretir. null = kullanıcı eksikleri doldurmak istedi.</summary>
    private byte[]? BuildDocument(bool askMissing = true)
    {
        if (_draft is null || _templateBytes is null) return null;
        PullGrids();
        var missing = _draft.Fields.Where(f => !f.Optional && string.IsNullOrWhiteSpace(f.Value)).ToList();
        if (askMissing && missing.Count > 0)
        {
            var ans = MessageBox.Show(this,
                $"{missing.Count} bilgi boş: {string.Join(", ", missing.Take(4).Select(m => m.Label))}{(missing.Count > 4 ? "…" : "")}\n\n" +
                "Boş bırakılarak devam edilsin mi?\n(Hayır: tabloda ilk eksik alana gidilir.)",
                "Eksik bilgi", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans != DialogResult.Yes)
            {
                _gridFields.CurrentCell = null;
                GoToNextMissing();
                return null;
            }
        }
        RememberAnswers();
        return DocxTemplate.Fill(_templateBytes, _draft.ToReportData());
    }

    private string DefaultName(string ext)
    {
        var name = _draft?.Fields.FirstOrDefault(f => f.Key == "TURNUVA_ADI")?.Value;
        if (string.IsNullOrWhiteSpace(name)) name = EventGrouping.BaseName(_ctx.EventName);
        var slug = Slug(name);
        var t = SelectedTemplate;
        var kind = t.Resource == BuiltInResource ? "yonerge" : t.Resource == TutanakResource ? "teknik_toplanti_tutanagi" : t.Resource == SonRaporResource ? "turnuva_sonu_raporu" : Slug(t.Label);
        return $"{(slug.Length == 0 ? "turnuva" : slug)}_{kind}.{ext}";
    }

    private string WriteTempDocx(byte[] bytes)
    {
        Directory.CreateDirectory(_ctx.OutputDir);
        var path = Path.Combine(_ctx.OutputDir, DefaultName("docx"));
        try { File.WriteAllBytes(path, bytes); }
        catch (IOException) // Word'de açık olabilir
        {
            path = Path.Combine(_ctx.OutputDir, Path.GetFileNameWithoutExtension(path) + $"_{DateTime.Now:HHmmss}.docx");
            File.WriteAllBytes(path, bytes);
        }
        return path;
    }

    private async Task SavePdfAsync()
    {
        var bytes = BuildDocument();
        if (bytes is null) return;
        if (!WordExport.IsAvailable)
        {
            var docx = WriteTempDocx(bytes);
            MessageBox.Show(this,
                "PDF'e dönüştürmek için Microsoft Word gerekiyor ve bu bilgisayarda bulunamadı.\n\n" +
                $"Belge Word dosyası olarak kaydedildi:\n{docx}\n\nAçılan programda \"Farklı Kaydet → PDF\" ile PDF alabilirsiniz.",
                "PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenWithShell(docx);
            return;
        }
        using var dlg = new SaveFileDialog
        {
            Title = "PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf",
            FileName = DefaultName("pdf"), InitialDirectory = _ctx.OutputDir
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        await RunExport("PDF hazırlanıyor (Word arka planda çalışıyor)…", async () =>
        {
            var docx = WriteTempDocx(bytes);
            await WordExport.ToPdfAsync(docx, dlg.FileName);
            _lblStatus.Text = "PDF kaydedildi: " + dlg.FileName;
            OpenWithShell(dlg.FileName);
        });
    }

    /// <summary>
    /// Yazdırmadan önce önizleme: belge Word ile PDF'e çevrilir ve sayfaları birebir gösterilir.
    /// Yazıcıya gönderme ve PDF kaydetme önizleme penceresinden yapılır.
    /// </summary>
    private async Task PreviewAsync()
    {
        var bytes = BuildDocument();
        if (bytes is null) return;
        if (!WordExport.IsAvailable)
        {
            // Word yoksa önizleme üretilemez: belge varsayılan programda açılır (oradan yazdırılır).
            var docx = WriteTempDocx(bytes);
            MessageBox.Show(this, "Önizleme ve yazdırma için Microsoft Word gerekiyor; bu bilgisayarda bulunamadı.\n\n" +
                                  $"Belge Word dosyası olarak kaydedildi ve açılıyor:\n{docx}", "Önizleme", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OpenWithShell(docx);
            return;
        }
        List<Bitmap>? pages = null;
        List<SizeF>? sizes = null;
        string? pdf = null;
        bool direct = PrintRouter.IsSilent;
        await RunExport(direct ? $"Yazdırılıyor (Word belgeyi PDF'e çeviriyor) → {PrintRouter.TargetName}…"
                               : "Önizleme hazırlanıyor (Word belgeyi PDF'e çeviriyor)…", async () =>
        {
            var docx = WriteTempDocx(bytes);
            pdf = Path.Combine(Path.GetTempPath(), $"rapor_onizleme_{Guid.NewGuid():N}.pdf");
            await WordExport.ToPdfAsync(docx, pdf);
            sizes = await PdfPages.SizesAsync(pdf);
            if (direct)
            {
                // Doğrudan yazdır açık: önizleme açılmadan yazıcıya
                if (await ReportPreviewForm.PrintPdfAsync(this, pdf, sizes, SelectedTemplate.Label))
                    _lblStatus.Text = $"Yazıcıya gönderildi: {PrintRouter.TargetName}";
                try { File.Delete(pdf); } catch { }
                pdf = null;
                return;
            }
            pages = await PdfPages.RenderAsync(pdf, 110);
        });
        if (pages is null || sizes is null || pdf is null) return;
        try
        {
            using var preview = new ReportPreviewForm(pdf, pages, sizes, DefaultName("pdf"), _ctx.OutputDir, SelectedTemplate.Label) { Icon = Icon };
            preview.ShowDialog(this);
            _lblStatus.Text = preview.Printed ? $"Yazıcıya gönderildi: {PrintRouter.TargetName}"
                            : preview.SavedPdf is { } saved ? "PDF kaydedildi: " + saved
                            : "Önizleme kapatıldı.";
        }
        finally
        {
            try { File.Delete(pdf); } catch { /* geçici dosya */ }
        }
    }

    // ================= Önizle ve Düzenle =================
    /// <summary>
    /// Belgeyi Word görünümünde açar; alanlar sayfa üzerinde tıklanıp yazılarak düzenlenir (sabit metin
    /// kilitli). Değişiklikler bu penceredeki tabloya anında yansır; yazdırma/PDF oradan da yapılabilir.
    /// </summary>
    private void OpenEditor()
    {
        if (_draft is null || _templateBytes is null) return;
        if (!WordExport.IsAvailable)
        {
            MessageBox.Show(this, "Önizle ve Düzenle için Microsoft Word gerekiyor; bu bilgisayarda bulunamadı.\n\n" +
                                  "Alanları tablodan doldurup \"Word (.docx) kaydet\" ile belgeyi alabilirsiniz.",
                            "Önizle ve Düzenle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        PullGrids();
        bool edited = false;
        var ctx = new ReportEditorContext(
            SelectedTemplate.Label,
            () => EditorTargets(() => edited = true),
            RenderForEditorAsync,
            PrintFromEditorAsync,
            SavePdfFromEditorAsync,
            SaveDocxFromEditor);
        using (var editor = new ReportEditorForm(ctx) { Icon = Icon })
            editor.ShowDialog(this);
        if (edited) RememberAnswers(); // elle yazılanlar sonraki rapor/tutanak için hatırlansın
    }

    private static readonly Dictionary<string, string> ListTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kategori"] = "Kategori", ["program"] = "Program", ["katilim"] = "Katılım", ["il"] = "İl dağılımı",
        ["derece"] = "Dereceye giren", ["siralama"] = "Sıralama"
    };

    private static readonly Dictionary<string, string> ColumnTitles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kategori.adi"] = "Kategori adı", ["kategori.kriteri"] = "Kriter", ["program.tarih"] = "Tarih", ["program.saat"] = "Saat",
        ["program.etkinlik"] = "Program", ["il.adi"] = "İl", ["il.sayi"] = "Sporcu sayısı", ["sporcu"] = "Sporcu sayısı",
        ["tur"] = "Oynanan tur", ["kategori"] = "Kategori", ["derece"] = "Derece", ["sira"] = "Sıra", ["ad"] = "Ad soyad",
        ["il"] = "İl", ["rtg"] = "Rating", ["puan"] = "Puan"
    };

    private static string ColumnTitle(string list, string col)
        => ColumnTitles.TryGetValue(list + "." + col, out var t) || ColumnTitles.TryGetValue(col, out t) ? t : col;

    private static EditTarget Cell(string list, int row, string col, Func<string> get, Action<string> set) => new()
    {
        Key = $"{list}[{row}].{col}",
        Label = $"{(ListTitles.TryGetValue(list, out var lt) ? lt : list)} {row + 1} · {ColumnTitle(list, col)}",
        Placeholder = ColumnTitle(list, col),
        Optional = true,
        Get = get,
        Set = set
    };

    /// <summary>
    /// Sayfada düzenlenebilecek her şey: tekil alanlar (alan tablosu), kategori ve program hücreleri
    /// (sekmelerdeki tablolar) ve turnuva sonu tabloları. Anahtarlar belgedeki işaretlerle aynıdır
    /// ("program[2].saat"). Program başına eklenen açılış kalemlerinde yalnız saat düzenlenir (alanından).
    /// </summary>
    private List<EditTarget> EditorTargets(Action onEdit)
    {
        var list = new List<EditTarget>();
        if (_draft is null) return list;
        foreach (var f in _draft.Fields)
            list.Add(new EditTarget
            {
                Key = f.Key, Label = f.Label, Placeholder = f.Label, Hint = f.Hint, Optional = f.Optional, Multiline = f.Multiline,
                Get = () => f.Value, Set = v => { SetField(f, v, ManualOrigin); onEdit(); }
            });

        Action<string> GridSetter(DataGridView grid, int row, int col) => v => { grid.Rows[row].Cells[col].Value = v; onEdit(); };
        Func<string> GridGetter(DataGridView grid, int row, int col) => () => grid.Rows[row].Cells[col].Value?.ToString() ?? "";

        int k = 0;
        static bool Blank(DataGridViewRow r) => r.Cells.Cast<DataGridViewCell>().All(c => string.IsNullOrWhiteSpace(c.Value?.ToString()));
        foreach (DataGridViewRow r in _gridCats.Rows)
        {
            if (r.IsNewRow || Blank(r)) continue; // PullGrids boş satırı belgeye koymaz
            int i = r.Index, n = k++;
            list.Add(Cell("kategori", n, "adi", GridGetter(_gridCats, i, 0), GridSetter(_gridCats, i, 0)));
            list.Add(Cell("kategori", n, "kriteri", GridGetter(_gridCats, i, 1), GridSetter(_gridCats, i, 1)));
        }

        // Program: önce açılış kalemleri (saatleri dolu olan PROGRAM_* alanları), sonra sekmedeki satırlar.
        int row = 0;
        foreach (var (key, _) in ReportDraft.PreProgram)
        {
            var f = _draft.Fields.FirstOrDefault(x => x.Key == key);
            if (f is null || string.IsNullOrWhiteSpace(f.Value)) continue;
            var c = Cell("program", row++, "saat", () => f.Value, v => { SetField(f, v, ManualOrigin); onEdit(); });
            list.Add(new EditTarget { Key = c.Key, Label = f.Label, Placeholder = "Saat", Optional = true, Get = c.Get, Set = c.Set });
        }
        foreach (DataGridViewRow r in _gridProgram.Rows)
        {
            if (r.IsNewRow || Blank(r)) continue;
            int i = r.Index, n = row++;
            list.Add(Cell("program", n, "tarih", GridGetter(_gridProgram, i, 0), GridSetter(_gridProgram, i, 0)));
            list.Add(Cell("program", n, "saat", GridGetter(_gridProgram, i, 1), GridSetter(_gridProgram, i, 1)));
            list.Add(Cell("program", n, "etkinlik", GridGetter(_gridProgram, i, 2), GridSetter(_gridProgram, i, 2)));
        }

        // Turnuva sonu tabloları (katılım, il, derece, sıralama): taslaktaki satırlar
        foreach (var (name, rows) in _draft.Lists)
            for (int i = 0; i < rows.Count; i++)
            {
                var data = rows[i];
                foreach (var col in data.Keys.ToList())
                    list.Add(Cell(name, i, col, () => data.TryGetValue(col, out var v) ? v : "", v => { data[col] = v; onEdit(); }));
            }
        return list;
    }

    /// <summary>Düzenleme modunda belge: alanlar renk koduyla işaretli, boşlar "[Alan adı]" yer tutucuyla.</summary>
    private async Task<EditorRender> RenderForEditorAsync(WordSession word)
    {
        PullGrids();
        var placeholders = EditorTargets(() => { }).GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Placeholder, StringComparer.OrdinalIgnoreCase);
        var marker = new FieldMarker(k => "[" + (placeholders.TryGetValue(k, out var l) && l.Length > 0 ? l : k) + "]");
        var bytes = DocxTemplate.Fill(_templateBytes!, _draft!.ToReportData(), marker);
        var docx = Path.Combine(Path.GetTempPath(), $"rapor_duzenle_{Guid.NewGuid():N}.docx");
        var pdf = Path.ChangeExtension(docx, ".pdf");
        await File.WriteAllBytesAsync(docx, bytes);
        try { await word.ToPdfAsync(docx, pdf); }
        finally { try { File.Delete(docx); } catch { } }
        var sizes = await PdfPages.SizesAsync(pdf);
        var pages = await PdfPages.RenderAsync(pdf, 110);
        var boxes = await Task.Run(() => PdfFieldLocator.Locate(pdf, marker.Keys));
        return new EditorRender(pdf, pages, sizes, boxes);
    }

    /// <summary>Temiz (işaretsiz) belgeyi açık Word oturumuyla PDF'e çevirir.</summary>
    private async Task<string?> CleanPdfAsync(WordSession word, string pdfPath)
    {
        var bytes = BuildDocument(askMissing: false); // boş alan sorusu düzenleme ekranında soruldu
        if (bytes is null) return null;
        var docx = WriteTempDocx(bytes);
        await word.ToPdfAsync(docx, pdfPath);
        return pdfPath;
    }

    private async Task<string?> PrintFromEditorAsync(WordSession word, IWin32Window owner)
    {
        var pdf = Path.Combine(Path.GetTempPath(), $"rapor_yazdir_{Guid.NewGuid():N}.pdf");
        try
        {
            if (await CleanPdfAsync(word, pdf) is null) return null;
            var sizes = await PdfPages.SizesAsync(pdf);
            bool ok = await ReportPreviewForm.PrintPdfAsync(owner, pdf, sizes, SelectedTemplate.Label);
            if (ok) _lblStatus.Text = $"Yazıcıya gönderildi: {PrintRouter.TargetName}";
            return ok ? $"✓ Yazıcıya gönderildi: {PrintRouter.TargetName}" : null;
        }
        finally { try { File.Delete(pdf); } catch { } }
    }

    private async Task<string?> SavePdfFromEditorAsync(WordSession word, IWin32Window owner)
    {
        using var dlg = new SaveFileDialog
        {
            Title = "PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf",
            FileName = DefaultName("pdf"), InitialDirectory = _ctx.OutputDir
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        if (await CleanPdfAsync(word, dlg.FileName) is null) return null;
        _lblStatus.Text = "PDF kaydedildi: " + dlg.FileName;
        OpenWithShell(dlg.FileName);
        return "✓ PDF kaydedildi: " + Path.GetFileName(dlg.FileName);
    }

    private string? SaveDocxFromEditor(IWin32Window owner)
    {
        var bytes = BuildDocument(askMissing: false);
        if (bytes is null) return null;
        using var dlg = new SaveFileDialog
        {
            Title = "Word belgesi olarak kaydet", Filter = "Word belgesi (*.docx)|*.docx",
            FileName = DefaultName("docx"), InitialDirectory = _ctx.OutputDir
        };
        if (dlg.ShowDialog(owner) != DialogResult.OK) return null;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            _lblStatus.Text = "Kaydedildi: " + dlg.FileName;
            return "✓ Word belgesi kaydedildi: " + Path.GetFileName(dlg.FileName);
        }
        catch (IOException ex) { return "Kaydedilemedi (dosya açık olabilir): " + ex.Message; }
    }

    private void SaveDocx()
    {
        var bytes = BuildDocument();
        if (bytes is null) return;
        using var dlg = new SaveFileDialog
        {
            Title = "Word belgesi olarak kaydet", Filter = "Word belgesi (*.docx)|*.docx",
            FileName = DefaultName("docx"), InitialDirectory = _ctx.OutputDir
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllBytes(dlg.FileName, bytes);
            _lblStatus.Text = "Kaydedildi: " + dlg.FileName;
            OpenWithShell(dlg.FileName);
        }
        catch (IOException ex) { MessageBox.Show(this, "Kaydedilemedi (dosya açık olabilir): " + ex.Message, "Kaydet", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private async Task RunExport(string status, Func<Task> work)
    {
        SetEnabled(false);
        UseWaitCursor = true;
        _lblStatus.Text = status;
        try { await work(); }
        catch (Exception ex)
        {
            _lblStatus.Text = "İşlem tamamlanamadı.";
            MessageBox.Show(this, "Word ile işlem yapılamadı: " + (ex.InnerException?.Message ?? ex.Message) +
                                  "\n\nBelgeyi \"Word (.docx) kaydet\" ile alıp elle dönüştürebilirsiniz.",
                            "Hata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; SetEnabled(true); }
    }

    private void SetEnabled(bool on)
    {
        foreach (var c in new Control[] { _cboTemplate, _btnUpload, _btnDocx, _btnEdit, _btnPrint, _btnPdf, _cboYonerge, _btnYonRefresh })
            c.Enabled = on;
        _btnRemove.Enabled = on && _cboTemplate.Items.Count > 0 && SelectedTemplate.Path is not null;
        if (on) UpdateNextButton(); else _btnNext.Enabled = false;
    }

    private static void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { /* açılamadıysa yine de kaydedildi */ }
    }

    private static string Slug(string s)
    {
        var f = EventGrouping.Fold(s.Trim());
        var sb = new System.Text.StringBuilder();
        foreach (var c in f)
        {
            if (char.IsLetterOrDigit(c) && c < 128) sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var r = sb.ToString().Trim('_');
        return r.Length > 60 ? r[..60].Trim('_') : r;
    }
}
