using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Yazıcı ayarları (hedef yazıcı, kağıt kaynağı, kopya, yazıcının kendi tercihleri, doğrudan yazdır).
/// Ayarlar penceresi ve ana ekrandaki "🖨 Yazıcı" düğmesinin açtığı küçük pencere aynı paneli kullanır.
/// </summary>
public sealed class PrinterSettingsPanel : UserControl
{
    private const string DefaultPrinterItem = "(Windows varsayılan yazıcısı)";
    private const string AutoTrayItem = "(otomatik)";
    private const string SameTrayItem = "(yukarıdakiyle aynı)";

    private readonly ComboBox _printer = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _tray = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _trayA5 = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown _copies = new() { Minimum = 1, Maximum = 20, Value = 1, Width = 70 };
    private readonly ToggleSwitch _silent = new() { Text = "Doğrudan yazdır (önizleme ve yazıcı penceresi açılmadan)", Dock = DockStyle.Fill, Height = 28 };
    private readonly Button _btnPrefs = new() { Text = "Yazıcı tercihleri… (sessiz mod, kalite)", AutoSize = true, Height = 30 };
    private readonly LinkLabel _lnkReset = new() { Text = "Sıfırla", AutoSize = true, Margin = new Padding(8, 8, 0, 0) };
    private readonly Label _lblPrefs = new() { AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(3, 0, 3, 6) };
    private readonly Dictionary<string, string> _devModes = new(StringComparer.OrdinalIgnoreCase);

