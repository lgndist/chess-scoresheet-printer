using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Kullanım kolaylıkları: açılır/kapanır işlem kaydı, F5 sonrası yeni turu gelen kategorilerin
/// vurgulanması, liste kısayolları (Ctrl+E eşleştirme, Ctrl+R sonuç).
/// </summary>
public partial class MainForm
{
    private LinkLabel? _lnkLog;
    private LinkLabel? _lnkLogWindow;
    private LogWindow? _logWindow;
    private int _logLines, _logErrors;
    private bool _busy;
    private static readonly Color Highlight = Color.FromArgb(232, 160, 32);

    private void InitUx()
    {
        _lnkLog = new LinkLabel
        {
            AutoSize = true,
            LinkBehavior = LinkBehavior.HoverUnderline,
            LinkColor = ThGreenDark, ActiveLinkColor = ThGreenDark,
            Location = new Point(lblStatus.Left, lblStatus.Bottom + 2),
            Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _lnkLog.LinkClicked += (_, _) => ToggleLog();
        Controls.Add(_lnkLog);
        new ToolTip().SetToolTip(_lnkLog, "İşlem kaydını aç/kapat (çekilen turlar, basılanlar, uyarılar).");
        // Kaydı ayrı, boyutlandırılabilir pencerede aç (sol alt kutu küçük kaldığında)
        _lnkLogWindow = new LinkLabel
        {
            AutoSize = true, Text = "⧉ ayrı pencerede", LinkBehavior = LinkBehavior.HoverUnderline,
            LinkColor = ThGreenDark, ActiveLinkColor = ThGreenDark, Anchor = AnchorStyles.Top | AnchorStyles.Left
        };
        _lnkLogWindow.LinkClicked += (_, _) => ShowLogWindow();
        Controls.Add(_lnkLogWindow);
        new ToolTip().SetToolTip(_lnkLogWindow, "İşlem kaydını istediğiniz boyuta getirebileceğiniz ayrı bir pencerede açar.");
        Load += (_, _) => { ApplyLogState(); RelayoutLeft(); };

        // Yazdır düğmesinin altında ince yazıcı satırı: hangi yazıcıya basılacağı her an görünür, tıklayınca ayarlar.
        btnPrint.Height -= 28;
        const int queueW = 128;
        _btnPrinter = new Button
        {
            Location = new Point(btnPrint.Left, btnPrint.Bottom + 4),
            Size = new Size(btnPrint.Width - queueW - 4, 24),
            Anchor = btnPrint.Anchor,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(60, 60, 60),
            Font = new Font("Segoe UI", 8.75f),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        StyleButton(_btnPrinter);
        _btnPrinter.Click += (_, _) => OpenPrinterSettings();
        grpPairings.Controls.Add(_btnPrinter);
        new ToolTip().SetToolTip(_btnPrinter, "Yazıcı ayarları: hedef yazıcı, kağıt kaynağı, kopya, yazıcı tercihleri (sessiz mod), doğrudan yazdır.");

        // Yanında yazdırma sırası: bekleyen/takılan işler tek tıkla görülsün (Ctrl+Q).
        _btnQueue = new Button
        {
            Text = "📋 Yazdırma sırası",
            Location = new Point(_btnPrinter.Right + 4, _btnPrinter.Top),
            Size = new Size(queueW, 24),
            Anchor = btnPrint.Anchor & ~AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(60, 60, 60),
            Font = new Font("Segoe UI", 8.75f)
        };
        StyleButton(_btnQueue);
        _btnQueue.Click += (_, _) => OpenPrintQueue();
        grpPairings.Controls.Add(_btnQueue);
        new ToolTip().SetToolTip(_btnQueue, "Hedef yazıcının yazdırma sırasını açar: bekleyen, takılan ya da hatalı işleri görün, iptal edin (Ctrl+Q).");

        foreach (var item in _gridListMenu.Items.OfType<ToolStripMenuItem>())
            item.ShortcutKeyDisplayString = item.Text!.Contains("Hakem") ? "Ctrl+H" : item.Text.Contains("eşleştirme") ? "Ctrl+E" : "Ctrl+R";
        _listTip.SetToolTip(_btnGridList!, "Listede görünen kategori ve turun eşleştirme (Ctrl+E) ya da sonuç (Ctrl+R) listesini A4 bas.");
    }

    private Button? _btnPrinter, _btnQueue;

    /// <summary>Hedef yazıcının Windows yazdırma sırası penceresi (bekleyen işler).</summary>
    private void OpenPrintQueue()
    {
        var printer = Overlay.PrintRouter.TargetName;
        if (string.IsNullOrWhiteSpace(printer)) { SetStatus("Yazıcı bulunamadı: Windows'ta varsayılan yazıcı yok.", ok: false); return; }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe", $"printui.dll,PrintUIEntry /o /n \"{printer}\"") { UseShellExecute = false });
            SetStatus($"Yazdırma sırası açıldı: {printer}", ok: true);
        }
        catch (Exception ex) { SetStatus("Yazdırma sırası açılamadı: " + ex.Message, ok: false); }
    }

    private void OpenPrinterSettings()
    {
        using var f = new Overlay.PrinterSettingsForm(_config.Printing) { Icon = Icon };
        if (f.ShowDialog(this) != DialogResult.OK) return;
        SaveConfig();
        Overlay.PrintRouter.Config = _config.Printing;
        UpdatePrintButtonsForSilent();
        SetStatus("Yazıcı ayarları kaydedildi: " + Overlay.PrintRouter.TargetName, ok: true);
    }

    /// <summary>Yazıcı satırının yazısı ("🖨 HP LaserJet • Tepsi 2 • doğrudan   ⚙").</summary>
    private void UpdatePrinterButton()
    {
        if (_btnPrinter is null) return;
        _btnPrinter.Text = Overlay.PrinterSettingsPanel.Summary(_config.Printing) + "   ⚙ değiştir";
    }

    private bool LogExpanded => _config?.Layout.LogExpanded ?? false;

    private void ToggleLog()
    {
        _config.Layout.LogExpanded = !_config.Layout.LogExpanded;
        SaveConfig();
        ApplyLogState();
        RelayoutLeft();
        if (LogExpanded) { txtLog.SelectionStart = txtLog.TextLength; txtLog.ScrollToCaret(); }
    }

    private void ApplyLogState()
    {
        txtLog.Visible = LogExpanded;
        UpdateLogLink();
    }

    private void OnLogged(string line)
    {
        if (_logWindow is { Visible: true }) _logWindow.RefreshText();
        _logLines++;
        if (line.StartsWith("✗") || line.StartsWith("Uyarı")) _logErrors++;
        UpdateLogLink();
    }

    private void UpdateLogLink()
    {
        if (_lnkLog is null) return;
        var errors = _logErrors > 0 ? $", {_logErrors} uyarı" : "";
        _lnkLog.Text = (LogExpanded ? "▾ İşlem kaydını gizle" : "▸ İşlem kaydı") + $" ({_logLines} satır{errors})";
        _lnkLog.LinkColor = _logErrors > 0 && !LogExpanded ? Color.FromArgb(160, 60, 60) : ThGreenDark;
        PlaceLogWindowLink();
    }

    private void PlaceLogWindowLink()
    {
        if (_lnkLog is null || _lnkLogWindow is null) return;
        _lnkLogWindow.Location = new Point(_lnkLog.Right + 14, _lnkLog.Top);
    }

    private void ShowLogWindow()
    {
        _logWindow ??= new LogWindow(() => txtLog.Text, () => { txtLog.Clear(); _logLines = _logErrors = 0; UpdateLogLink(); }) { Icon = Icon };
        if (!_logWindow.Visible)
        {
            var wa = Screen.FromControl(this).WorkingArea;
            _logWindow.Location = new Point(Math.Max(wa.Left, Math.Min(Left + 40, wa.Right - _logWindow.Width)),
                                            Math.Max(wa.Top, Math.Min(Bottom - _logWindow.Height - 40, wa.Bottom - _logWindow.Height)));
            _logWindow.Show(this);
        }
        _logWindow.RefreshText();
        _logWindow.Activate();    }

    // ================= F5: yeni tur vurgusu =================
    /// <summary>Yenilemeden önce her kategorinin son eşlenen ve sonuçlanan turu.</summary>
    private Dictionary<int, (int Current, int Ranked)> SnapshotRounds(IEnumerable<CategoryRef> cats)
        => cats.ToDictionary(c => c.Tnr, c => (
            _roundCache.TryGetValue(c.Tnr, out var r) ? r.Current : -1,
            _ranked.TryGetValue(c.Tnr, out var k) ? k : -1));

    /// <summary>
    /// Yenilemeden sonra turu ilerleyen kategorilerin düğmelerini birkaç saniye turuncu yakar ve
    /// günlüğe yazar ("🆕 Yeni eşleştirme: 12 Yaş T6 • Yeni sonuç: Açık T5").
    /// </summary>
    private void FlashChangedRounds(IReadOnlyList<CategoryRef> cats, Dictionary<int, (int Current, int Ranked)> before)
    {
        var newPair = new List<string>();
        var newRes = new List<string>();
        var buttons = new List<(Button Button, bool Primary)>();
        foreach (var c in cats)
        {
            if (!before.TryGetValue(c.Tnr, out var old)) continue;
            if (_roundCache.TryGetValue(c.Tnr, out var r) && old.Current >= 0 && r.Current > old.Current)
            {
                newPair.Add($"{ButtonText(c)} T{r.Current}");
                buttons.AddRange(flowQuick.Controls.OfType<Button>().Where(b => b.Tag is CategoryRef k && k.Tnr == c.Tnr).Select(b => (b, true)));
                if (_flowPairLists is not null)
                    buttons.AddRange(_flowPairLists.Controls.OfType<Button>().Where(b => b.Tag is CategoryRef k && k.Tnr == c.Tnr).Select(b => (b, false)));
            }
            if (_ranked.TryGetValue(c.Tnr, out var rk) && old.Ranked >= 0 && rk > old.Ranked)
            {
                newRes.Add($"{ButtonText(c)} T{rk}");
                if (_flowResultLists is not null)
                    buttons.AddRange(_flowResultLists.Controls.OfType<Button>().Where(b => b.Tag is CategoryRef k && k.Tnr == c.Tnr).Select(b => (b, false)));
            }
        }
        if (newPair.Count == 0 && newRes.Count == 0) return;

        var parts = new List<string>();
        if (newPair.Count > 0) parts.Add("Yeni eşleştirme: " + string.Join(", ", newPair));
        if (newRes.Count > 0) parts.Add("Yeni sonuç: " + string.Join(", ", newRes));
        Log("🆕 " + string.Join(" • ", parts));
        SetStatus("🆕 " + string.Join(" • ", parts), ok: true);

        foreach (var (b, _) in buttons)
        {
            b.BackColor = Highlight;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderColor = Color.FromArgb(190, 120, 10);
        }
        var timer = new System.Windows.Forms.Timer { Interval = 8000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            foreach (var (b, primary) in buttons)
            {
                if (b.IsDisposed) continue;
                b.BackColor = primary ? ThGreen : Color.White;
                b.ForeColor = primary ? Color.White : Color.FromArgb(40, 40, 40);
                StyleButton(b);
            }
        };
        timer.Start();
    }
}
