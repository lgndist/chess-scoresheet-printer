namespace NotasyonOtomasyonu.App;

/// <summary>
/// İşlem kaydının ayrı, boyutlandırılabilir penceresi (ana penceredeki sol alt kutu küçük kaldığında).
/// Ana kayıtla canlı eşlenir; yalnız uyarıları süzme, kopyalama ve temizleme sunar. Kapatılınca gizlenir.
/// </summary>
public sealed class LogWindow : Form
{
    private readonly TextBox _txt = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
        BackColor = Color.White, Font = new Font("Consolas", 9.5f), BorderStyle = BorderStyle.None
    };
    private readonly CheckBox _chkWarnings = new() { Text = "Yalnız uyarılar", AutoSize = true, Margin = new Padding(8, 8, 8, 0) };
    private readonly Func<string> _source;

    public LogWindow(Func<string> source, Action clear)
    {
        _source = source;
        Text = "İşlem kaydı";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.Manual;
        Size = new Size(760, 420);
        MinimumSize = new Size(360, 200);
        ShowInTaskbar = false;

        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, Padding = new Padding(6, 4, 6, 0), BackColor = Color.FromArgb(242, 242, 236) };
        var copy = new Button { Text = "📋 Kopyala", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        var clr = new Button { Text = "Temizle", AutoSize = true, FlatStyle = FlatStyle.Flat, BackColor = Color.White };
        copy.Click += (_, _) => { if (_txt.TextLength > 0) Clipboard.SetText(_txt.Text); };
        clr.Click += (_, _) => { clear(); RefreshText(); };
        _chkWarnings.CheckedChanged += (_, _) => RefreshText();
        bar.Controls.AddRange(new Control[] { copy, clr, _chkWarnings });
        Controls.Add(_txt);
        Controls.Add(bar);
        FormClosing += (_, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
    }

    /// <summary>Ana kayıttan yeniden doldurur ve sona kaydırır.</summary>
    public void RefreshText()
    {
        var text = _source();
        if (_chkWarnings.Checked)
            text = string.Join(Environment.NewLine, text.Split(Environment.NewLine)
                                                        .Where(l => l.StartsWith("✗") || l.StartsWith("Uyarı")));
        _txt.Text = text;
        _txt.SelectionStart = _txt.TextLength;
        _txt.ScrollToCaret();
    }
}