    public PrinterSettingsPanel(int width)
    {
        Width = width;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        // Dock'lu çocuk AutoSize panelin boyutuna katılmaz: tablo sabit sütunlu ve kendi boyunda.
        var t = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = Point.Empty, Margin = Padding.Empty };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, width - 130));

        void Row(string label, Control c)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) });
            t.Controls.Add(c);
        }
        Row("Hedef yazıcı:", _printer);
        Row("Kağıt kaynağı:", _tray);
        Row("A5 tepsisi:", _trayA5);
        Row("Kopya sayısı:", _copies);
        var prefs = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        prefs.Controls.AddRange(new Control[] { _btnPrefs, _lnkReset });
        t.Controls.Add(new Label());
        t.Controls.Add(prefs);
        t.Controls.Add(new Label());
        t.Controls.Add(_lblPrefs);
        t.Controls.Add(_silent);
        t.SetColumnSpan(_silent, 2);
        var note = new Label
        {
            AutoSize = true, MaximumSize = new Size(width - 10, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 6, 3, 3),
            Text = "A4 belgeler (hakem kağıdı, listeler) ile A5 notasyon aynı tepsiden basılıyorsa kağıt değiştirince tepsinin kılavuzlarını " +
                   "ve boyut ayarını A5'e getirin; yoksa yazılar kayar. İki tepsi varsa A5 notasyonu ayrı tepsiden basın. " +
                   "Sessiz mod, baskı kalitesi gibi ayarlar üreticiye özeldir: \"Yazıcı tercihleri…\" yazıcının kendi penceresini açar; " +
                   "seçtikleriniz her baskıda uygulanır. \"Doğrudan yazdır\" açıkken önizleme ve yazıcı penceresi açılmaz. " +
                   "Kopya sayısı her baskı işine uygulanır (notasyon nüshası ayrıca belirlenir)."
        };
        t.Controls.Add(note);
        t.SetColumnSpan(note, 2);
        Controls.Add(t);

        _btnPrefs.Click += (_, _) =>
        {
            var name = PrinterNameOf(_printer.SelectedItem as string);
            var result = PrinterPreferences.Edit(FindForm()!, name, _devModes.GetValueOrDefault(name));
            if (result is not null) _devModes[name] = result;
            UpdatePrefsLabel();
        };
        _lnkReset.LinkClicked += (_, _) => { _devModes.Remove(PrinterNameOf(_printer.SelectedItem as string)); UpdatePrefsLabel(); };
        _printer.SelectedIndexChanged += (_, _) => { FillTrays(null, null); UpdatePrefsLabel(); };
    }

    /// <summary>Ayarları panele yükler.</summary>
    public void LoadFrom(PrintConfig pc)
    {
        _printer.Items.Clear();
        _printer.Items.Add(DefaultPrinterItem);
        foreach (var p in PrintRouter.InstalledPrinters()) _printer.Items.Add(p);
        _printer.SelectedItem = pc.PrinterName is { } pn && _printer.Items.Contains(pn) ? pn : DefaultPrinterItem;
        FillTrays(pc.PaperSource, pc.PaperSourceA5);
        _copies.Value = Math.Clamp(pc.Copies, 1, 20);
        _silent.Checked = pc.Silent;
        _devModes.Clear();
        foreach (var (k, v) in pc.DevModes) _devModes[k] = v;
        UpdatePrefsLabel();
    }

    /// <summary>Paneldeki seçimleri ayarlara yazar.</summary>
    public void SaveTo(PrintConfig pc)
    {
        pc.PrinterName = _printer.SelectedItem is string p && p != DefaultPrinterItem ? p : null;
        pc.PaperSource = _tray.SelectedItem is string t && t != AutoTrayItem ? t : null;
        pc.PaperSourceA5 = _trayA5.SelectedItem is string t5 && t5 != SameTrayItem ? t5 : null;
        pc.Copies = (int)_copies.Value;
        pc.Silent = _silent.Checked;
        pc.DevModes = new Dictionary<string, string>(_devModes, StringComparer.OrdinalIgnoreCase);
    }

    private static string PrinterNameOf(string? item)
        => string.IsNullOrWhiteSpace(item) || item == DefaultPrinterItem ? PrintRouter.DefaultPrinter() : item;

    private void UpdatePrefsLabel()
    {
        bool has = _devModes.ContainsKey(PrinterNameOf(_printer.SelectedItem as string));
        _lblPrefs.Text = has ? "✓ Bu yazıcı için kayıtlı tercihler her baskıda uygulanır." : "Yazıcının kendi varsayılan ayarları kullanılır.";
        _lnkReset.Visible = has;
    }

    private void FillTrays(string? select, string? selectA5)
    {
        var printer = _printer.SelectedItem as string;
        var sources = PrintRouter.PaperSources(printer == DefaultPrinterItem ? null : printer);
        _tray.Items.Clear();
        _tray.Items.Add(AutoTrayItem);
        foreach (var s in sources) _tray.Items.Add(s);
        _tray.SelectedItem = select is not null && _tray.Items.Contains(select) ? select : AutoTrayItem;
        _trayA5.Items.Clear();
        _trayA5.Items.Add(SameTrayItem);
        foreach (var s in sources) _trayA5.Items.Add(s);
        _trayA5.SelectedItem = selectA5 is not null && _trayA5.Items.Contains(selectA5) ? selectA5 : SameTrayItem;
    }

    /// <summary>Ana ekrandaki düğme için kısa özet: "🖨 HP LaserJet • Tepsi 2 • 2 kopya • doğrudan".</summary>
    public static string Summary(PrintConfig pc)
    {
        var parts = new List<string> { "🖨 " + PrintRouter.TargetName };
        if (!string.IsNullOrWhiteSpace(pc.PaperSource)) parts.Add(pc.PaperSource!);
        if (!string.IsNullOrWhiteSpace(pc.PaperSourceA5)) parts.Add("A5: " + pc.PaperSourceA5);
        if (pc.Copies > 1) parts.Add($"{pc.Copies} kopya");
        if (pc.DevModes.ContainsKey(PrintRouter.TargetName)) parts.Add("tercihli");
        if (PrintRouter.IsSilent) parts.Add("doğrudan");
        return string.Join(" • ", parts);
    }
}

/// <summary>Ana ekrandan açılan küçük yazıcı ayarları penceresi.</summary>
public sealed class PrinterSettingsForm : Form
{
    private readonly PrinterSettingsPanel _panel = new(460);

    public PrinterSettingsForm(PrintConfig pc)
    {
        Text = "Yazıcı ayarları";
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);
        BackColor = Color.FromArgb(250, 250, 247);

        var green = Color.FromArgb(118, 150, 86);
        var ok = new Button { Text = "Kaydet", Width = 110, Height = 34, BackColor = green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Vazgeç", Width = 90, Height = 34, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 10, 0, 0) };
        buttons.Controls.AddRange(new Control[] { ok, cancel });
        var host = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        host.Controls.Add(_panel);
        host.Controls.Add(buttons);
        buttons.Width = _panel.Width;
        Controls.Add(host);
        AcceptButton = ok;
        CancelButton = cancel;
        _panel.LoadFrom(pc);
        FormClosing += (_, _) => { if (DialogResult == DialogResult.OK) _panel.SaveTo(pc); };
    }
}
