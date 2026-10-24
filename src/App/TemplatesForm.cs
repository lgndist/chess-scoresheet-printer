using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.App.TemplateDesigner;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Hazır kağıt şablonu kütüphanesi: birden çok notasyon kağıdı tutulur; biri ETKİN'dir.
/// Kullanıcı düzenleyebilir, yeni ekleyebilir, çoğaltabilir, silebilir ve etkin yapabilir.
/// Etkin şablon = <see cref="AppConfig.Overlay"/>.
/// </summary>
public sealed class TemplatesForm : Form
{
    private readonly AppConfig _cfg;
    private readonly Func<(string Size, double X, double Y)> _print;   // kağıt boyutu ve yazıcı kaydırması
    private readonly ListBox _list = new();
    private readonly Label _info = new();

    public TemplatesForm(AppConfig cfg, Func<(string Size, double X, double Y)>? print = null)
    {
        _cfg = cfg;
        _print = print ?? (() => (cfg.Layout.PageSize, cfg.Layout.PrintOffsetXmm, cfg.Layout.PrintOffsetYmm));

        Text = "🎨 Hazır Kağıt Şablonları";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        ClientSize = new System.Drawing.Size(520, 384);

        Controls.Add(new Label
        {
            Text = "Notasyon kağıtlarınız. Etkin olan baskıda kullanılır (✓).",
            AutoSize = true, Location = new System.Drawing.Point(16, 12), ForeColor = System.Drawing.Color.Gray
        });

        _list.SetBounds(16, 38, 320, 294);
        _list.IntegralHeight = false;
        _list.SelectedIndexChanged += (_, _) => UpdateInfo();
        _list.DoubleClick += (_, _) => EditSelected();
        Controls.Add(_list);

        _info.SetBounds(16, 338, 320, 36);
        _info.ForeColor = System.Drawing.Color.FromArgb(95, 122, 70);
        Controls.Add(_info);

        int bx = 352, by = 38;
        AddBtn("✓ Etkin Yap", bx, ref by, MakeActive, primary: true);
        AddBtn("✏ Düzenle…", bx, ref by, (_, _) => EditSelected());
        AddBtn("➕ Yeni Ekle…", bx, ref by, AddNew);
        AddBtn("⧉ Çoğalt", bx, ref by, Duplicate);
        AddBtn("🗑 Sil", bx, ref by, Delete);
        by += 12;
        AddBtn("↺ Varsayılana Sıfırla", bx, ref by, ResetDefault);
        by += 6;
        AddBtn("🖨 Hizalama testi", bx, ref by, (_, _) =>
        {
            var t = Selected; if (t is null) return;
            var (size, x, y) = _print();
            AlignmentTest.Print(this, IsActive(t) ? _cfg.Overlay : t, size, x, y);
        });

        var close = new Button { Text = "Kapat", DialogResult = DialogResult.OK, Size = new System.Drawing.Size(152, 30), Location = new System.Drawing.Point(bx, 344) };
        Controls.Add(close);
        AcceptButton = close;

        RefreshList(_cfg.Overlay.Name);
    }

