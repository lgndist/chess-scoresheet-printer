using System.Drawing;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.TemplateDesigner;

/// <summary>
/// Hazır notasyon kağıdı şablonu tasarımcısı: boş kağıt görselini yükle, alan kutularını
/// sürükleyip boyutlandır, yazıların sığması için font/kısaltma kurallarını ayarla, kaydet.
/// </summary>
public sealed class TemplateDesignerForm : Form
{
    private readonly OverlayTemplate _tpl;
    private readonly string _pageSize;
    private readonly Tournament _sampleT;
    private readonly Pairing _sampleP;
    private readonly List<FieldBoxControl> _boxes = new();
    private FieldBoxControl? _sel;
    private bool _loading;

    private readonly Panel _canvas = new() { BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };

    // Sağ panel kontrolleri
    private readonly ComboBox _cboPerPage = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _chkPrintBg = new() { Text = "Arka planı da yazdır (düz kağıt testi)", AutoSize = true };
    private readonly ComboBox _cboAddKind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private static readonly FieldKind[] AddableKinds =
        Enum.GetValues<FieldKind>().Where(k => !FieldKinds.IsRetired(k)).ToArray();
    private readonly TextBox _txtStatic = new();
    private readonly NumericUpDown _numFont = new() { Minimum = 5, Maximum = 48, DecimalPlaces = 0 };
    private readonly NumericUpDown _numMin = new() { Minimum = 4, Maximum = 24 };
    private readonly CheckBox _chkBold = new() { Text = "Kalın", AutoSize = true };
    private readonly ComboBox _cboAlign = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _cboOverflow = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _lblSelInfo = new() { AutoSize = true, ForeColor = Color.FromArgb(95, 122, 70) };

    public OverlayTemplate Result => _tpl;

    private string SizeLabel => Core.PageGeometry.IsA5(_pageSize) ? "A5" : "A4";

    public TemplateDesignerForm(OverlayTemplate template, string pageSize = "A5")
    {
        _tpl = template.DeepClone();
        _pageSize = pageSize;
        (_sampleT, _sampleP) = OverlayLayout.Sample();

        Text = $"Hazır Kağıt Şablonu Tasarımcısı — {SizeLabel}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        BackColor = Color.FromArgb(245, 245, 240);
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(740, 660);
        this.FitToScreen(); // küçük ekranda / yüksek ölçekte taşmasın

        _canvas.Location = new Point(12, 12);
        _canvas.BackgroundImageLayout = ImageLayout.Stretch;
        Controls.Add(_canvas);

        BuildRightPanel();
        ApplyTemplateToUi();
        RebuildBoxes();
        SelectBox(null);
    }

