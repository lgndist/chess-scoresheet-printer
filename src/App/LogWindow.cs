namespace NotasyonOtomasyonu.App;

/// <summary>
/// İşlem kaydının ayrı, boyutlandırılabilir penceresi (ana penceredeki sol alt kutu küçük kaldığında).
/// İki sekme: "İşlem kaydı" ana kayıtla canlı eşlenir (yalnız uyarıları süzme, kopyalama, temizleme);
/// "Baskı ayrıntıları" yazıcı, kağıt, kenar boşluğu, kaydırma ve son baskıda yazıların kağıttaki yerini
/// gösterir (kayma sorununu çözmek ya da göndermek için kopyalanır). Kapatılınca gizlenir.
/// </summary>
public sealed class LogWindow : Form
{
    private readonly TextBox _txt = Box();
    private readonly TextBox _details = Box();
    private readonly CheckBox _chkWarnings = new() { Text = "Yalnız uyarılar", AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _detailsPage = new("🔍 Baskı ayrıntıları");
    private readonly Func<string> _source;
    private readonly Func<string>? _detailsSource;

    private static TextBox Box() => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        BackColor = Color.White, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None
    };

    private static Button Btn(string text) => new() { Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White };

    public LogWindow(Func<string> source, Action clear, Func<string>? details = null)
    {
        _source = source;
        _detailsSource = details;
        _details.WordWrap = true;                   // ayrıntılar okunsun diye satırlar kaydırılır
        _details.ScrollBars = ScrollBars.Vertical;
        Text = "İşlem kaydı";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.Manual;
        Size = new Size(820, 480);
        MinimumSize = new Size(380, 220);
        ShowInTaskbar = false;

        // ---- İşlem kaydı ----
        var logPage = new TabPage("İşlem kaydı");
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 0), BackColor = Color.FromArgb(242, 242, 236) };
        var copy = Btn("📋 Kopyala");
        var clr = Btn("Temizle");
        copy.Click += (_, _) => { if (_txt.TextLength > 0) Clipboard.SetText(_txt.Text); };
        clr.Click += (_, _) => { clear(); RefreshText(); };
        _chkWarnings.CheckedChanged += (_, _) => RefreshText();
        bar.Controls.AddRange(new Control[] { copy, clr, _chkWarnings });
        logPage.Controls.Add(_txt);
        logPage.Controls.Add(bar);
        _tabs.TabPages.Add(logPage);

        // ---- Baskı ayrıntıları ----
        if (details is not null)
        {
            var dbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 0), BackColor = Color.FromArgb(242, 242, 236) };
            var refresh = Btn("↻ Yenile");
            var dcopy = Btn("📋 Kopyala (göndermek için)");
            refresh.Click += (_, _) => RefreshDetails();
            dcopy.Click += (_, _) => { RefreshDetails(); if (_details.TextLength > 0) Clipboard.SetText(_details.Text); };
            dbar.Controls.AddRange(new Control[] { refresh, dcopy });
            _detailsPage.Controls.Add(_details);
            _detailsPage.Controls.Add(dbar);
            _tabs.TabPages.Add(_detailsPage);
            _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedTab == _detailsPage) RefreshDetails(); };
        }
        Controls.Add(_tabs);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    /// <summary>Ana kayıttan yeniden doldurur ve sona kaydırır.</summary>
    public void RefreshText()
    {
        var text = _source();
        if (_chkWarnings.Checked)
            text = string.Join(Environment.NewLine, text.Split(Environment.NewLine)
                                                        .Where(l => l.StartsWith("✗") || l.StartsWith("Uyarı") || l.StartsWith("⚠")));
        _txt.Text = text;
        _txt.SelectionStart = _txt.TextLength;
        _txt.ScrollToCaret();
        if (_tabs.SelectedTab == _detailsPage) RefreshDetails();
    }

    /// <summary>Baskı ayrıntılarını yeniden hesaplar (yazıcıdan da okunur).</summary>
    public void RefreshDetails()
    {
        if (_detailsSource is null) return;
        try { _details.Text = _detailsSource(); }
        catch (Exception ex) { _details.Text = "Ayrıntılar alınamadı: " + ex.Message; }
    }

    /// <summary>Pencereyi baskı ayrıntıları sekmesinde açar.</summary>
    public void ShowDetailsTab()
    {
        if (_detailsSource is null) return;
        _tabs.SelectedTab = _detailsPage;
        RefreshDetails();
    }
}
