using NotasyonOtomasyonu.Online;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Sık değişmeyen ama kalıcı olması gereken ayarlar, üstte sekmelerle: Notasyon (turnuva adı, sayfa
/// boyutu, yazıcı hizalaması), Şablonlar, Bölge (il, TSF sitesi, logo, kayıt listesi), Yazıcı ve
/// Güncelleme. Kaydet'e basınca verilen <see cref="AppConfig"/> nesnesine yazar (çağıran kaydeder).
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;

    private readonly TextBox _name = new();
    private readonly ComboBox _pageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _offX = MmBox();
    private readonly NumericUpDown _offY = MmBox();
    private readonly Dictionary<string, ComboBox> _systemTemplate = new();
    private readonly ComboBox _testTemplate = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    // Sağ sütun: bölge (varsayılan il, TSF sitesi) ve yazıcı
    private readonly ComboBox _province = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _tsfSite = new();
    private readonly PrinterSettingsPanel _printerPanel = new(RW);
    private readonly PictureBox _provinceLogo = new() { SizeMode = PictureBoxSizeMode.CenterImage, Size = new System.Drawing.Size(58, 58) };
    private readonly ComboBox _logoKind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Action<IWin32Window>? _openRegistration;
    private readonly Action<IWin32Window>? _openUpdates;
    private readonly Action? _saveNow;          // şablon değişikliklerini hemen diske yaz
    private readonly string? _configPath;
    private readonly CheckBox _autoUpdate = new() { Text = "Program açılınca yeni sürümü denetle (günde bir kez)", AutoSize = true };
    private readonly TabControl _tabs = new() { Location = new System.Drawing.Point(12, 12) };
    private Control _host = null!;                             // kurulurken denetimlerin eklendiği sekme
    private const int TabW = 456;                              // sekme genişliği (içerik 16 + 408 + pay)
    private const string NoProvinceItem = "(seçilmedi)";

    private const string ActiveTemplateItem = "(etkin şablon)";
    private static readonly (string Key, string Label)[] SystemRows =
    {
        ("Swiss", "İsviçre sistemi:"),
        ("RoundRobin", "Berger (döner):"),
        ("Team", "Takım turnuvası:"),
    };

    public SettingsForm(AppConfig cfg, Action<IWin32Window>? openRegistration = null, Action<IWin32Window>? openUpdates = null,
                        Action? saveNow = null, string? configPath = null)
    {
        _cfg = cfg;
        _openRegistration = openRegistration;
        _openUpdates = openUpdates;
        _saveNow = saveNow;
        _configPath = configPath;

        Text = "⚙ Ayarlar";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);

        _tabs.Font = Font;
        _tabs.Padding = new System.Drawing.Point(14, 5);
        TabPage Page(string text)
        {
            var page = new TabPage(text) { BackColor = System.Drawing.Color.FromArgb(250, 250, 247), UseVisualStyleBackColor = false };
            _tabs.TabPages.Add(page);
            return page;
        }
        var pNotation = Page("Notasyon");
        var pTemplates = Page("Şablonlar");
        var pRegion = Page("Bölge");
        var pPrinter = Page("Yazıcı");
        var pUpdate = Page("Güncelleme");
        Controls.Add(_tabs);
        _host = pNotation;

        int y = 16;
        AddRow("Varsayılan turnuva adı:", _name, ref y);

        _pageSize.Items.AddRange(new object[] { "A5 (önerilen)", "A4" });
        AddRow("Sayfa boyutu:", _pageSize, ref y);

        // ---- Yazıcı hizalama ----
        y += 4;
        _host.Controls.Add(new Label
        {
            Text = "Yazıcı hizalama (mm)", AutoSize = true, Location = new System.Drawing.Point(16, y),
            Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70)
        });
        y += 24;
        _host.Controls.Add(new Label { Text = "Sağa (+) / sola (−):", AutoSize = true, Location = new System.Drawing.Point(16, y + 4) });
        _offX.SetBounds(150, y, 70, 25); _host.Controls.Add(_offX);
        _host.Controls.Add(new Label { Text = "Aşağı (+) / yukarı (−):", AutoSize = true, Location = new System.Drawing.Point(230, y + 4) });
        _offY.SetBounds(374, y, 62, 25); _host.Controls.Add(_offY);
        y += 32;
        _host.Controls.Add(new Label
        {
            Text = "Yazılar hazır kağıttaki kutulara göre kayıksa buradan düzeltin (±40 mm); şablonu değiştirmeniz gerekmez. " +
                   "Önce hizalama testini boş bir hazır kağıda basıp ölçün. 25–30 mm'lik kayma çoğunlukla tepsinin A4'te kalmasıdır.",
            AutoSize = false, Size = new System.Drawing.Size(408, 72), Location = new System.Drawing.Point(16, y),
            ForeColor = System.Drawing.Color.Gray
        });
        y += 76;
        AddRow("Test edilecek şablon:", _testTemplate, ref y);
        y -= 4;
        var btnTest = new Button
        {
            Text = "🖨 Hizalama testi yazdır (örnek tek kağıt)…",
            Size = new System.Drawing.Size(408, 32), Location = new System.Drawing.Point(16, y)
        };
        btnTest.Click += PrintAlignmentTest;
        _host.Controls.Add(btnTest);
        y += 44;
        int yNotation = y;

        // ---- Şablonlar sekmesi: hazır kağıt (overlay) şablonu tasarımcısı ----
        _host = pTemplates;
        y = 16;
        var btnTemplate = new Button
        {
            Text = "🎨 Hazır kağıt şablonları (ekle / düzenle)…",
            Size = new System.Drawing.Size(408, 32),
            Location = new System.Drawing.Point(16, y)
        };
        btnTemplate.Click += OpenTemplates;
        _host.Controls.Add(btnTemplate);
        y += 42;

        // ---- Sisteme göre şablon ----
        _host.Controls.Add(new Label
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
        _host.Controls.Add(new Label
        {
            Text = "Kategorinin sistemi chess-results'taki \"Turnuva Tipi\"nden okunur. Takım maçlarında takım adları " +
                   "kağıttaki Kulüp kutularına basılır.",
            AutoSize = false, Size = new System.Drawing.Size(408, 36), Location = new System.Drawing.Point(16, y + 2),
            ForeColor = System.Drawing.Color.Gray
        });
        y += 44;
        int yTemplates = y;

        _host = pRegion;
        int yRegion = BuildRegion();
        _host = pPrinter;
        int yPrinter = BuildPrinter();
        _host = pUpdate;
        int yUpdate = BuildUpdates();
        int tabContent = new[] { yNotation, yTemplates, yRegion, yPrinter, yUpdate }.Max() + 12;
        _tabs.Size = new System.Drawing.Size(TabW, tabContent + _tabs.ItemSize.Height + 12);

        // ---- Sekmelerin altı: imza ve düğmeler ----
        _host = this;
        y = _tabs.Bottom + 10;
        // İmza: adın kendisi GitHub sayfasına götürür (adres metni gösterilmez).
        const string author = "Tunahan Dilercan";
        const string githubUrl = "https://github.com/lgndist/chess-scoresheet-printer";
        var version = typeof(SettingsForm).Assembly.GetName().Version;
        var credit = new LinkLabel
        {
            AutoSize = false, Size = new System.Drawing.Size(TabW, 22), Location = new System.Drawing.Point(12, y),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70),
            LinkColor = System.Drawing.Color.FromArgb(95, 122, 70),
            ActiveLinkColor = System.Drawing.Color.FromArgb(118, 150, 86),
            LinkBehavior = LinkBehavior.AlwaysUnderline,
            Text = $"♞ Geliştiren: {author} — satranç oyuncusu ve hakemi. (v{version?.ToString(3)})"
        };
        credit.LinkArea = new LinkArea(credit.Text.IndexOf(author, StringComparison.Ordinal), author.Length);
        credit.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(githubUrl) { UseShellExecute = true }); }
            catch { /* yok say */ }
        };
        new ToolTip().SetToolTip(credit, "GitHub sayfasını aç");
        _host.Controls.Add(credit);
        y += 30;

        var ok = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Size = new System.Drawing.Size(100, 32) };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Size = new System.Drawing.Size(100, 32) };
        cancel.Location = new System.Drawing.Point(_tabs.Right - 100, y);
        ok.Location = new System.Drawing.Point(cancel.Left - 108, y);
        ok.Click += (_, _) => WriteBack();
        _host.Controls.Add(ok);
        _host.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new System.Drawing.Size(_tabs.Right + 12, y + 46);

        LoadFrom();
        ComboKeySearch.AttachAll(this); // harfle seçim (I → Iğdır, Isparta …)
    }

    // ================= Bölge / Yazıcı / Güncelleme sekmeleri =================
    private const int RX = 16, RW = 408;

    private Label Section(string text, int y) => new()
    {
        Text = text, AutoSize = true, Location = new System.Drawing.Point(RX, y),
        Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold),
        ForeColor = System.Drawing.Color.FromArgb(95, 122, 70)
    };

    private void RightRow(string label, Control input, ref int y)
    {
        _host.Controls.Add(new Label { Text = label, AutoSize = true, Location = new System.Drawing.Point(RX, y + 4) });
        input.SetBounds(RX + 134, y, RW - 134, 25);
        _host.Controls.Add(input);
        y += 34;
    }

    private Label Note(string text, int y, int h) => new()
    {
        Text = text, AutoSize = false, Size = new System.Drawing.Size(RW, h), Location = new System.Drawing.Point(RX, y),
        ForeColor = System.Drawing.Color.Gray
    };

    /// <summary>Bölge sekmesi; alt y'sini döndürür.</summary>
    private int BuildRegion()
    {
        int y = 16;
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
        _host.Controls.Add(open);
        y += 16;
        _host.Controls.Add(Note("Program açılınca bu ilin en güncel turnuvası açılır. Yönerge/Tutanak penceresi ilin TSF sitesindeki " +
                          "yönergeyi bulup bilgilerini (son başvuru, iletişim, program saatleri) kullanır.", y, 54));
        y += 58;
        // İlin TSF logosu (ana ekranın köşesinde ve masa kartlarında kullanılır)
        _provinceLogo.Location = new System.Drawing.Point(RX, y);
        _host.Controls.Add(_provinceLogo);
        // Logo: seçili ilin TSF logosu ya da genel TSF logosu (ana ekran köşesi, masa ve yaka kartları)
        _host.Controls.Add(new Label { Text = "Logo:", AutoSize = true, Location = new System.Drawing.Point(RX + 68, y + 4) });
        _logoKind.SetBounds(RX + 134, y, RW - 134, 25);
        _host.Controls.Add(_logoKind);
        _host.Controls.Add(new Label
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
        _host.Controls.Add(btnReg);
        y += 44;
        _province.SelectedIndexChanged += (_, _) =>
        {
            _tsfSite.Text = TsfSites.SiteFor(_province.SelectedItem as string, _cfg.TsfSites) ?? "";
            FillLogoKinds();
            ShowProvinceLogo();
        };
        _logoKind.SelectedIndexChanged += (_, _) => ShowProvinceLogo();
        return y;
    }

    /// <summary>Yazıcı sekmesi.</summary>
    private int BuildPrinter()
    {
        int y = 16;
        _printerPanel.Location = new System.Drawing.Point(RX, y);
        _host.Controls.Add(_printerPanel);
        return y + _printerPanel.GetPreferredSize(new System.Drawing.Size(RW, 0)).Height + 8;
    }

    /// <summary>Güncelleme sekmesi: sürüm, elle denetim, açılışta denetim.</summary>
    private int BuildUpdates()
    {
        int y = 16;
        _host.Controls.Add(new Label
        {
            Text = $"Kullandığınız sürüm: v{NotasyonOtomasyonu.App.Update.Updater.Current}", AutoSize = true, Location = new System.Drawing.Point(RX, y),
            Font = new System.Drawing.Font("Segoe UI", 10.5F, System.Drawing.FontStyle.Bold), ForeColor = System.Drawing.Color.FromArgb(95, 122, 70)
        });
        y += 32;
        var btn = new Button
        {
            Text = "⬆ Güncellemeleri denetle…", Size = new System.Drawing.Size(RW, 34), Location = new System.Drawing.Point(RX, y),
            Enabled = _openUpdates is not null
        };
        btn.Click += (_, _) => { WriteBack(); _openUpdates?.Invoke(this); };
        _host.Controls.Add(btn);
        y += 44;
        _autoUpdate.Location = new System.Drawing.Point(RX, y);
        _host.Controls.Add(_autoUpdate);
        y += 30;
        _host.Controls.Add(Note("Yeni sürüm GitHub'dan indirilir, doğrulanır ve programın yerine kurulur; program yeni sürümle " +
                                "yeniden açılır. Ayarlarınız, şablonlarınız ve çıktılarınız korunur. " +
                                (_cfg.Updates.LastCheck is { } last ? $"Son denetim: {last:dd.MM.yyyy HH:mm}." : ""), y, 72));
        y += 76;
        if (_configPath is not null)
        {
            // Ayarların (şablonlar dahil) kaydedildiği yer; klasör açılabilir (yedek almak için).
            _host.Controls.Add(Note($"Ayarlar, şablonlar ve eklediğiniz afiş/logo/fotoğraflar: {Path.GetDirectoryName(_configPath)} " +
                                    "(her kayıtta ayarların önceki hâli config.json.bak olarak saklanır).", y, 54));
            y += 56;
            var open = new LinkLabel { Text = "📂 Ayar klasörünü aç", AutoSize = true, Location = new System.Drawing.Point(RX, y) };
            open.LinkClicked += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.GetDirectoryName(_configPath)!) { UseShellExecute = true }); }
                catch { /* yok say */ }
            };
            _host.Controls.Add(open);
            y += 26;
        }
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
        // Kutunun gerçek piksel boyutunda (ölçeklenmeden, keskin)
        try { _provinceLogo.Image = Branding.OfficialLogos.Tsf(general || sel == NoProvinceItem ? null : sel, Math.Min(_provinceLogo.ClientSize.Width, _provinceLogo.ClientSize.Height)); }
        catch { _provinceLogo.Image = null; }
        old?.Dispose();
    }

    private static NumericUpDown MmBox() => new()
    {
        Minimum = -40, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m, TextAlign = HorizontalAlignment.Right
    };

    private void OpenTemplates(object? sender, EventArgs e)
    {
        // Şablon listesindeki hizalama testi bu penceredeki (henüz kaydedilmemiş) kağıt boyutu ve kaydırmayı kullanır.
        using var dlg = new TemplatesForm(_cfg, () => (_pageSize.SelectedIndex == 0 ? "A5" : "A4", (double)_offX.Value, (double)_offY.Value));
        dlg.ShowDialog(this);
        // Şablon değişiklikleri pencere kapanınca hemen kaydedilir (Ayarlar'da "Vazgeç"e basılsa da kaybolmaz).
        _saveNow?.Invoke();
        FillSystemCombos(); // şablon eklenmiş/silinmiş olabilir
        FillTestTemplates();
    }

    /// <summary>Hizalama testi şablon listesi: kütüphanedeki şablonlar, nerede kullanıldıklarıyla; varsayılan etkin şablon.</summary>
    private void FillTestTemplates()
    {
        var keep = _testTemplate.SelectedIndex;
        _testTemplate.Items.Clear();
        foreach (var t in _cfg.Templates) _testTemplate.Items.Add(t.Name + AlignmentTest.Uses(_cfg, t));
        int active = _cfg.Templates.FindIndex(t => t.Name == _cfg.Overlay.Name);
        _testTemplate.SelectedIndex = keep >= 0 && keep < _testTemplate.Items.Count ? keep
            : active >= 0 ? active : (_testTemplate.Items.Count > 0 ? 0 : -1);
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
        // Seçilen şablon; etkin şablon seçiliyse baskıda kullanılan kopyası (Overlay).
        int i = _testTemplate.SelectedIndex;
        var tpl = i >= 0 && i < _cfg.Templates.Count && _cfg.Templates[i].Name != _cfg.Overlay.Name ? _cfg.Templates[i] : _cfg.Overlay;
        AlignmentTest.Print(this, tpl, _pageSize.SelectedIndex == 0 ? "A5" : "A4", (double)_offX.Value, (double)_offY.Value);
    }

    private void AddRow(string label, Control input, ref int y)
    {
        _host.Controls.Add(new Label { Text = label, AutoSize = true, Location = new System.Drawing.Point(16, y + 4) });
        input.SetBounds(172, y, 252, 25);
        _host.Controls.Add(input);
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
        _autoUpdate.Checked = _cfg.Updates.AutoCheck;
        FillSystemCombos();
        FillTestTemplates();
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
        _cfg.Updates.AutoCheck = _autoUpdate.Checked;
        foreach (var (key, cbo) in _systemTemplate)
        {
            if (cbo.SelectedItem is string name && name != ActiveTemplateItem) _cfg.TemplateBySystem[key] = name;
            else _cfg.TemplateBySystem.Remove(key);
        }
    }
}
