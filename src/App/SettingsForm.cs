using NotasyonOtomasyonu.Online;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Sık değişmeyen ama kalıcı olması gereken ayarlar: varsayılan turnuva adı, sayfa boyutu,
/// yazıcı hizalaması ve şablonlar. OK'e basınca verilen <see cref="AppConfig"/> nesnesine yazar (çağıran kaydeder).
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;

    private readonly TextBox _name = new();
    private readonly ComboBox _pageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _offX = MmBox();
    private readonly NumericUpDown _offY = MmBox();
    private readonly Dictionary<string, ComboBox> _systemTemplate = new();

    // Sağ sütun: bölge (varsayılan il, TSF sitesi) ve yazıcı
    private readonly ComboBox _province = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _tsfSite = new();
    private readonly PrinterSettingsPanel _printerPanel = new(RW);
    private readonly PictureBox _provinceLogo = new() { SizeMode = PictureBoxSizeMode.Zoom, Size = new System.Drawing.Size(58, 58) };
    private readonly ComboBox _logoKind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Action<IWin32Window>? _openRegistration;
    private const string NoProvinceItem = "(seçilmedi)";

    private const string ActiveTemplateItem = "(etkin şablon)";
    private static readonly (string Key, string Label)[] SystemRows =
    {
        ("Swiss", "İsviçre sistemi:"),
        ("RoundRobin", "Berger (döner):"),
        ("Team", "Takım turnuvası:"),
    };

    public SettingsForm(AppConfig cfg, Action<IWin32Window>? openRegistration = null)
    {
        _cfg = cfg;
        _openRegistration = openRegistration;

        Text = "⚙ Ayarlar";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);

        int y = 16;
        AddRow("Varsayılan turnuva adı:", _name, ref y);

        _pageSize.Items.AddRange(new object[] { "A5 (önerilen)", "A4" });
        AddRow("Sayfa boyutu:", _pageSize, ref y);

        // ---- Yazıcı hizalama ----
        y += 4;
        Controls.Add(new Label
        {
            Text = "Yazıcı hizalama (mm)", AutoSize = true, Location = new System.Drawing.Point(16, y),
            Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70)
        });
        y += 24;
        Controls.Add(new Label { Text = "Sağa (+) / sola (−):", AutoSize = true, Location = new System.Drawing.Point(16, y + 4) });
        _offX.SetBounds(150, y, 70, 25); Controls.Add(_offX);
        Controls.Add(new Label { Text = "Aşağı (+) / yukarı (−):", AutoSize = true, Location = new System.Drawing.Point(230, y + 4) });
        _offY.SetBounds(374, y, 50, 25); Controls.Add(_offY);
        y += 32;
        Controls.Add(new Label
        {
            Text = "Yazılar hazır kağıttaki kutulara göre kayıksa buradan düzeltin; şablonu değiştirmeniz gerekmez. " +
                   "Önce hizalama testini boş bir hazır kağıda basıp ölçün.",
            AutoSize = false, Size = new System.Drawing.Size(408, 54), Location = new System.Drawing.Point(16, y),
            ForeColor = System.Drawing.Color.Gray
        });
        y += 58;
        var btnTest = new Button
        {
            Text = "🖨 Hizalama testi yazdır (örnek tek kağıt)…",
            Size = new System.Drawing.Size(408, 32), Location = new System.Drawing.Point(16, y)
        };
        btnTest.Click += PrintAlignmentTest;
        Controls.Add(btnTest);
        y += 44;

        // Hazır kağıt (overlay) şablonu tasarımcısı
        var btnTemplate = new Button
        {
            Text = "🎨 Hazır kağıt şablonları (ekle / düzenle)…",
            Size = new System.Drawing.Size(408, 32),
            Location = new System.Drawing.Point(16, y)
        };
        btnTemplate.Click += OpenTemplates;
        Controls.Add(btnTemplate);
        y += 42;

        // ---- Sisteme göre şablon ----
        Controls.Add(new Label
        {
            Text = "Sisteme göre şablon", AutoSize = true, Location = new System.Drawing.Point(16, y),
            Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70)
        });
        y += 24;
        foreach (var (key, label) in SystemRows)
        {
            var cbo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            _systemTemplate[key] = cbo;
            AddRow(label, cbo, ref y);
            y -= 4;
        }
        Controls.Add(new Label
        {
            Text = "Kategorinin sistemi chess-results'taki \"Turnuva Tipi\"nden okunur. Takım maçlarında takım adları " +
                   "kağıttaki Kulüp kutularına basılır.",
            AutoSize = false, Size = new System.Drawing.Size(408, 36), Location = new System.Drawing.Point(16, y + 2),
            ForeColor = System.Drawing.Color.Gray
        });
        y += 44;

        y = Math.Max(y, BuildRightColumn());

        // Credit
        var sep = new Label { BorderStyle = BorderStyle.Fixed3D, Location = new System.Drawing.Point(16, y), Size = new System.Drawing.Size(848, 2) };
        Controls.Add(sep); y += 8;
        // İmza: adın kendisi GitHub sayfasına götürür (adres metni gösterilmez).
        const string author = "Tunahan Dilercan";
        const string githubUrl = "https://github.com/lgndist/chess-scoresheet-printer";
        var version = typeof(SettingsForm).Assembly.GetName().Version;
        var credit = new LinkLabel
        {
            AutoSize = true, Location = new System.Drawing.Point(16, y + 4),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70),
            LinkColor = System.Drawing.Color.FromArgb(95, 122, 70),
            ActiveLinkColor = System.Drawing.Color.FromArgb(118, 150, 86),
            LinkBehavior = LinkBehavior.AlwaysUnderline,
            Text = $"♞ Geliştiren: {author} — satranç oyuncusu ve hakemi.  (v{version?.ToString(3)})"
        };
        credit.LinkArea = new LinkArea(credit.Text.IndexOf(author, StringComparison.Ordinal), author.Length);
        credit.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(githubUrl) { UseShellExecute = true }); }
            catch { /* yok say */ }
        };
        new ToolTip().SetToolTip(credit, "GitHub sayfasını aç");
        Controls.Add(credit);
        y += 36;

        var ok = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Size = new System.Drawing.Size(100, 32) };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Size = new System.Drawing.Size(100, 32) };
        ok.Location = new System.Drawing.Point(658, y);
        cancel.Location = new System.Drawing.Point(766, y);
        ok.Click += (_, _) => WriteBack();
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new System.Drawing.Size(880, y + 46);

        LoadFrom();
        ComboKeySearch.AttachAll(this); // harfle seçim (I → Iğdır, Isparta …)
    }

    // ================= Sağ sütun =================
    private const int RX = 456, RW = 408;

    private Label Section(string text, int y) => new()
    {
        Text = text, AutoSize = true, Location = new System.Drawing.Point(RX, y),
        Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold),
        ForeColor = System.Drawing.Color.FromArgb(95, 122, 70)
    };

    private void RightRow(string label, Control input, ref int y)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Location = new System.Drawing.Point(RX, y + 4) });
        input.SetBounds(RX + 134, y, RW - 134, 25);
        Controls.Add(input);
        y += 34;
    }

    private Label Note(string text, int y, int h) => new()
    {
        Text = text, AutoSize = false, Size = new System.Drawing.Size(RW, h), Location = new System.Drawing.Point(RX, y),
        ForeColor = System.Drawing.Color.Gray
    };

    /// <summary>Bölge ve yazıcı ayarları; sütunun alt y'sini döndürür.</summary>
    private int BuildRightColumn()
    {
        int y = 16;
        Controls.Add(Section("Bölge", y)); y += 24;
        // "(seçilmedi)": varsayılan il yok → ana ekranda il kutusu boş gelir
        _province.Items.Add(NoProvinceItem);
        _province.Items.AddRange(Provinces.List.Skip(1).ToArray());
        RightRow("Varsayılan il:", _province, ref y);
        RightRow("TSF il sitesi:", _tsfSite, ref y);
        var open = new LinkLabel { Text = "Siteyi aç", AutoSize = true, Location = new System.Drawing.Point(RX + 134, y - 6) };
        open.LinkClicked += (_, _) =>
        {
            var url = _tsfSite.Text.Trim();
            if (url.StartsWith("http")) try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        };
        Controls.Add(open);
        y += 16;
        Controls.Add(Note("Program açılınca bu ilin en güncel turnuvası açılır. Yönerge/Tutanak penceresi ilin TSF sitesindeki " +
                          "yönergeyi bulup bilgilerini (son başvuru, iletişim, program saatleri) kullanır.", y, 54));
        y += 58;
        // İlin TSF logosu (ana ekranın köşesinde ve masa kartlarında kullanılır)
        _provinceLogo.Location = new System.Drawing.Point(RX, y);
        Controls.Add(_provinceLogo);
        // Logo: seçili ilin TSF logosu ya da genel TSF logosu (ana ekran köşesi, masa ve yaka kartları)
        Controls.Add(new Label { Text = "Logo:", AutoSize = true, Location = new System.Drawing.Point(RX + 68, y + 4) });
        _logoKind.SetBounds(RX + 134, y, RW - 134, 25);
        Controls.Add(_logoKind);
        Controls.Add(new Label
        {
            AutoSize = false, Size = new System.Drawing.Size(RW - 68, 30), Location = new System.Drawing.Point(RX + 68, y + 30),
            ForeColor = System.Drawing.Color.Gray,
            Text = "Ana ekran köşesi, masa ve yaka kartları için."
        });
        y += 66;
        // Kayıt listesi turnuva açılınca kendiliğinden alınır; elle düzeltme gerekirse buradan.
        var btnReg = new Button
        {
            Text = "📋 Kayıt listesi ve sporcu illeri…", Size = new System.Drawing.Size(RW, 32),
            Location = new System.Drawing.Point(RX, y), Enabled = _openRegistration is not null
        };
        btnReg.Click += (_, _) => _openRegistration?.Invoke(this);
        new ToolTip().SetToolTip(btnReg, "Açık turnuvanın TSF kayıt listesi kendiliğinden alınır. Başka bir kayıt formunu seçmek, " +
                                         "listeyi yenilemek ya da bir sporcunun ilini elle düzeltmek için açın.");
        Controls.Add(btnReg);
        y += 44;
        _province.SelectedIndexChanged += (_, _) =>
        {
            _tsfSite.Text = TsfSites.SiteFor(_province.SelectedItem as string, _cfg.TsfSites) ?? "";
            FillLogoKinds();
            ShowProvinceLogo();
        };
        _logoKind.SelectedIndexChanged += (_, _) => ShowProvinceLogo();

        Controls.Add(Section("Yazıcı", y)); y += 24;
        _printerPanel.Location = new System.Drawing.Point(RX, y);
        Controls.Add(_printerPanel);
        y += _printerPanel.GetPreferredSize(new System.Drawing.Size(RW, 0)).Height + 8;
        return y;
    }

    /// <summary>Logo seçenekleri: seçili ilin TSF logosu (il varsa) ve TSF genel logosu; seçim korunur.</summary>
    private void FillLogoKinds()
    {
        bool general = _logoKind.Items.Count == 2 ? _logoKind.SelectedIndex == 1 : _cfg.GeneralTsfLogo;
        var sel = _province.SelectedItem as string;
        bool hasProvince = sel is not null && sel != NoProvinceItem;
        _logoKind.Items.Clear();
        if (hasProvince) _logoKind.Items.Add($"TSF {sel} il logosu");
        _logoKind.Items.Add("TSF genel logosu");
        _logoKind.SelectedIndex = hasProvince && !general ? 0 : _logoKind.Items.Count - 1;
    }

    private void ShowProvinceLogo()
    {
        var sel = _province.SelectedItem as string;
        bool general = _logoKind.Items.Count < 2 || _logoKind.SelectedIndex == 1;
        var old = _provinceLogo.Image;
        try { _provinceLogo.Image = Branding.OfficialLogos.Tsf(general || sel == NoProvinceItem ? null : sel, 174); }
        catch { _provinceLogo.Image = null; }
        old?.Dispose();
    }

    private static NumericUpDown MmBox() => new()
    {
        Minimum = -15, Maximum = 15, DecimalPlaces = 1, Increment = 0.5m, TextAlign = HorizontalAlignment.Right
    };

    private void OpenTemplates(object? sender, EventArgs e)
    {
        using var dlg = new TemplatesForm(_cfg);
        dlg.ShowDialog(this);
        // Etkin şablon değişmiş olabilir; çağıran (MainForm) Kaydet'te config'i yazar.
        FillSystemCombos(); // şablon eklenmiş/silinmiş olabilir
    }

    /// <summary>Sistem → şablon listelerini doldurur; mevcut seçimi (ya da config'tekini) korur.</summary>
    private void FillSystemCombos()
    {
        foreach (var (key, cbo) in _systemTemplate)
        {
            var current = cbo.SelectedItem as string
                          ?? (_cfg.TemplateBySystem.TryGetValue(key, out var n) ? n : null);
            cbo.Items.Clear();
            cbo.Items.Add(ActiveTemplateItem);
            foreach (var t in _cfg.Templates) cbo.Items.Add(t.Name);
            cbo.SelectedItem = current is not null && cbo.Items.Contains(current) ? current : ActiveTemplateItem;
        }
    }

    /// <summary>Örnek verili tek kağıt basar: kutu hizasını ve kaydırma ayarını gerçek kağıtta görmek için.</summary>
    private void PrintAlignmentTest(object? sender, EventArgs e)
    {
        if (!_cfg.Overlay.IsConfigured)
        {
            MessageBox.Show(this, "Önce bir hazır kağıt şablonu tanımlayın.", "Şablon yok", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (t, p) = OverlayLayout.Sample();
        t = t with { Pairings = new[] { p } };
        var size = _pageSize.SelectedIndex == 0 ? "A5" : "A4";
        new SheetPrinter(_cfg.Overlay, t, size, (double)_offX.Value, (double)_offY.Value).PrintWithPreview(this);
    }

    private void AddRow(string label, Control input, ref int y)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Location = new System.Drawing.Point(16, y + 4) });
        input.SetBounds(150, y, 274, 25);
        Controls.Add(input);
        y += 34;
    }

    private void LoadFrom()
    {
        _name.Text = _cfg.Tournament.Name;
        _pageSize.SelectedIndex = NotasyonOtomasyonu.Core.PageGeometry.IsA5(_cfg.Layout.PageSize) ? 0 : 1;
        _offX.Value = Clamp(_cfg.Layout.PrintOffsetXmm, _offX);
        _offY.Value = Clamp(_cfg.Layout.PrintOffsetYmm, _offY);

        _province.SelectedItem = _province.Items.Contains(_cfg.Online.Province) ? _cfg.Online.Province : NoProvinceItem;
        _tsfSite.Text = TsfSites.SiteFor(_province.SelectedItem as string, _cfg.TsfSites) ?? "";
        FillLogoKinds();
        ShowProvinceLogo();
        _printerPanel.LoadFrom(_cfg.Printing);
        FillSystemCombos();
    }

    private static decimal Clamp(double v, NumericUpDown n)
        => Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)v));

    private void WriteBack()
    {
        _cfg.Tournament.Name = _name.Text.Trim();
        _cfg.Layout.PageSize = _pageSize.SelectedIndex == 0 ? "A5" : "A4";
        _cfg.Layout.PrintOffsetXmm = (double)_offX.Value;
        _cfg.Layout.PrintOffsetYmm = (double)_offY.Value;

        // İl seçiliyken ikinci seçenek genel logodur; il yokken tek seçenek (genel) — o durumda tercih korunur.
        if (_logoKind.Items.Count == 2) _cfg.GeneralTsfLogo = _logoKind.SelectedIndex == 1;
        if (_province.SelectedItem is string sel)
        {
            var prov = sel == NoProvinceItem ? Provinces.All : sel;
            _cfg.Online.Province = prov;
            // TSF sitesi elle değiştirildiyse il için kaydedilir; gömülü adrese dönüldüyse kayıt silinir.
            var url = _tsfSite.Text.Trim().TrimEnd('/');
            if (prov != Provinces.All)
            {
                if (url.Length == 0 || url == TsfSites.SiteFor(prov)) _cfg.TsfSites.Remove(prov);
                else _cfg.TsfSites[prov] = url;
            }
        }
        _printerPanel.SaveTo(_cfg.Printing);
        foreach (var (key, cbo) in _systemTemplate)
        {
            if (cbo.SelectedItem is string name && name != ActiveTemplateItem) _cfg.TemplateBySystem[key] = name;
            else _cfg.TemplateBySystem.Remove(key);
        }
    }
}