    private void AddBtn(string text, int x, ref int y, EventHandler onClick, bool primary = false)
    {
        var b = new Button { Text = text, Size = new System.Drawing.Size(152, 30), Location = new System.Drawing.Point(x, y), TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
        if (primary) { b.BackColor = System.Drawing.Color.FromArgb(118, 150, 86); b.ForeColor = System.Drawing.Color.White; b.FlatStyle = FlatStyle.Flat; }
        b.Click += onClick;
        Controls.Add(b);
        y += 36;
    }

    private void RefreshList(string? selectName)
    {
        _list.Items.Clear();
        foreach (var t in _cfg.Templates)
        {
            bool active = IsActive(t);
            string mark = active ? "✓ " : "   ";
            int fields = t.Fields.Count;
            _list.Items.Add($"{mark}{t.Name}  ({fields} alan){AlignmentTest.Uses(_cfg, t)}");
        }
        // seçimi koru
        int idx = selectName is null ? -1 : _cfg.Templates.FindIndex(t => t.Name == selectName);
        if (idx < 0 && _cfg.Templates.Count > 0) idx = 0;
        if (idx >= 0) _list.SelectedIndex = idx;
        UpdateInfo();
    }

    private bool IsActive(OverlayTemplate t) =>
        ReferenceEquals(t, _cfg.Overlay) ||
        (t.Name == _cfg.Overlay.Name && t.Fields.Count == _cfg.Overlay.Fields.Count);

    private OverlayTemplate? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _cfg.Templates.Count
            ? _cfg.Templates[_list.SelectedIndex] : null;

    private void UpdateInfo()
    {
        var t = Selected;
        if (t is null) { _info.Text = ""; return; }
        var bg = string.IsNullOrWhiteSpace(t.BackgroundImagePath) ? "arka plan yok" : Path.GetFileName(t.BackgroundImagePath);
        _info.Text = (IsActive(t) ? "ETKİN — " : "") + $"{bg}";
    }

    private void MakeActive(object? sender, EventArgs e)
    {
        var t = Selected; if (t is null) return;
        _cfg.Overlay = t.DeepClone();
        RefreshList(t.Name);
    }

    private void EditSelected()
    {
        var t = Selected; if (t is null) return;
        // Ayarlar'da seçili (henüz kaydedilmemiş olabilir) kağıt boyutuyla: A5 basılacaksa A5'e göre düzenlenir.
        using var dlg = new TemplateDesignerForm(t.DeepClone(), _print().Size);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var edited = dlg.Result;
        if (string.IsNullOrWhiteSpace(edited.Name)) edited.Name = t.Name;
        int i = _cfg.Templates.IndexOf(t);
        bool wasActive = IsActive(t);
        _cfg.Templates[i] = edited;
        if (wasActive) _cfg.Overlay = edited.DeepClone();
        RefreshList(edited.Name);
    }

    private void AddNew(object? sender, EventArgs e)
    {
        var name = Prompt("Yeni şablon adı:", "Yeni Şablon");
        if (name is null) return;
        var tpl = new OverlayTemplate { Name = UniqueName(name) };
        using var dlg = new TemplateDesignerForm(tpl, _cfg.Layout.PageSize);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var made = dlg.Result;
        made.Name = tpl.Name;
        _cfg.Templates.Add(made);
        if (_cfg.Templates.Count == 1) _cfg.Overlay = made.DeepClone();
        RefreshList(made.Name);
    }

    private void Duplicate(object? sender, EventArgs e)
    {
        var t = Selected; if (t is null) return;
        var copy = t.DeepClone();
        copy.Name = UniqueName(t.Name + " (kopya)");
        _cfg.Templates.Add(copy);
        RefreshList(copy.Name);
    }

    private void Delete(object? sender, EventArgs e)
    {
        var t = Selected; if (t is null) return;
        if (_cfg.Templates.Count <= 1)
        {
            MessageBox.Show("En az bir şablon kalmalı.", "Silinemez", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show($"“{t.Name}” silinsin mi?", "Sil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        bool wasActive = IsActive(t);
        _cfg.Templates.Remove(t);
        if (wasActive) _cfg.Overlay = _cfg.Templates[0].DeepClone();
        RefreshList(_cfg.Overlay.Name);
    }

    private void ResetDefault(object? sender, EventArgs e)
    {
        // Seçili şablon yerleşikse onu, değilse varsayılanı ("Ana Örnek 2") sıfırla.
        var name = Selected?.Name is OverlayDefaults.LegacyName or OverlayDefaults.TsfName
            ? Selected!.Name : OverlayDefaults.DefaultName;
        if (MessageBox.Show($"“{name}” şablonu ilk haline sıfırlansın mı? (Bu şablondaki değişiklikler kaybolur.)",
            "Sıfırla", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        var bg = OverlayDefaults.ExtractBackground(AppPaths.DataDir);
        var def = OverlayDefaults.BuildBuiltIn(name, bg)!;
        int i = _cfg.Templates.FindIndex(t => t.Name == name);
        if (i >= 0) _cfg.Templates[i] = def; else _cfg.Templates.Add(def);
        _cfg.Overlay = def.DeepClone();
        RefreshList(def.Name);
    }

    private string UniqueName(string baseName)
    {
        var name = baseName.Trim();
        if (name.Length == 0) name = "Şablon";
        int n = 2; var candidate = name;
        while (_cfg.Templates.Any(t => string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = $"{name} {n++}";
        return candidate;
    }

    private static string? Prompt(string label, string def)
    {
        using var f = new Form
        {
            Text = "Şablon Adı", FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false,
            ClientSize = new System.Drawing.Size(340, 110), Font = new System.Drawing.Font("Segoe UI", 9.75F)
        };
        f.Controls.Add(new Label { Text = label, AutoSize = true, Location = new System.Drawing.Point(14, 16) });
        var tb = new TextBox { Text = def };
        tb.SetBounds(14, 40, 312, 25);
        f.Controls.Add(tb);
        var ok = new Button { Text = "Tamam", DialogResult = DialogResult.OK, Location = new System.Drawing.Point(150, 72), Size = new System.Drawing.Size(80, 28) };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Location = new System.Drawing.Point(238, 72), Size = new System.Drawing.Size(80, 28) };
        f.Controls.Add(ok); f.Controls.Add(cancel);
        f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK && tb.Text.Trim().Length > 0 ? tb.Text.Trim() : null;
    }
}