    // ----------------- Sağ panel -----------------
    private void BuildRightPanel()
    {
        int x = 470, w = 256, y = 12;

        void Header(string t)
        {
            Controls.Add(new Label { Text = t, AutoSize = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(95, 122, 70), Location = new Point(x, y) });
            y += 22;
        }
        void Row(string label, Control c, int h = 25)
        {
            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(x, y + 4) });
            c.SetBounds(x + 96, y, w - 96, h);
            Controls.Add(c);
            y += h + 6;
        }

        Header($"Kağıt / arka plan  ({SizeLabel})");
        _cboPerPage.Items.AddRange(new object[] { $"1 kağıt / {SizeLabel}", $"2 kağıt / {SizeLabel}" });
        _cboPerPage.SelectedIndexChanged += (_, _) => { if (_loading) return; _tpl.PerPage = _cboPerPage.SelectedIndex == 0 ? 1 : 2; LayoutCanvas(); RebuildBoxes(); };
        Row("Sayfada:", _cboPerPage);

        var btnBg = new Button { Text = "Arka plan görseli yükle…" };
        btnBg.SetBounds(x, y, w, 28); btnBg.Click += LoadBg_Click; Controls.Add(btnBg); y += 34;
        var btnClr = new Button { Text = "Arka planı kaldır" };
        btnClr.SetBounds(x, y, w, 26); btnClr.Click += (_, _) => { _tpl.BackgroundImagePath = null; _canvas.BackgroundImage = null; }; Controls.Add(btnClr); y += 32;
        _chkPrintBg.Location = new Point(x, y); _chkPrintBg.CheckedChanged += (_, _) => { if (!_loading) _tpl.PrintBackground = _chkPrintBg.Checked; }; Controls.Add(_chkPrintBg); y += 28;

        y += 6; Header("Alan ekle");
        // Yer/zaman kontrolü/hakem artık basılmadığından listede yok.
        foreach (var k in AddableKinds)
            _cboAddKind.Items.Add(OverlayLayout.DisplayName(k));
        _cboAddKind.SelectedIndex = Array.IndexOf(AddableKinds, FieldKind.WhiteName);
        _cboAddKind.SetBounds(x, y, w - 70, 25);
        var btnAdd = new Button { Text = "Ekle" };
        btnAdd.SetBounds(x + w - 64, y - 1, 64, 27); btnAdd.Click += AddField_Click;
        Controls.Add(_cboAddKind); Controls.Add(btnAdd); y += 36;

        y += 6; Header("Seçili alan özellikleri");
        Row("Sabit metin:", _txtStatic);
        _txtStatic.TextChanged += (_, _) => { if (_loading || _sel is null) return; _sel.Field.StaticText = _txtStatic.Text; RefreshBox(_sel); };
        Row("Punto:", _numFont);
        _numFont.ValueChanged += (_, _) => { if (_loading || _sel is null) return; _sel.Field.FontSize = (double)_numFont.Value; RefreshBox(_sel); };
        Row("En küçük punto:", _numMin);
        _numMin.ValueChanged += (_, _) => { if (_loading || _sel is null) return; _sel.Field.MinFontSize = (double)_numMin.Value; RefreshBox(_sel); };

        _chkBold.Location = new Point(x + 96, y); _chkBold.CheckedChanged += (_, _) => { if (_loading || _sel is null) return; _sel.Field.Bold = _chkBold.Checked; RefreshBox(_sel); };
        Controls.Add(_chkBold); y += 28;

        _cboAlign.Items.AddRange(new object[] { "Sol", "Orta", "Sağ" });
        _cboAlign.SelectedIndexChanged += (_, _) => { if (_loading || _sel is null) return; _sel.Field.Align = (HAlign)_cboAlign.SelectedIndex; };
        Row("Hizalama:", _cboAlign);

        _cboOverflow.Items.AddRange(new object[] { "Sadece küçült", "Küçült, sığmazsa kes (…)", "Küçült, sığmazsa kısalt (A. Soyad)" });
        _cboOverflow.SelectedIndexChanged += (_, _) => { if (_loading || _sel is null) return; _sel.Field.Overflow = (OverflowMode)_cboOverflow.SelectedIndex; RefreshBox(_sel); };
        Row("Uzun metin:", _cboOverflow, 25);

        var btnDel = new Button { Text = "Seçili alanı sil", ForeColor = Color.FromArgb(160, 60, 60) };
        btnDel.SetBounds(x, y, w, 26); btnDel.Click += DeleteField_Click; Controls.Add(btnDel); y += 34;

        _lblSelInfo.Location = new Point(x, y); _lblSelInfo.MaximumSize = new Size(w, 0); Controls.Add(_lblSelInfo); y += 40;

        // Alt: kaydet/kapat
        var btnSave = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, BackColor = Color.FromArgb(118, 150, 86), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        btnSave.SetBounds(x, 600, 124, 34);
        var btnCancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel };
        btnCancel.SetBounds(x + 132, 600, 124, 34);
        btnSave.Click += (_, _) => CommitGeometry();
        Controls.Add(btnSave); Controls.Add(btnCancel);
        AcceptButton = btnSave; CancelButton = btnCancel;

        var help = new Label
        {
            Text = "İpucu: kutuyu gövdesinden sürükleyerek taşı, sağ-alt köşeden boyutlandır. " +
                   "Yazılar kutuya otomatik sığar.",
            Location = new Point(12, 624), AutoSize = false, Size = new Size(450, 30),
            ForeColor = Color.Gray
        };
        Controls.Add(help);
    }

    // ----------------- Tuval / kutular -----------------
    private void LayoutCanvas()
    {
        // Seçili sayfa boyutuna (A5/A4) göre kağıt en/boy oranı.
        // 1 kağıt = dikey tam sayfa, 2 kağıt = sayfanın üst/alt yarısı (yatay).
        int maxH = 600, maxW = 440;
        var (pw, ph) = Core.PageGeometry.Points(_pageSize);
        int wpx, hpx;
        if (_tpl.PerPage == 2) { wpx = maxW; hpx = (int)(maxW * (ph / 2f) / pw); }
        else { hpx = maxH; wpx = (int)(maxH * pw / ph); }
        _canvas.Size = new Size(wpx, hpx);
    }

    private void RebuildBoxes()
    {
        foreach (var b in _boxes) { _canvas.Controls.Remove(b); b.Dispose(); }
        _boxes.Clear();
        foreach (var f in _tpl.Fields) AddBoxFor(f);
        SelectBox(null);
    }

    private FieldBoxControl AddBoxFor(OverlayField f)
    {
        var box = new FieldBoxControl(f);
        ApplyFieldToBox(box);
        box.GeometryChanged += (_, _) => ReadBoxToField(box);
        box.SelectedNow += (_, _) => SelectBox(box);
        _canvas.Controls.Add(box);
        _boxes.Add(box);
        RefreshBox(box);
        return box;
    }

    private void ApplyFieldToBox(FieldBoxControl box)
    {
        int cw = _canvas.ClientSize.Width, ch = _canvas.ClientSize.Height;
        var f = box.Field;
        box.Bounds = new Rectangle(
            (int)(f.X * cw), (int)(f.Y * ch),
            Math.Max(box.MinimumSize.Width, (int)(f.W * cw)),
            Math.Max(box.MinimumSize.Height, (int)(f.H * ch)));
    }

    private void ReadBoxToField(FieldBoxControl box)
    {
        double cw = _canvas.ClientSize.Width, ch = _canvas.ClientSize.Height;
        var f = box.Field;
        f.X = box.Left / cw; f.Y = box.Top / ch;
        f.W = box.Width / cw; f.H = box.Height / ch;
    }

    private void RefreshBox(FieldBoxControl box)
    {
        box.Caption = OverlayLayout.DisplayName(box.Field.Kind);
        box.PreviewText = OverlayLayout.Resolve(box.Field, _sampleT, _sampleP);
        box.Invalidate();
    }

    private void SelectBox(FieldBoxControl? box)
    {
        _sel = box;
        foreach (var b in _boxes) b.Selected = ReferenceEquals(b, box);

        _loading = true;
        bool has = box is not null;
        _txtStatic.Enabled = has && box!.Field.Kind == FieldKind.FreeText;
        _numFont.Enabled = _numMin.Enabled = _chkBold.Enabled = _cboAlign.Enabled = _cboOverflow.Enabled = has;
        if (has)
        {
            var f = box!.Field;
            _txtStatic.Text = f.StaticText ?? "";
            _numFont.Value = Clamp((decimal)f.FontSize, _numFont.Minimum, _numFont.Maximum);
            _numMin.Value = Clamp((decimal)f.MinFontSize, _numMin.Minimum, _numMin.Maximum);
            _chkBold.Checked = f.Bold;
            _cboAlign.SelectedIndex = (int)f.Align;
            _cboOverflow.SelectedIndex = (int)f.Overflow;
            _lblSelInfo.Text = $"Seçili: {OverlayLayout.DisplayName(f.Kind)}";
        }
        else _lblSelInfo.Text = "Bir alan seçin veya yeni alan ekleyin.";
        _loading = false;
    }

    // ----------------- Olaylar -----------------
    private void AddField_Click(object? sender, EventArgs e)
    {
        var kind = AddableKinds[_cboAddKind.SelectedIndex];
        var f = new OverlayField
        {
            Kind = kind,
            StaticText = kind == FieldKind.FreeText ? "Metin" : null,
            X = 0.08, Y = 0.08, W = 0.40, H = 0.05,
            FontSize = kind == FieldKind.BoardNo ? 20 : 11,
            Bold = kind is FieldKind.BoardNo
        };
        _tpl.Fields.Add(f);
        var box = AddBoxFor(f);
        box.BringToFront();
        SelectBox(box);
    }

    private void DeleteField_Click(object? sender, EventArgs e)
    {
        if (_sel is null) return;
        _tpl.Fields.Remove(_sel.Field);
        _canvas.Controls.Remove(_sel);
        _boxes.Remove(_sel);
        _sel.Dispose();
        SelectBox(null);
    }

    private void LoadBg_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Boş notasyon kağıdı görseli",
            Filter = "Görsel (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|Tümü (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            using (var img = Image.FromFile(dlg.FileName))
                _canvas.BackgroundImage = new Bitmap(img); // dosyayı kilitleme
            // Kopyası veri klasörüne alınır: asıl dosya silinse ya da taşınsa da şablon bozulmasın.
            _tpl.BackgroundImagePath = AppPaths.KeepCopy(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Görsel yüklenemedi: " + ex.Message, "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyTemplateToUi()
    {
        _loading = true;
        _cboPerPage.SelectedIndex = _tpl.PerPage == 2 ? 1 : 0;
        _chkPrintBg.Checked = _tpl.PrintBackground;
        LayoutCanvas();
        if (!string.IsNullOrWhiteSpace(_tpl.BackgroundImagePath) && File.Exists(_tpl.BackgroundImagePath))
        {
            try { using var img = Image.FromFile(_tpl.BackgroundImagePath); _canvas.BackgroundImage = new Bitmap(img); }
            catch { /* görsel yoksa boş tuval */ }
        }
        _loading = false;
    }

    private void CommitGeometry()
    {
        foreach (var b in _boxes) ReadBoxToField(b);
    }

    private static decimal Clamp(decimal v, decimal min, decimal max) => v < min ? min : v > max ? max : v;
}
