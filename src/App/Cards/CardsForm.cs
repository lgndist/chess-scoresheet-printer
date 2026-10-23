using System.Globalization;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Kartlar penceresinin ana pencereden aldığı bağlam.</summary>
public sealed record CardsContext(
    string EventName,
    IReadOnlyList<CategoryRef> Categories,
    Func<CategoryRef, Task<(int Boards, string Source)>> BoardCount,
    AppConfig Config,
    Action SaveConfig,
    string BaseDir,
    string OutputDir);

/// <summary>
/// Kategori masa kartları: her kategorinin tek bir (kalıcı) tema rengi vardır; kart stili ya beyaz
/// zeminde tema renginde yazı ya da tema renginde zeminde beyaz yazıdır. Üstte turnuva afişi (oranı
/// korunur, kırpma aracıyla yuvaya göre kırpılabilir), altta büyük kategori adı. Adet varsayılan
/// olarak kategorideki masa sayısıdır.
/// </summary>
public sealed class CardsForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);
    private static readonly CultureInfo Tr = new("tr-TR");

    private readonly CardsContext _ctx;
    private CardConfig Cfg => _ctx.Config.Cards;
    private readonly HashSet<int> _countEdited = new();   // kullanıcının adedini elle değiştirdiği satırlar
    private int _countsPending;                           // masa sayısı henüz gelmemiş kategori sayısı
    private bool _initializing = true;

    private readonly PictureBox _picLogo = new() { Width = 270, Height = 120, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
    private readonly Button _btnLogo = new() { Text = "🖼 Afiş seç…", AutoSize = true, Height = 32 };
    private readonly Button _btnCrop = new() { Text = "✂ Kırp…", AutoSize = true, Height = 32 };
    private readonly Button _btnLogoClear = new() { Text = "Kaldır", AutoSize = true, Height = 32 };
    private readonly PictureBox _picPreview = new() { SizeMode = PictureBoxSizeMode.Zoom, Dock = DockStyle.Fill };
    private readonly DataGridView _grid = new();
    private readonly Label _lblStatus = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly Button _btnPdf = new() { Text = "📄 PDF Kaydet", Width = 150, Height = 40 };
    private readonly Button _btnPrint = new() { Text = "🖨 Önizle ve Yazdır", Width = 200, Height = 40, BackColor = Green, ForeColor = Color.White };
    private readonly Button _btnAll = new() { Text = "Tümünü seç", AutoSize = true, Height = 40 };
    private readonly Button _btnNone = new() { Text = "Hiçbiri", AutoSize = true, Height = 40 };
    private readonly ComboBox _cboFont = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly RadioButton _rbLandscape = new() { Text = "Yatay", AutoSize = true };
    private readonly RadioButton _rbPortrait = new() { Text = "Dikey", AutoSize = true };
    private readonly CheckBox _chkFullWidth = new() { Text = "Afişi tam genişliğe yay (kenardan kenara)", AutoSize = true };
    private readonly RadioButton _rbTextColored = new() { Text = "Yazı renkli / beyaz zemin", AutoSize = true };
    private readonly RadioButton _rbFilled = new() { Text = "Zemin renkli / beyaz yazı", AutoSize = true };

    public CardsForm(CardsContext ctx)
    {
        _ctx = ctx;
        Text = "Kategori Masa Kartları — " + EventGrouping.BaseName(ctx.EventName);
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1000, 720);
        MinimumSize = new Size(820, 600);
        BackColor = Color.FromArgb(250, 250, 247);

        BuildLayout();

        // Kalıcı seçimler önizleme çizilmeden ÖNCE yüklenir: aksi hâlde ilk çizim yön/stil seçilmemişken
        // (ikisi de kapalı → dikey) yapılıyordu ve yön ancak seçim değiştirilince düzeliyordu.
        FillFonts(Cfg.FontName);
        _chkFullWidth.Checked = Cfg.LogoFullWidth;
        (Cfg.Landscape ? _rbLandscape : _rbPortrait).Checked = true;
        (Cfg.FilledBand ? _rbFilled : _rbTextColored).Checked = true;

        LoadLogo();
        FillGrid();
        UpdatePrintButton();
        _initializing = false;
        UpdatePreview();

        _cboFont.SelectedIndexChanged += (_, _) => { Cfg.FontName = (string)_cboFont.SelectedItem!; _ctx.SaveConfig(); UpdatePreview(); };
        _rbLandscape.CheckedChanged += (_, _) => { Cfg.Landscape = _rbLandscape.Checked; _ctx.SaveConfig(); UpdatePreview(); };
        _rbFilled.CheckedChanged += (_, _) => { Cfg.FilledBand = _rbFilled.Checked; _ctx.SaveConfig(); UpdatePreview(); };
        _chkFullWidth.CheckedChanged += (_, _) => { Cfg.LogoFullWidth = _chkFullWidth.Checked; _ctx.SaveConfig(); UpdatePreview(); };
        _btnLogo.Click += (_, _) => ChooseLogo();
        _btnCrop.Click += (_, _) => CropLogo();
        _btnLogoClear.Click += (_, _) => { Cfg.LogoPath = null; Cfg.LogoSourcePath = null; _ctx.SaveConfig(); LoadLogo(); UpdatePreview(); };
        _btnAll.Click += (_, _) => SetAll(true);
        _btnNone.Click += (_, _) => SetAll(false);
        _btnPrint.Click += (_, _) => Print();
        _btnPdf.Click += (_, _) => SavePdf();
        Shown += async (_, _) =>
        {
            try { await LoadCountsAsync(); }
            catch (Exception ex) { _countsPending = 0; _lblStatus.Text = "Masa sayıları alınamadı: " + ex.Message; }
        };
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        // Sol sütun: üstte afiş ve kart ayarları, altta kalan yüksekliği dolduran kart önizlemesi.
        var leftHost = new Panel { Dock = DockStyle.Left, Width = 310 };
        var left = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Padding = new Padding(14, 12, 8, 0)
        };
        left.Controls.Add(Header("Turnuva afişi / logosu", 0));
        left.Controls.Add(_picLogo);
        var logoButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        logoButtons.Controls.AddRange(new Control[] { _btnLogo, _btnCrop, _btnLogoClear });
        left.Controls.Add(logoButtons);
        left.Controls.Add(_chkFullWidth);
        left.Controls.Add(new Label
        {
            AutoSize = false, Width = 280, Height = 54, ForeColor = Color.Gray,
            Text = "Oranı korunarak basılır; \"Kırp\" ile yuvaya göre kırpabilirsiniz. Seçilmezse turnuva adı yazılır."
        });
        var fontRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
        fontRow.Controls.Add(new Label { Text = "Yazı tipi:", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        fontRow.Controls.Add(_cboFont);
        left.Controls.Add(fontRow);
        var orientRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        orientRow.Controls.Add(new Label { Text = "Kağıt yönü:", AutoSize = true, Margin = new Padding(0, 5, 6, 0) });
        orientRow.Controls.AddRange(new Control[] { _rbLandscape, _rbPortrait });
        left.Controls.Add(orientRow);
        left.Controls.Add(Header("Kart stili (tema rengiyle)", 6));
        // Stil seçenekleri ayrı bir kapta: yön düğmeleriyle aynı grupta olmasınlar.
        var styleBox = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0) };
        styleBox.Controls.AddRange(new Control[] { _rbTextColored, _rbFilled });
        left.Controls.Add(styleBox);
        left.Controls.Add(Header("Önizleme (seçili kategori)", 8));
        var previewHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 8, 10) };
        _picPreview.BackColor = BackColor;
        previewHost.Controls.Add(_picPreview);
        leftHost.Controls.Add(previewHost); // Fill önce
        leftHost.Controls.Add(left);        // Top sonra

        _grid.Dock = DockStyle.Fill;
        _grid.AllowUserToAddRows = _grid.AllowUserToDeleteRows = _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.BorderStyle = BorderStyle.None;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.RowTemplate.Height = 34;
        _grid.GridColor = Color.FromArgb(230, 230, 222);
        _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Bas", HeaderText = "Bas", Width = 44 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kategori", HeaderText = "Kartta yazacak ad", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Renk", HeaderText = "Tema rengi", Width = 120, FlatStyle = FlatStyle.Flat });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Adet", HeaderText = "Adet", Width = 70 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Masa", HeaderText = "Masa sayısı", Width = 100, ReadOnly = true });
        _grid.Columns["Adet"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["Masa"]!.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns["Masa"]!.DefaultCellStyle.ForeColor = Color.Gray;
        _grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _grid.CellContentClick += (_, e) => { if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == "Renk") PickColor(e.RowIndex); };
        _grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0) return;
            var col = _grid.Columns[e.ColumnIndex].Name;
            if (col == "Adet") _countEdited.Add(e.RowIndex);
            UpdateSummary();
            if (col == "Kategori") UpdatePreview(); // adet/masa sayısı kartın görünüşünü değiştirmez
        };
        _grid.CellValidating += (_, e) =>
        {
            if (_grid.Columns[e.ColumnIndex].Name != "Adet") return;
            if (!int.TryParse(e.FormattedValue?.ToString(), out var n) || n < 0 || n > 500)
            {
                e.Cancel = true;
                _lblStatus.Text = "Adet 0–500 arası bir sayı olmalı.";
            }
        };
        _grid.SelectionChanged += (_, _) => UpdatePreview();

        var gridPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 12, 14, 8) };
        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 62, ForeColor = Color.DimGray,
            Text = "Adet, TSF kayıt listesindeki sporcu sayısına göre (masa = sporcu ÷ 2) önerilir; kayıt yoksa chess-results'taki masa sayısı (yeniden basım için değiştirebilirsiniz). " +
                   "Her kategoriye otomatik ayrı bir tema rengi atanır ve hatırlanır; değiştirmek için renge tıklayın."
        };
        gridPanel.Controls.Add(_grid);
        gridPanel.Controls.Add(hint);

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var bl = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false };
        bl.Controls.AddRange(new Control[] { _btnAll, _btnNone, _lblStatus });
        _lblStatus.Margin = new Padding(10, 11, 0, 0);
        var br = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        br.Controls.AddRange(new Control[] { _btnPdf, _btnPrint });
        bottom.Controls.Add(bl);
        bottom.Controls.Add(br);

        foreach (var b in new[] { _btnLogo, _btnCrop, _btnLogoClear, _btnPdf, _btnPrint, _btnAll, _btnNone }) Style(b);
        _btnPrint.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);

        Controls.Add(gridPanel);
        Controls.Add(leftHost);
        Controls.Add(bottom);
    }

    private Label Header(string text, int top) => new()
    {
        Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0, top, 0, 4)
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

    private void UpdatePrintButton()
    {
        bool silent = Overlay.PrintRouter.IsSilent;
        _btnPrint.Text = silent ? "🖨 Yazdır" : "🖨 Önizle ve Yazdır";
        new ToolTip().SetToolTip(_btnPrint, silent
            ? $"Doğrudan yazdır açık: önizlemesiz “{Overlay.PrintRouter.TargetName}” yazıcısına gönderilir (Ayarlar → Yazıcı)."
            : "Önizleme açılır; oradan yazıcıya gönderilir.");
    }

    // ================= Veri =================
    private void FillGrid()
    {
        var tnrs = _ctx.Categories.Select(c => c.Tnr).ToList();
        bool assigned = false;
        foreach (var c in _ctx.Categories)
        {
            assigned |= !Cfg.CategoryColors.ContainsKey(c.Tnr.ToString());
            var color = CardLayout.ParseColor(Cfg.ColorFor(c.Tnr, tnrs), Color.Gray);
            var label = EventGrouping.ShortCategory(c.Name).ToUpper(Tr);
            int i = _grid.Rows.Add(true, label, "", "…", "…");
            var row = _grid.Rows[i];
            row.Tag = c;
            SetColorCell(row, color);
        }
        if (assigned) _ctx.SaveConfig(); // yeni atanan renkler kalıcı olsun
        if (_grid.Rows.Count > 0) _grid.Rows[0].Selected = true;
        UpdateSummary();
    }

    private static void SetColorCell(DataGridViewRow row, Color color)
    {
        var cell = row.Cells["Renk"];
        cell.Value = CardLayout.ToHex(color);
        cell.Style.BackColor = cell.Style.SelectionBackColor = color;
        cell.Style.ForeColor = cell.Style.SelectionForeColor = CardLayout.TextColorFor(color);
    }

    private async Task LoadCountsAsync()
    {
        _countsPending = _grid.Rows.Count;
        UpdateSummary();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (IsDisposed) return;
            if (row.Tag is not CategoryRef c) { _countsPending--; continue; }
            int n;
            string source;
            try { (n, source) = await _ctx.BoardCount(c); }
            catch { n = 0; source = "Masa sayısı bulunamadı"; }
            if (IsDisposed) return;
            // Kayıt listesinde tek sporculu kategori: masa 0 (sporcu BAY); "?" yalnız sayı bulunamadıysa.
            row.Cells["Masa"].Value = n > 0 || source.StartsWith("TSF") ? n.ToString() : "?";
            row.Cells["Masa"].ToolTipText = source;
            if (!_countEdited.Contains(row.Index))
            {
                row.Cells["Adet"].Value = Math.Max(1, n).ToString();
                _countEdited.Remove(row.Index); // programın yazdığı değer "elle" sayılmasın
            }
            _countsPending--;
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var cards = Selected().ToList();
        _lblStatus.Text = _countsPending > 0
            ? $"Masa sayıları hesaplanıyor… ({_grid.Rows.Count - _countsPending}/{_grid.Rows.Count})"
            : cards.Count == 0 ? "Basılacak kart seçilmedi." : $"{cards.Count} kategori • toplam {cards.Sum(c => c.Copies)} kart (A4)";
        _btnPrint.Enabled = _btnPdf.Enabled = cards.Sum(c => c.Copies) > 0;
    }

    private IEnumerable<CardSpec> Selected()
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.Cells["Bas"].Value is not true) continue;
            var name = row.Cells["Kategori"].Value?.ToString()?.Trim() ?? "";
            int.TryParse(row.Cells["Adet"].Value?.ToString(), out var copies);
            var color = CardLayout.ParseColor(row.Cells["Renk"].Value?.ToString(), Color.Gray);
            if (name.Length > 0 && copies > 0) yield return new CardSpec(name, color, copies);
        }
    }

    private void SetAll(bool on)
    {
        foreach (DataGridViewRow r in _grid.Rows) r.Cells["Bas"].Value = on;
        UpdateSummary();
    }

    private void PickColor(int rowIndex)
    {
        var row = _grid.Rows[rowIndex];
        if (row.Tag is not CategoryRef c) return;
        using var dlg = new ColorDialog
        {
            FullOpen = true, AnyColor = true,
            Color = CardLayout.ParseColor(row.Cells["Renk"].Value?.ToString(), Color.Gray),
            CustomColors = CardConfig.Palette.Select(h => { var k = ColorTranslator.FromHtml(h); return k.R | (k.G << 8) | (k.B << 16); }).ToArray()
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        SetColorCell(row, dlg.Color);
        Cfg.CategoryColors[c.Tnr.ToString()] = CardLayout.ToHex(dlg.Color);
        _ctx.SaveConfig();
        UpdatePreview();
    }

    // ================= Afiş / logo =================
    private string CardsDir()
    {
        var dir = Path.Combine(_ctx.BaseDir, "cards");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void ChooseLogo()
    {
        using var dlg = new OpenFileDialog { Title = "Turnuva afişi / logosu seç", Filter = "Resim (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using (Image.FromFile(dlg.FileName)) { } // geçerli resim mi
            // Asıl görselin kopyası exe yanında tutulur: dosya taşınsa da yeniden kırpılabilsin.
            var src = Path.Combine(CardsDir(), "afis_asil_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(dlg.FileName).ToLowerInvariant());
            File.Copy(dlg.FileName, src, overwrite: true);
            Cfg.LogoSourcePath = src;
            if (!CropLogo()) { Cfg.LogoPath = src; _ctx.SaveConfig(); LoadLogo(); UpdatePreview(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Resim açılamadı: " + ex.Message, "Afiş", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Asıl afişi kırpma aracında açar; kırpılan görsel kartta kullanılır. false = vazgeçildi.</summary>
    private bool CropLogo()
    {
        var srcPath = Cfg.LogoSourcePath is { } s && File.Exists(s) ? s : Cfg.LogoPath;
        if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath))
        {
            _lblStatus.Text = "Önce bir afiş seçin.";
            return false;
        }
        using var src = LoadBitmap(srcPath);
        if (src is null) return false;
        var slot = new CardLayout(_rbLandscape.Checked);
        using var crop = new CropForm(src, _chkFullWidth.Checked ? slot.HeaderAspect : slot.LogoAspect);
        if (crop.ShowDialog(this) != DialogResult.OK || crop.Result is null) return false;
        using var result = crop.Result;
        var dest = Path.Combine(CardsDir(), "afis_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".png");
        result.Save(dest, System.Drawing.Imaging.ImageFormat.Png);
        Cfg.LogoPath = dest;
        _ctx.SaveConfig();
        LoadLogo();
        UpdatePreview();
        return true;
    }

    // ================= Yazı tipleri =================
    private void FillFonts(string? select)
    {
        _cboFont.Items.Clear();
        foreach (var n in CardFonts.Names) _cboFont.Items.Add(n);
        _cboFont.SelectedItem = select is not null && _cboFont.Items.Contains(select) ? select : CardFonts.Default;
    }

    private static Bitmap? LoadBitmap(string path)
    {
        try { using var fs = File.OpenRead(path); using var img = Image.FromStream(fs); return new Bitmap(img); } // dosyayı kilitlemeden
        catch { return null; }
    }

    private void LoadLogo()
    {
        _picLogo.Image?.Dispose();
        _picLogo.Image = null;
        if (!string.IsNullOrWhiteSpace(Cfg.LogoPath) && File.Exists(Cfg.LogoPath)) _picLogo.Image = LoadBitmap(Cfg.LogoPath);
        _btnCrop.Enabled = _picLogo.Image is not null;
    }

    private void UpdatePreview()
    {
        if (_initializing) return;
        var row = _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0] : (_grid.Rows.Count > 0 ? _grid.Rows[0] : null);
        if (row is null) return;
        var name = row.Cells["Kategori"].Value?.ToString() ?? "";
        var color = CardLayout.ParseColor(row.Cells["Renk"].Value?.ToString(), Color.Gray);
        var layout = new CardLayout(_rbLandscape.Checked);
        const float scale = 0.6f; // 842×595 pt → ~505×357 px
        var bmp = new Bitmap((int)(layout.PageW * scale), (int)(layout.PageH * scale));
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.PageUnit = GraphicsUnit.Point;         // yazı puntoları baskıdakiyle aynı ölçülsün
            float k = scale * 72f / bmp.HorizontalResolution; // Point biriminde 1 pt = dpi/72 px → scale px olsun
            g.ScaleTransform(k, k);
            // Önizleme de baskıdaki ortak puntoyu kullanır (seçili tüm kategorilerin sığdığı en büyük)
            var names = Selected().Select(c => c.Category).Append(name);
            var pt = CardPrinter.CommonBandPt(layout, names, _cboFont.SelectedItem as string);
            CardPrinter.Draw(g, layout, new CardSpec(name, color, 1), _picLogo.Image,
                             EventGrouping.BaseName(_ctx.EventName), _cboFont.SelectedItem as string, _rbFilled.Checked, _chkFullWidth.Checked, pt);
            using var pen = new Pen(Color.FromArgb(200, 200, 200), 1f / k);
            g.DrawRectangle(pen, 0, 0, layout.PageW, layout.PageH); // beyaz zeminde kart kenarı görünsün
        }
        var old = _picPreview.Image;
        _picPreview.Image = bmp;
        old?.Dispose();
    }

    // ================= Çıktı =================
    private CardPrinter? Printer()
    {
        _grid.EndEdit();
        var cards = Selected().ToList();
        if (cards.Count == 0) { _lblStatus.Text = "Basılacak kart seçilmedi."; return null; }
        return new CardPrinter(cards, Cfg.LogoPath, EventGrouping.BaseName(_ctx.EventName),
                               _cboFont.SelectedItem as string, _rbLandscape.Checked, _rbFilled.Checked, _chkFullWidth.Checked);
    }

    private void Print()
    {
        var p = Printer();
        if (p is null) return;
        if (p.PrintWithPreview(this)) _lblStatus.Text = $"{p.PageCount} kart yazıcıya gönderildi.";
    }

    private void SavePdf()
    {
        var p = Printer();
        if (p is null) return;
        Directory.CreateDirectory(_ctx.OutputDir);
        using var dlg = new SaveFileDialog
        {
            Title = "Masa kartlarını PDF olarak kaydet", Filter = "PDF belgesi (*.pdf)|*.pdf",
            InitialDirectory = _ctx.OutputDir, FileName = "masa_kartlari.pdf"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            UseWaitCursor = true;
            p.SavePdf(dlg.FileName);
            _lblStatus.Text = "PDF kaydedildi: " + Path.GetFileName(dlg.FileName);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "PDF oluşturulamadı: " + ex.Message, "PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally { UseWaitCursor = false; }
    }
}
