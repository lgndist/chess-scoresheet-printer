using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Badges;

/// <summary>Yaka kartı penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record BadgesContext(
    string EventName,
    Func<Task<IReadOnlyDictionary<string, string>>> LoadInfo,
    AppConfig Config,
    Action SaveConfig,
    string BaseDir,
    string OutputDir);

/// <summary>
/// Hakem yaka kartları: görevliler chess-results'tan gelir (direktör, başhakem, yardımcı, hakemler);
/// unvan FIDE unvanından (IA/FA/NA) önerilir, TSF derecesi elle seçilebilir ve hatırlanır.
/// </summary>
public sealed class BadgesForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);

    private readonly BadgesContext _ctx;
    private BadgeConfig Cfg => _ctx.Config.Badges;

    private readonly PictureBox _picLogo = new() { Width = 120, Height = 80, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
    private readonly Button _btnLogo = new() { Text = "🖼 Logo seç…", AutoSize = true, Height = 30 };
    private readonly Button _btnLogoClear = new() { Text = "Kaldır", AutoSize = true, Height = 30 };
    private readonly ComboBox _cboSize = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly CheckBox _chkSimpleRoles = new() { Text = "Görevi sade yaz: Başhakem / Hakem", AutoSize = true };
    private static readonly string[] Sizes = { "85x54", "90x60", "95x135" };
    private readonly CheckBox _chkCrop = new() { Text = "Kesim (crop) işaretleri", AutoSize = true };
    private readonly CheckBox _chkTsfLogo = new() { Text = "Logo seçilmezse TSF il logosu", AutoSize = true };
    private Image? _tsfLogo;   // otomatik il logosu (özel logo yokken)
    private readonly PictureBox _picPreview = new() { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new();
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _btnPdf = new() { Text = "📄 PDF Kaydet", Width = 150, Height = 40 };
    private readonly Button _btnPrint = new() { Text = "🖨 Önizle ve Yazdır", Width = 200, Height = 40, BackColor = Green, ForeColor = Color.White };
    private readonly Button _btnAll = new() { Text = "Tümünü seç", AutoSize = true, Height = 40 };
    private readonly Button _btnNone = new() { Text = "Hiçbiri", AutoSize = true, Height = 40 };
    private bool _loading;

    public BadgesForm(BadgesContext ctx)
    {
        _ctx = ctx;
        Text = "Hakem Yaka Kartları — " + EventGrouping.BaseName(ctx.EventName);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1080, 700);
        MinimumSize = new Size(900, 580);
        BackColor = Color.FromArgb(250, 250, 247);

        BuildLayout();
        _cboSize.Items.AddRange(new object[] { "85 × 54 mm yatay (2×5 / sayfa)", "90 × 60 mm yatay (2×4 / sayfa)", "95 × 135 mm dikey, büyük (2×2 / sayfa)" });
        _cboSize.SelectedIndex = Math.Max(0, Array.IndexOf(Sizes, Cfg.Size));
        _chkSimpleRoles.Checked = Cfg.SimpleRoles;
        _chkSimpleRoles.CheckedChanged += (_, _) => { Cfg.SimpleRoles = _chkSimpleRoles.Checked; _ctx.SaveConfig(); UpdatePreview(); };
        _chkCrop.Checked = Cfg.CropMarks;
        _cboSize.SelectedIndexChanged += (_, _) => { Cfg.Size = Sizes[Math.Max(0, _cboSize.SelectedIndex)]; _ctx.SaveConfig(); UpdateSummary(); UpdatePreview(); };
        _chkCrop.CheckedChanged += (_, _) => { Cfg.CropMarks = _chkCrop.Checked; _ctx.SaveConfig(); };
        _chkTsfLogo.Checked = Cfg.AutoTsfLogo;
        _chkTsfLogo.CheckedChanged += (_, _) => { Cfg.AutoTsfLogo = _chkTsfLogo.Checked; _ctx.SaveConfig(); LoadLogo(); UpdatePreview(); };
        new ToolTip().SetToolTip(_chkTsfLogo, Branding.OfficialLogos.Caption(_ctx.Config.LogoProvince()) + " logosu (alt yayında il adı).");
        FormClosed += (_, _) => _tsfLogo?.Dispose();
        LoadLogo();

        _btnLogo.Click += (_, _) => ChooseLogo();
        _btnLogoClear.Click += (_, _) => { Cfg.LogoPath = null; _ctx.SaveConfig(); LoadLogo(); UpdatePreview(); };
        _btnAll.Click += (_, _) => SetAll(true);
        _btnNone.Click += (_, _) => SetAll(false);
        _btnPrint.Click += (_, _) => Print();
        _btnPdf.Click += (_, _) => SavePdf();
        bool silent = PrintRouter.IsSilent;
        _btnPrint.Text = silent ? "🖨 Yazdır" : "🖨 Önizle ve Yazdır";
        Shown += async (_, _) => await LoadArbitersAsync();
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        var leftHost = new Panel { Dock = DockStyle.Left, Width = 320 };
        var left = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(14, 12, 8, 0) };
        left.Controls.Add(Header("Resmi logo"));
        var logoRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var logoBtns = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(8, 0, 0, 0) };
        logoBtns.Controls.AddRange(new Control[] { _btnLogo, _btnLogoClear });
        logoRow.Controls.AddRange(new Control[] { _picLogo, logoBtns });
        left.Controls.Add(logoRow);
        left.Controls.Add(_chkTsfLogo);
        left.Controls.Add(Header("Kart ölçüsü"));
        left.Controls.Add(_cboSize);
        left.Controls.Add(_chkCrop);
        left.Controls.Add(Header("Görev yazımı"));
        left.Controls.Add(_chkSimpleRoles);
        left.Controls.Add(Header("Unvan renkleri"));
        foreach (var g in Arbiters.Grades)
        {
            var c = CardLayout.ParseColor(Arbiters.ColorFor(g), Color.Gray);
            left.Controls.Add(new Label
            {
                Text = "  " + g, AutoSize = false, Width = 200, Height = 20, BackColor = c, ForeColor = CardLayout.TextColorFor(c),
                Font = new Font(Font, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 1, 0, 1)
            });
        }
        left.Controls.Add(Header("Önizleme"));
        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 10, 10) };
        previewHost.Controls.Add(_picPreview);
        leftHost.Controls.Add(previewHost);
        leftHost.Controls.Add(left);

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = true;               // listede olmayan görevli elle eklenebilir
        _grid.AllowUserToDeleteRows = true;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowTemplate.Height = 32;
        _grid.EditMode = DataGridViewEditMode.EditOnEnter;
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Bas", HeaderText = "Bas", Width = 44 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ad", HeaderText = "Ad Soyad", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        var role = new DataGridViewComboBoxColumn { Name = "Gorev", HeaderText = "Görev", Width = 170, FlatStyle = FlatStyle.Flat };
        role.Items.AddRange(Arbiters.Roles);
        _grid.Columns.Add(role);
        var grade = new DataGridViewComboBoxColumn { Name = "Unvan", HeaderText = "Unvan (renk)", Width = 160, FlatStyle = FlatStyle.Flat };
        grade.Items.AddRange(Arbiters.Grades);
        _grid.Columns.Add(grade);
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Foto", HeaderText = "Fotoğraf", Width = 110, FlatStyle = FlatStyle.Flat });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Adet", HeaderText = "Adet", Width = 60 });
        _grid.Columns["Adet"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.DefaultValuesNeeded += (_, e) =>
        {
            e.Row.Cells["Bas"].Value = true;
            e.Row.Cells["Gorev"].Value = "Hakem";
            e.Row.Cells["Unvan"].Value = Arbiters.Il;
            e.Row.Cells["Foto"].Value = "Seç…";
            e.Row.Cells["Adet"].Value = "1";
        };
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Foto") PickPhoto(e.RowIndex); };
        _grid.CellValueChanged += (_, e) =>
        {
            if (_loading || e.RowIndex < 0) return;
            var row = _grid.Rows[e.RowIndex];
            if (_grid.Columns[e.ColumnIndex].Name == "Unvan" && row.Cells["Ad"].Value is string n && n.Length > 0)
            {
                Cfg.Grades[Key(n)] = row.Cells["Unvan"].Value?.ToString() ?? Arbiters.Il; // elle seçilen derece hatırlanır
                _ctx.SaveConfig();
            }
            UpdateSummary();
            UpdatePreview();
        };
        _grid.DataError += (_, e) => e.ThrowException = false;
        _grid.SelectionChanged += (_, _) => UpdatePreview();

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 14, 8) };
        gridPanel.Controls.Add(_grid);
        gridPanel.Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 44, ForeColor = Color.DimGray,
            Text = "Görevliler chess-results'tan gelir. Unvan, FIDE unvanından (IA/FA/NA) önerilir; TSF derecesini seçebilirsiniz (hatırlanır). " +
                   "Listede olmayan görevliyi en alttaki boş satıra yazarak ekleyin."
        });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var bl = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        bl.Controls.AddRange(new Control[] { _btnAll, _btnNone, _lblStatus });
        _lblStatus.Margin = new Padding(10, 11, 0, 0);
        var br = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        br.Controls.AddRange(new Control[] { _btnPdf, _btnPrint });
        bottom.Controls.Add(bl);
        bottom.Controls.Add(br);
        foreach (var b in new[] { _btnLogo, _btnLogoClear, _btnPdf, _btnPrint, _btnAll, _btnNone }) Style(b);
        _btnPrint.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);

        Controls.Add(gridPanel);
        Controls.Add(leftHost);
        Controls.Add(bottom);
    }

    private Label Header(string text) => new()
    {
        Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0, 10, 0, 4)
    };

    private static void Style(Button b)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.Cursor = Cursors.Hand;
        b.Margin = new Padding(4, 0, 4, 2);
        bool primary = b.BackColor == Green;
        if (!primary) b.BackColor = Color.White;
        b.FlatAppearance.BorderColor = primary ? GreenDark : Border;
        b.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(134, 168, 100) : Color.FromArgb(236, 241, 231);
    }

    private static string Key(string name) => EventGrouping.Fold(name).Replace(" ", "");

    // ================= Veri =================
    private async Task LoadArbitersAsync()
    {
        _lblStatus.Text = "Görevliler chess-results'tan alınıyor…";
        List<ArbiterEntry> list;
        try { list = Arbiters.FromInfo(await _ctx.LoadInfo()); }
        catch { list = new(); _lblStatus.Text = "Görevliler alınamadı (çevrimdışı?) — satırları elle ekleyebilirsiniz."; }
        if (IsDisposed) return;
        _loading = true;
        foreach (var a in list)
        {
            var grade = Cfg.Grades.TryGetValue(Key(a.Name), out var g) ? g : Arbiters.GradeFromTitle(a.FideTitle) ?? Arbiters.Il;
            var photo = Cfg.Photos.TryGetValue(Key(a.Name), out var p) && File.Exists(p) ? p : null;
            int i = _grid.Rows.Add(true, a.Name, a.Role, grade, photo is null ? "Seç…" : "✓ Değiştir", "1");
            _grid.Rows[i].Cells["Foto"].Tag = photo;
        }
        _loading = false;
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
        UpdateSummary();
        UpdatePreview();
    }

    private IEnumerable<BadgeSpec> Selected()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.IsNewRow || row.Cells["Bas"].Value is not true) continue;
            var spec = SpecOf(row);
            if (spec is not null && spec.Copies > 0) yield return spec;
        }
    }

    private BadgeSpec? SpecOf(DataGridViewRow row)
    {
        var name = row.Cells["Ad"].Value?.ToString()?.Trim() ?? "";
        if (name.Length == 0) return null;
        var grade = row.Cells["Unvan"].Value?.ToString() ?? Arbiters.Il;
        int.TryParse(row.Cells["Adet"].Value?.ToString(), out var copies);
        // Karta basılan görev: sade yazımda "Başhakem" ya da "Hakem" (tablodaki görev değişmez).
        return new BadgeSpec(name, Arbiters.PrintedRole(row.Cells["Gorev"].Value?.ToString(), Cfg.SimpleRoles), grade,
                             CardLayout.ParseColor(Arbiters.ColorFor(grade), Color.Gray), row.Cells["Foto"].Tag as string, copies);
    }

    private void UpdateSummary()
    {
        var p = NewPrinter(Selected().ToList());
        _lblStatus.Text = p.CardCount == 0 ? "Basılacak kart seçilmedi." : $"{p.CardCount} kart • {p.PageCount} sayfa A4 ({p.Cols}×{p.Rows})";
        _btnPrint.Enabled = _btnPdf.Enabled = p.CardCount > 0;
    }

    private void SetAll(bool on)
    {
        foreach (DataGridViewRow r in _grid.Rows) if (!r.IsNewRow) r.Cells["Bas"].Value = on;
        UpdateSummary();
    }

    private void PickPhoto(int rowIndex)
    {
        var row = _grid.Rows[rowIndex];
        var name = row.Cells["Ad"].Value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(name)) { _lblStatus.Text = "Önce ad soyad yazın."; return; }
        using var dlg = new OpenFileDialog { Title = $"{name} — vesikalık fotoğraf", Filter = "Resim (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using (Image.FromFile(dlg.FileName)) { } // geçerli resim mi
            var dir = Path.Combine(_ctx.BaseDir, "badges", "photos");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Key(name) + Path.GetExtension(dlg.FileName).ToLowerInvariant());
            File.Copy(dlg.FileName, dest, overwrite: true);
            Cfg.Photos[Key(name)] = dest;
            _ctx.SaveConfig();
            row.Cells["Foto"].Tag = dest;
            row.Cells["Foto"].Value = "✓ Değiştir";
            UpdatePreview();
        }
        catch (Exception ex) { MessageBox.Show(this, "Fotoğraf açılamadı: " + ex.Message, "Fotoğraf", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void ChooseLogo()
    {
        using var dlg = new OpenFileDialog { Title = "Resmi logo seç", Filter = "Resim (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using (Image.FromFile(dlg.FileName)) { }
            var dir = Path.Combine(_ctx.BaseDir, "badges");
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, "logo" + Path.GetExtension(dlg.FileName).ToLowerInvariant());
            File.Copy(dlg.FileName, dest, overwrite: true);
            Cfg.LogoPath = dest;
            _ctx.SaveConfig();
            LoadLogo();
            UpdatePreview();
        }
        catch (Exception ex) { MessageBox.Show(this, "Resim açılamadı: " + ex.Message, "Logo", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    /// <summary>Seçilen logo; yoksa (seçenek açıksa) turnuva ilinin TSF logosu.</summary>
    private void LoadLogo()
    {
        if (_picLogo.Image is { } old && old != _tsfLogo) old.Dispose();
        _picLogo.Image = null;
        if (!string.IsNullOrWhiteSpace(Cfg.LogoPath) && File.Exists(Cfg.LogoPath))
            try { using var fs = File.OpenRead(Cfg.LogoPath); using var img = Image.FromStream(fs); _picLogo.Image = new Bitmap(img); }
            catch { }
        if (_picLogo.Image is null && Cfg.AutoTsfLogo)
            try { _picLogo.Image = _tsfLogo ??= Branding.OfficialLogos.Tsf(_ctx.Config.LogoProvince(), 600); }
            catch { }
    }

    /// <summary>Basılacak yedek logo: özel logo yokken TSF il logosu.</summary>
    private Image? FallbackLogo => Cfg.AutoTsfLogo ? _tsfLogo : null;

    private void UpdatePreview()
    {
        var row = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : null;
        var spec = row is null || row.IsNewRow ? null : SpecOf(row);
        spec ??= new BadgeSpec("Ad SOYAD", "Hakem", Arbiters.Il, CardLayout.ParseColor(Arbiters.ColorFor(Arbiters.Il), Color.Gray), null, 1);
        var p = NewPrinter(new[] { spec });
        const float scale = 2f; // önizleme büyütmesi
        var bmp = new Bitmap((int)(p.CardW * scale) + 2, (int)(p.CardH * scale) + 2);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PageUnit = GraphicsUnit.Point;
            float k = scale * 72f / bmp.HorizontalResolution;
            g.ScaleTransform(k, k);
            Image? photo = null;
            try { if (spec.PhotoPath is not null) { using var fs = File.OpenRead(spec.PhotoPath); using var src = Image.FromStream(fs); photo = new Bitmap(src); } } catch { }
            BadgePrinter.DrawCard(g, new RectangleF(0, 0, p.CardW, p.CardH), spec, _picLogo.Image, photo, EventGrouping.BaseName(_ctx.EventName), CardFonts.Default);
            photo?.Dispose();
            using var pen = new Pen(Color.FromArgb(180, 180, 180), 0.5f);
            g.DrawRectangle(pen, 0, 0, p.CardW, p.CardH);
        }
        var old = _picPreview.Image;
        _picPreview.Image = bmp;
        old?.Dispose();
    }

    // ================= Çıktı =================
    private BadgePrinter NewPrinter(IEnumerable<BadgeSpec> specs)
        => new(specs, Cfg.LogoPath, EventGrouping.BaseName(_ctx.EventName), Cfg.Size, Cfg.CropMarks, null, FallbackLogo);

    private BadgePrinter? Printer()
    {
        _grid.EndEdit();
        var p = NewPrinter(Selected().ToList());
        if (p.CardCount == 0) { _lblStatus.Text = "Basılacak kart seçilmedi."; return null; }
        return p;
    }

    private void Print()
    {
        var p = Printer();
        if (p is null) return;
        if (p.PrintWithPreview(this)) _lblStatus.Text = $"{p.CardCount} yaka kartı ({p.PageCount} sayfa) yazıcıya gönderildi.";
    }

    private void SavePdf()
    {
        var p = Printer();
        if (p is null) return;
        Directory.CreateDirectory(_ctx.OutputDir);
        using var dlg = new SaveFileDialog { Title = "Yaka kartlarını PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf", InitialDirectory = _ctx.OutputDir, FileName = "hakem_yaka_kartlari.pdf" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            p.SavePdf(dlg.FileName);
            _lblStatus.Text = "PDF kaydedildi: " + Path.GetFileName(dlg.FileName);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex) { MessageBox.Show(this, "PDF oluşturulamadı: " + ex.Message, "PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        finally { UseWaitCursor = false; }
    }
}
