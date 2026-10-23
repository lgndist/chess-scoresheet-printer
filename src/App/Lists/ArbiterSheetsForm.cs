using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Lists;

/// <summary>Hakem kağıtları penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record ArbiterSheetsContext(
    string EventName,
    IReadOnlyList<CategoryRef> Categories,
    Func<CategoryRef, int?, Task<ArbiterSheet?>> Load,   // tur null = son eşlenen tur; eşleştirme yoksa null
    ArbiterSheetConfig Config,
    Action SaveConfig,
    string OutputDir);

/// <summary>
/// Hakem eşleştirme kağıtları: hangi kategorilerin basılacağı, tur, sayfa düzeni (her kategori ayrı,
/// sığdıkça aynı sayfa ya da elle sayfa grupları) ve yazı boyutu seçilir; sağda sayfa sayfa canlı önizleme.
/// </summary>
public sealed class ArbiterSheetsForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);
    private static readonly (string Label, float Pt)[] Sizes = { ("Normal", 11f), ("Büyük (önerilen)", 12.5f), ("Çok büyük", 14f) };

    private readonly ArbiterSheetsContext _ctx;
    private readonly Dictionary<int, ArbiterSheet?> _sheets = new();   // kategori tnr → yüklenen kağıt
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
        Size = new Size(1180, 760);
        MinimumSize = new Size(900, 600);
        BackColor = Color.FromArgb(250, 250, 247);
        BuildLayout();

        foreach (var (label, _) in Sizes) _cboSize.Items.Add(label);
        int si = Array.FindIndex(Sizes, s => Math.Abs(s.Pt - ctx.Config.FontPt) < 0.3);
        _cboSize.SelectedIndex = si >= 0 ? si : 1;
        (ctx.Config.Layout switch { "Separate" => _rbSeparate, "Groups" => _rbGroups, _ => _rbFit }).Checked = true;
        FillGrid();
        _init = false;

        foreach (var rb in new[] { _rbSeparate, _rbFit, _rbGroups })
            rb.CheckedChanged += (_, _) => { if (rb.Checked) { SaveOptions(); Rebuild(); } };
        _cboSize.SelectedIndexChanged += (_, _) => { SaveOptions(); Rebuild(); };
        _btnPrev.Click += (_, _) => ShowPage(_page - 1);
        _btnNext.Click += (_, _) => ShowPage(_page + 1);
        _btnPrint.Click += (_, _) => Print();
        _btnPdf.Click += (_, _) => SavePdf();
        Shown += async (_, _) => await LoadAllAsync();
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        var left = new Panel { Dock = DockStyle.Left, Width = 560, Padding = new Padding(14, 12, 8, 8) };
        var options = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 170, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        options.Controls.Add(Header("Sayfa düzeni"));
        // Düzen seçenekleri kendi kabında (yazı boyutu kutusuyla aynı grupta olmasınlar)
        var layoutBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        layoutBox.Controls.AddRange(new Control[] { _rbSeparate, _rbFit, _rbGroups });
        options.Controls.Add(layoutBox);
        var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        sizeRow.Controls.Add(new Label { Text = "Yazı boyutu:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark });
        sizeRow.Controls.Add(_cboSize);
        options.Controls.Add(sizeRow);

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
        Rebuild();
    }

    private List<ArbiterSheet> Selected()
    {
        var list = new List<ArbiterSheet>();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells["Bas"].Value is not true || row.Tag is not CategoryRef c || _sheets.GetValueOrDefault(c.Tnr) is not { } s) continue;
            int grp = int.TryParse(row.Cells["Sayfa"].Value?.ToString(), out var g) && g > 0 ? g : 1;
            list.Add(s with { Category = row.Cells["Kategori"].Value?.ToString() ?? s.Category, Group = grp });
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
        _ctx.SaveConfig();
    }

    /// <summary>Sayfalamayı yeniden kurar ve önizlemeyi tazeler.</summary>
    private void Rebuild()
    {
        if (_init) return;
        _grid.EndEdit();
        _grid.Columns["Sayfa"]!.Visible = _rbGroups.Checked;
        var sheets = Selected();
        _printer = sheets.Count == 0 ? null : new ArbiterSheetPrinter(sheets, _ctx.EventName, LayoutChoice, FontPt);
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
        _lblStatus.Text = _pending > 0 ? $"Eşleştirmeler alınıyor… ({_grid.Rows.Count - _pending}/{_grid.Rows.Count})"
            : sel.Count == 0 ? "Basılacak kategori yok (eşleştirmesi yayımlanmış kategori seçin)."
            : $"{sel.Count} kategori • {sel.Sum(s => s.Pairings.Pairings.Count(p => !p.IsBye))} masa • {_printer?.PageCount ?? 0} sayfa A4";
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
