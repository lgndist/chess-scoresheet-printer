using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Lists;

/// <summary>Hakem kağıtları penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record ArbiterSheetsContext(
    string EventName,
    IReadOnlyList<CategoryRef> Categories,
    Func<CategoryRef, int?, Task<ArbiterSheet?>> Load,   // tur null = son eşlenen tur; eşleştirme yoksa null
    Func<CategoryRef, Task<List<ArbiterSheet>>> LoadAllRounds,   // döner turnuvada bütün turlar
    ArbiterSheetConfig Config,
    Action SaveConfig,
    string OutputDir);

/// <summary>
/// Hakem eşleştirme kağıtları: hangi kategorilerin basılacağı, tur, sayfa düzeni (her kategori ayrı,
/// sığdıkça aynı sayfa ya da elle sayfa grupları) ve yazı boyutu seçilir; döner (Berger) kategorilerde
/// bütün turlar, ceza bildirim formu (sayfa altında ya da ayrı sayfa, boş veya sporcularla dolu).
/// Sağda sayfa sayfa canlı önizleme.
/// </summary>
public sealed class ArbiterSheetsForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);
    private static readonly (string Label, float Pt)[] Sizes = { ("Normal", 11f), ("Büyük (önerilen)", 12.5f), ("Çok büyük", 14f) };
    private static readonly (string Label, PenaltyForm Kind)[] Penalties =
    {
        ("Ekleme", PenaltyForm.None),
        ("Her sayfanın altında (boş, katlama çizgisiyle)", PenaltyForm.Below),
        ("Ayrı sayfa — boş form", PenaltyForm.BlankPage),
        ("Ayrı sayfa — işaretli kategorilerin sporcularıyla (masa, ad, tur)", PenaltyForm.FilledPage)
    };

    private readonly ArbiterSheetsContext _ctx;
    private readonly Dictionary<int, ArbiterSheet?> _sheets = new();   // kategori tnr → yüklenen kağıt
    private readonly Dictionary<int, List<ArbiterSheet>> _rounds = new();  // döner kategori → bütün turlar
    private readonly CheckBox _chkAllRounds = new() { Text = "Döner (Berger) kategorilerde bütün turlar (masa numaraları değişmez)", AutoSize = true };
    private readonly ComboBox _cboPenalty = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 400 };
    private readonly CheckBox _chkPenaltyOnly = new() { Text = "Yalnız ceza formu (eşleştirme kağıtları olmadan)", AutoSize = true };
    private readonly CheckBox _chkTwoPerPage = new() { Text = "Bir sayfaya 2 form (ortadan kesilir)", AutoSize = true };
    private readonly DataGridView _grid = new();
    private readonly RadioButton _rbSeparate = new() { Text = "Her kategori ayrı sayfada", AutoSize = true };
    private readonly RadioButton _rbFit = new() { Text = "Sığdıkça aynı sayfaya (kağıt tasarrufu)", AutoSize = true };
    private readonly RadioButton _rbGroups = new() { Text = "Sayfa gruplarına göre (tablodaki \"Sayfa\" sütunu)", AutoSize = true };
    private readonly ComboBox _cboSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly PictureBox _pic = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(226, 226, 220) };
    private readonly Label _lblPage = new() { AutoSize = false, Width = 120, TextAlign = ContentAlignment.MiddleCenter, Height = 30 };
    private readonly Button _btnPrev = new() { Text = "◀", Width = 40, Height = 30 };
    private readonly Button _btnNext = new() { Text = "▶", Width = 40, Height = 30 };
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(10, 11, 0, 0) };
    private readonly Button _btnPdf = new() { Text = "📄 PDF Kaydet", Width = 140, Height = 40 };
    private readonly Button _btnPrint = new() { Text = "🖨 Önizle ve Yazdır", Width = 190, Height = 40, BackColor = Green, ForeColor = Color.White };
    private ArbiterSheetPrinter? _printer;
    private int _page;
    private int _pending;
    private bool _init = true;

    public ArbiterSheetsForm(ArbiterSheetsContext ctx)
    {
        _ctx = ctx;
        Text = "Hakem Eşleştirme Kağıtları — " + ctx.EventName;
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1100, 760);
        MinimumSize = new Size(820, 560);
        this.FitToScreen(); // küçük ekranda / yüksek ölçekte taşmasın
        BackColor = Color.FromArgb(250, 250, 247);
        BuildLayout();

        foreach (var (label, _) in Sizes) _cboSize.Items.Add(label);
        int si = Array.FindIndex(Sizes, s => Math.Abs(s.Pt - ctx.Config.FontPt) < 0.3);
        _cboSize.SelectedIndex = si >= 0 ? si : 1;
        (ctx.Config.Layout switch { "Separate" => _rbSeparate, "Groups" => _rbGroups, _ => _rbFit }).Checked = true;
        foreach (var (label, _) in Penalties) _cboPenalty.Items.Add(label);
        int pi = Array.FindIndex(Penalties, p => p.Kind.ToString() == ctx.Config.Penalty);
        _cboPenalty.SelectedIndex = pi >= 0 ? pi : 0;
        _chkPenaltyOnly.Checked = ctx.Config.PenaltyOnly;
        _chkTwoPerPage.Checked = ctx.Config.PenaltyTwoPerPage;
        _chkAllRounds.Checked = ctx.Config.AllRounds;
        UpdatePenaltyOnly();
        FillGrid();
        _init = false;

        foreach (var rb in new[] { _rbSeparate, _rbFit, _rbGroups })
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { SaveOptions(); Rebuild(); } };
        _cboSize.SelectedIndexChanged += (_, _) => { SaveOptions(); Rebuild(); };
        _cboPenalty.SelectedIndexChanged += (_, _) => { UpdatePenaltyOnly(); SaveOptions(); Rebuild(); };
        _chkPenaltyOnly.CheckedChanged += (_, _) => { SaveOptions(); Rebuild(); };
        _chkTwoPerPage.CheckedChanged += (_, _) => { SaveOptions(); Rebuild(); };
        _chkAllRounds.CheckedChanged += async (_, _) => { SaveOptions(); await LoadRoundsAsync(); };
        _btnPrev.Click += (_, _) => ShowPage(_page - 1);
        _btnNext.Click += (_, _) => ShowPage(_page + 1);
        _btnPrint.Click += (_, _) => Print();
        _btnPdf.Click += (_, _) => SavePdf();
        Shown += async (_, _) => await LoadAllAsync();
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        var left = new Panel { Dock = DockStyle.Left, Width = 520, Padding = new Padding(14, 12, 8, 8) };
        // Seçenekler kendi içinde kaydırılır: kısa pencerede de hepsine ulaşılsın, tablo ezilmesin.
        var options = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Height = 300 };
        options.Controls.Add(Header("Sayfa düzeni"));
        // Düzen seçenekleri kendi kabında (yazı boyutu kutusuyla aynı grupta olmasınlar)
        var layoutBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        layoutBox.Controls.AddRange(new Control[] { _rbSeparate, _rbFit, _rbGroups });
        options.Controls.Add(layoutBox);
        var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        sizeRow.Controls.Add(new Label { Text = "Yazı boyutu:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark });
        sizeRow.Controls.Add(_cboSize);
        options.Controls.Add(sizeRow);
        _chkAllRounds.Margin = new Padding(0, 8, 0, 0);
        options.Controls.Add(_chkAllRounds);
        options.Controls.Add(Header("Ceza bildirim formu"));
        options.Controls.Add(_cboPenalty);
        _chkPenaltyOnly.Margin = new Padding(0, 4, 0, 0);
        var penaltyRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 4, 0, 0) };
        _chkPenaltyOnly.Margin = new Padding(0, 0, 0, 2);
        _chkTwoPerPage.Margin = new Padding(0);
        penaltyRow.Controls.AddRange(new Control[] { _chkPenaltyOnly, _chkTwoPerPage });
        options.Controls.Add(penaltyRow);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowTemplate.Height = 30;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Bas", HeaderText = "Bas", Width = 44 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kategori", HeaderText = "Kategori", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tur", HeaderText = "Tur", Width = 56 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Masa", HeaderText = "Masa", Width = 64, ReadOnly = true });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Sayfa", HeaderText = "Sayfa", Width = 64 });
        foreach (var c in new[] { "Tur", "Masa", "Sayfa" }) _grid.Columns[c]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["Masa"]!.DefaultCellStyle.ForeColor = Color.Gray;
        _grid.Columns["Tur"]!.ToolTipText = "Varsayılan: son eşleştirilen tur. Başka bir turu basmak için değiştirin.";
        _grid.Columns["Sayfa"]!.ToolTipText = "Aynı numaralı kategoriler aynı kağıda basılır (\"Sayfa gruplarına göre\" düzeninde).";
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellValueChanged += async (_, e) =>
        {
            if (_init || e.RowIndex < 0) return;
            var col = _grid.Columns[e.ColumnIndex].Name;
            var row = _grid.Rows[e.RowIndex];
            if (col == "Tur" && row.Tag is CategoryRef c && int.TryParse(row.Cells["Tur"].Value?.ToString(), out var round) && round > 0
                && _sheets.GetValueOrDefault(c.Tnr)?.Round != round)
                await LoadAsync(row, round);
            else if (col == "Sayfa")
            {
                if (row.Tag is CategoryRef cg && int.TryParse(row.Cells["Sayfa"].Value?.ToString(), out var grp) && grp > 0)
                { _ctx.Config.Groups[cg.Name] = grp; _ctx.SaveConfig(); }
                Rebuild();
            }
            else Rebuild();
        };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 44, ForeColor = Color.DimGray,
            Text = "Basılacak kategorileri işaretleyin. Tur, son eşleştirilen turdur (değiştirilebilir). Yazılar Swiss-Manager çıktısından " +
                   "iridir; her masada sonucun yazılacağı kutu vardır."
        };
        left.Controls.Add(_grid);
        left.Controls.Add(hint);
        left.Controls.Add(options);

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 14, 8) };
        var nav = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.LeftToRight, Padding = new Padding(0, 4, 0, 0) };
        nav.Controls.AddRange(new Control[] { _btnPrev, _lblPage, _btnNext });
        right.Controls.Add(_pic);
        right.Controls.Add(nav);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var bl = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        bl.Controls.Add(_lblStatus);
        var br = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        br.Controls.AddRange(new Control[] { _btnPdf, _btnPrint });
        bottom.Controls.Add(bl);
        bottom.Controls.Add(br);
        foreach (var b in new[] { _btnPdf, _btnPrint, _btnPrev, _btnNext })
        {
            b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand; b.Margin = new Padding(4, 0, 4, 0);
            bool primary = b == _btnPrint;
            if (!primary) b.BackColor = Color.White;
            b.FlatAppearance.BorderColor = primary ? GreenDark : Border;
        }
        _btnPrint.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(bottom);
    }

    private Label Header(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0, 4, 0, 4)
    };

    // ================= Veri =================
    private void FillGrid()
    {
        foreach (var c in _ctx.Categories)
        {
            // Varsayılan: hepsi 1. sayfa grubunda; ayrı kağıda gidecek kategorinin numarası değiştirilir.
            int grp = _ctx.Config.Groups.GetValueOrDefault(c.Name, 1);
            int r = _grid.Rows.Add(true, EventGrouping.ShortCategory(c.Name) is { Length: > 0 } s ? s : c.Name, "…", "…", grp.ToString());
            _grid.Rows[r].Tag = c;
        }
    }

    private async Task LoadAllAsync()
    {
        _pending = _grid.Rows.Count;
        UpdateStatus();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (IsDisposed) return;
            await LoadAsync(row, null);
            _pending--;
            UpdateStatus();
        }
    }

    private async Task LoadAsync(DataGridViewRow row, int? round)
    {
        if (row.Tag is not CategoryRef c) return;
        ArbiterSheet? sheet;
        try { sheet = await _ctx.Load(c, round); }
        catch { sheet = null; }
        if (IsDisposed) return;
        _sheets[c.Tnr] = sheet;
        bool was = _init;
        _init = true;
        row.Cells["Tur"].Value = sheet?.Round.ToString() ?? "—";
        row.Cells["Masa"].Value = sheet is null ? "yok" : sheet.Pairings.Pairings.Count(p => !p.IsBye).ToString();
        if (sheet is null) { row.Cells["Bas"].Value = false; row.DefaultCellStyle.ForeColor = Color.Gray; }
        _init = was;
        if (_chkAllRounds.Checked && IsRoundRobin(sheet)) await LoadRoundsAsync(row);
        else Rebuild();
    }

    private static bool IsRoundRobin(ArbiterSheet? s) => s?.Pairings.System.IsRoundRobin() == true;

    /// <summary>Döner kategorilerin bütün turları (bir kez alınır); Tur hücresi "1–7" olur.</summary>
    private async Task LoadRoundsAsync(DataGridViewRow? only = null)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (only is not null && row != only) continue;
            if (row.Tag is not CategoryRef c || !IsRoundRobin(_sheets.GetValueOrDefault(c.Tnr))) continue;
            if (_chkAllRounds.Checked && !_rounds.ContainsKey(c.Tnr))
            {
                _lblStatus.Text = $"{row.Cells["Kategori"].Value}: bütün turlar alınıyor…";
                List<ArbiterSheet> list;
                try { list = await _ctx.LoadAllRounds(c); }
                catch { list = new(); }
                if (IsDisposed) return;
                if (list.Count > 0) _rounds[c.Tnr] = list;
            }
            bool was = _init;
            _init = true;
            row.Cells["Tur"].Value = _chkAllRounds.Checked && _rounds.TryGetValue(c.Tnr, out var all)
                ? $"{all.Min(s => s.Round)}–{all.Max(s => s.Round)}" : _sheets[c.Tnr]!.Round.ToString();
            row.Cells["Tur"].ReadOnly = _chkAllRounds.Checked && _rounds.ContainsKey(c.Tnr);
            _init = was;
        }
        Rebuild();
    }

    private PenaltyForm PenaltyChoice => Penalties[Math.Max(0, _cboPenalty.SelectedIndex)].Kind;

    private void UpdatePenaltyOnly()
        => _chkPenaltyOnly.Enabled = _chkTwoPerPage.Enabled = PenaltyChoice is PenaltyForm.BlankPage or PenaltyForm.FilledPage;

    private List<ArbiterSheet> Selected()
    {
        var list = new List<ArbiterSheet>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells["Bas"].Value is not true || row.Tag is not CategoryRef c || _sheets.GetValueOrDefault(c.Tnr) is not { } s) continue;
            int grp = int.TryParse(row.Cells["Sayfa"].Value?.ToString(), out var g) && g > 0 ? g : 1;
            var name = row.Cells["Kategori"].Value?.ToString() ?? s.Category;
            if (_chkAllRounds.Checked && _rounds.TryGetValue(c.Tnr, out var all))
                list.AddRange(all.Select(r => r with { Category = name, Group = grp }));
            else
                list.Add(s with { Category = name, Group = grp });
        }
        // Gruplarda: grup numarasına göre (aynı grup içinde tablo sırası korunur)
        return _rbGroups.Checked ? list.Select((s, i) => (s, i)).OrderBy(x => x.s.Group).ThenBy(x => x.i).Select(x => x.s).ToList() : list;
    }

    private SheetLayout LayoutChoice => _rbSeparate.Checked ? SheetLayout.Separate : _rbGroups.Checked ? SheetLayout.Groups : SheetLayout.Fit;
    private float FontPt => Sizes[Math.Max(0, _cboSize.SelectedIndex)].Pt;

    private void SaveOptions()
    {
        if (_init) return;
        _ctx.Config.Layout = LayoutChoice.ToString();
        _ctx.Config.FontPt = FontPt;
        _ctx.Config.Penalty = PenaltyChoice.ToString();
        _ctx.Config.PenaltyOnly = _chkPenaltyOnly.Checked;
        _ctx.Config.PenaltyTwoPerPage = _chkTwoPerPage.Checked;
        _ctx.Config.AllRounds = _chkAllRounds.Checked;
        _ctx.SaveConfig();
    }

    /// <summary>Sayfalamayı yeniden kurar ve önizlemeyi tazeler.</summary>
    private void Rebuild()
    {
        if (_init) return;
        _grid.EndEdit();
        _grid.Columns["Sayfa"]!.Visible = _rbGroups.Checked;
        var sheets = Selected();
        // Boş ceza formu kategori seçilmeden de basılabilir.
        bool blankOnly = PenaltyChoice == PenaltyForm.BlankPage && (_chkPenaltyOnly.Checked || sheets.Count == 0);
        _printer = sheets.Count == 0 && !blankOnly ? null
            : new ArbiterSheetPrinter(sheets, _ctx.EventName, LayoutChoice, FontPt, PenaltyChoice, _chkPenaltyOnly.Checked || sheets.Count == 0,
                                      _chkTwoPerPage.Checked);
        ShowPage(_page);
        UpdateStatus();
    }

    private void ShowPage(int page)
    {
        var old = _pic.Image;
        if (_printer is null)
        {
            _pic.Image = null;
            _lblPage.Text = "—";
        }
        else
        {
            _page = Math.Clamp(page, 0, _printer.PageCount - 1);
            _pic.Image = _printer.RenderPage(_page, 620);
            _lblPage.Text = $"Sayfa {_page + 1} / {_printer.PageCount}";
        }
        old?.Dispose();
        _btnPrev.Enabled = _printer is not null && _page > 0;
        _btnNext.Enabled = _printer is not null && _page < _printer.PageCount - 1;
    }

    private void UpdateStatus()
    {
        var sel = Selected();
        int cats = sel.Select(s => s.Category).Distinct().Count();
        _lblStatus.Text = _pending > 0 ? $"Eşleştirmeler alınıyor… ({_grid.Rows.Count - _pending}/{_grid.Rows.Count})"
            : sel.Count == 0 && _printer is not null ? $"Boş ceza bildirim formu • {_printer.PageCount} sayfa A4"
            : sel.Count == 0 ? "Basılacak kategori yok (eşleştirmesi yayımlanmış kategori seçin)."
            : $"{cats} kategori{(sel.Count > cats ? $" • {sel.Count} tur" : "")} • {sel.Sum(s => s.Pairings.Pairings.Count(p => !p.IsBye))} masa • {_printer?.PageCount ?? 0} sayfa A4";
        _btnPrint.Enabled = _btnPdf.Enabled = _printer is not null && _pending == 0;
    }

    // ================= Çıktı =================
    private void Print()
    {
        if (_printer is null) return;
        if (_printer.PrintWithPreview(this)) _lblStatus.Text = $"✓ {_printer.PageCount} sayfa yazıcıya gönderildi.";
    }

    private void SavePdf()
    {
        if (_printer is null) return;
        Directory.CreateDirectory(_ctx.OutputDir);
        using var dlg = new SaveFileDialog
        {
            Title = "Hakem eşleştirme kağıtlarını PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf",
            InitialDirectory = _ctx.OutputDir, FileName = "hakem_eslestirme.pdf"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _printer.SavePdf(dlg.FileName);
            _lblStatus.Text = "PDF kaydedildi: " + Path.GetFileName(dlg.FileName);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "PDF oluşturulamadı: " + ex.Message, "PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
