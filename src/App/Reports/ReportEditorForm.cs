using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>Düzenleme modunda üretilmiş belge: Word'ün PDF'i, sayfa görüntüleri ve alanların sayfadaki kutuları.</summary>
public sealed record EditorRender(string PdfPath, List<Bitmap> Pages, List<SizeF> SizesPt, List<FieldBox> Boxes);

/// <summary>
/// Sayfada düzenlenebilen bir yer: tekil alan ("TELEFON") ya da tablo hücresi ("program[2].saat").
/// Değer rapor penceresindeki kaynağından okunur/yazılır (alan tablosu, kategori/program sekmesi…).
/// </summary>
public sealed class EditTarget
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    /// <summary>Boşken sayfada görünen kısa ad ("[Saat]").</summary>
    public string Placeholder { get; init; } = "";
    public string Hint { get; init; } = "";
    public bool Optional { get; init; }
    public bool Multiline { get; init; }
    public required Func<string> Get { get; init; }
    public required Action<string> Set { get; init; }
    public string Value => Get() ?? "";
}

/// <summary>Düzenleme ekranının rapor penceresinden aldığı bağlam. Hedefler her yenilemede yeniden alınır.</summary>
public sealed record ReportEditorContext(
    string Title,
    Func<IReadOnlyList<EditTarget>> Targets,
    Func<WordSession, Task<EditorRender>> Render,
    Func<WordSession, IWin32Window, Task<string?>> Print,
    Func<WordSession, IWin32Window, Task<string?>> SavePdf,
    Func<IWin32Window, string?> SaveDocx);

/// <summary>
/// "Önizle ve Düzenle": belge Word'deki gibi tam sayfa görünür (Word'ün kendi çıktısı); doldurulacak
/// alanlar sarı (boşsa kırmızımsı, "[Telefon]" gibi) işaretlidir. Alana tıklayıp doğrudan yazılır;
/// şablonun başlıkları, logosu ve sabit metni kilitlidir. Her düzenlemeden kısa süre sonra sayfa Word
/// ile yeniden üretilir (Word bu pencere açıkken açık tutulur, yenileme hızlıdır).
/// </summary>
public sealed class ReportEditorForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Desk = Color.FromArgb(222, 222, 218);
    private static readonly Color FieldFill = Color.FromArgb(60, 255, 214, 0);
    private static readonly Color FieldHover = Color.FromArgb(110, 255, 196, 0);
    private static readonly Color EmptyFill = Color.FromArgb(55, 230, 60, 40);
    private const int PageGap = 24;

    private readonly ReportEditorContext _ctx;
    private readonly WordSession _word = new();
    private readonly Panel _scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Desk };
    private readonly PageCanvas _canvas;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 40, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, ForeColor = Color.FromArgb(70, 70, 70), BackColor = Color.FromArgb(245, 245, 240), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), AutoEllipsis = true };
    private readonly Label _zoomLabel = new() { AutoSize = true, Margin = new Padding(4, 14, 4, 0) };
    private readonly ToolTip _tip = new() { InitialDelay = 250 };
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 700 };

    private EditorRender? _render;
    private readonly List<RectangleF> _pageRects = new();   // ekran (tuval) koordinatlarında sayfalar
    private readonly Dictionary<string, string> _pending = new(StringComparer.OrdinalIgnoreCase); // yazıldı, sayfa henüz yenilenmedi
    private float _zoom = -1;          // -1 = sayfa genişliğine sığdır
    private string? _hoverKey;
    private TextBox? _editor;
    private EditTarget? _editing;
    private IReadOnlyList<EditTarget> _targets = Array.Empty<EditTarget>();
    // Geri al / yinele: (hedef, eski değer, yeni değer)
    private readonly Stack<(EditTarget Target, string Old, string New)> _undo = new(), _redo = new();
    private Button? _btnUndo, _btnRedo;
    private bool _rendering, _renderAgain, _closing;

    public ReportEditorForm(ReportEditorContext ctx)
    {
        _ctx = ctx;
        _canvas = new PageCanvas(this);
        Text = "Önizle ve Düzenle — " + ctx.Title;
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(900, 600);
        KeyPreview = true;
        _targets = ctx.Targets();
        BuildLayout();

        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RenderAsync(); };
        _scroll.Resize += (_, _) => { if (_zoom < 0) Relayout(); };
        Shown += async (_, _) =>
        {
            _ = _word.WarmUpAsync();
            await RenderAsync();
        };
        FormClosing += (_, _) => { _closing = true; _debounce.Stop(); CommitEdit(); };
        FormClosed += (_, _) => { _debounce.Dispose(); _word.Dispose(); DisposeRender(_render); _render = null; };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.P) { e.Handled = true; _ = PrintAsync(); }
            else if (e.KeyCode == Keys.Escape && _editor is null) Close();
            else if (e.KeyCode == Keys.F3) { e.Handled = true; GoToNextEmpty(); }
            else if (_editor is null && e.Control && e.KeyCode == Keys.Z && !e.Shift) { e.Handled = true; Undo(); }
            else if (_editor is null && e.Control && (e.KeyCode == Keys.Y || (e.KeyCode == Keys.Z && e.Shift))) { e.Handled = true; Redo(); }
        };
    }

    // ================= Yerleşim =================
    private void BuildLayout()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 7, 10, 0), BackColor = Color.FromArgb(245, 245, 240), WrapContents = false };
        var print = Btn(PrintRouter.IsSilent ? "🖨  Yazdır (doğrudan)" : "🖨  Yazdır", 170, primary: true);
        var pdf = Btn("📄 PDF kaydet", 130);
        var docx = Btn("Word (.docx)", 110);
        var close = Btn("Kapat", 80);
        var zoomOut = Btn("−", 34);
        var zoomFit = Btn("Sığdır", 70);
        var zoomIn = Btn("+", 34);
        var next = Btn("Sonraki boş alan ▶ (F3)", 190);
        _btnUndo = Btn("↶ Geri al", 96);
        _btnRedo = Btn("↷ Yinele", 90);
        _tip.SetToolTip(_btnUndo, "Son değişikliği geri al (Ctrl+Z)");
        _tip.SetToolTip(_btnRedo, "Geri alınanı yinele (Ctrl+Y)");
        _btnUndo.Click += (_, _) => Undo();
        _btnRedo.Click += (_, _) => Redo();
        UpdateUndoButtons();
        bar.Controls.AddRange(new Control[] { print, pdf, docx, close, Gap(), _btnUndo, _btnRedo, Gap(), zoomOut, _zoomLabel, zoomIn, zoomFit, Gap(), next });
        print.Click += async (_, _) => await PrintAsync();
        pdf.Click += async (_, _) => await SavePdfAsync();
        docx.Click += (_, _) => { CommitEdit(); var s = _ctx.SaveDocx(this); if (s is not null) _status.Text = s; };
        close.Click += (_, _) => Close();
        zoomIn.Click += (_, _) => SetZoom(CurrentZoom() * 1.15f);
        zoomOut.Click += (_, _) => SetZoom(CurrentZoom() / 1.15f);
        zoomFit.Click += (_, _) => SetZoom(-1);
        next.Click += (_, _) => GoToNextEmpty();

        var side = new Panel { Dock = DockStyle.Right, Width = 300, BackColor = Color.White, Padding = new Padding(10, 8, 6, 8) };
        side.Controls.Add(_list);
        side.Controls.Add(new Label
        {
            Dock = DockStyle.Top, Height = 118, ForeColor = Color.DimGray,
            Text = "Sarı alanlara tıklayıp yazın: Enter tamam, Tab sonraki, Esc vazgeç. " +
                   "Kırmızımsı alanlar boş. Başlık, logo ve sabit metin kilitli. " +
                   "Tablo hücreleri de tıklanıp düzenlenir. Ctrl+Z geri al, Ctrl+Y yinele."
        });
        side.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = "Alanlar", Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark });
        _list.DrawItem += DrawListItem;
        _list.Click += (_, _) => { if (_list.SelectedItem is EditTarget f) FocusField(f, edit: true); };

        _scroll.Controls.Add(_canvas);
        Controls.Add(_scroll);
        Controls.Add(side);
        Controls.Add(bar);
        Controls.Add(_status);
        FillList();
    }

    private static Control Gap() => new Label { Width = 14 };

    private Button Btn(string text, int width, bool primary = false)
    {
        var b = new Button { Text = text, Width = width, Height = 36, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new Padding(3, 0, 3, 0) };
        b.BackColor = primary ? Green : Color.White;
        b.ForeColor = primary ? Color.White : Color.Black;
        b.FlatAppearance.BorderColor = primary ? GreenDark : Color.FromArgb(208, 208, 198);
        if (primary) b.Font = new Font(Font, FontStyle.Bold);
        return b;
    }

    // ================= Belge üretimi =================
    private async Task RenderAsync()
    {
        if (_closing) return;
        if (_rendering) { _renderAgain = true; return; }
        _rendering = true;
        _status.Text = _render is null ? "Belge hazırlanıyor (Word başlatılıyor, ilk açılış biraz sürebilir)…" : "⟳ Sayfa güncelleniyor…";
        UseWaitCursor = _render is null;
        try
        {
            var r = await _ctx.Render(_word);
            if (_closing) { DisposeRender(r); return; }
            var old = _render;
            _render = r;
            _targets = _ctx.Targets();
            // Yalnız bu üretimden SONRA yazılanlar bekleyen olarak kalır.
            if (!_renderAgain) _pending.Clear();
            DisposeRender(old);
            Relayout();
            FillList();
            int empty = _targets.Count(f => !f.Optional && string.IsNullOrWhiteSpace(f.Value) && HasBox(f.Key));
            _status.Text = $"✓ Güncel • {r.Pages.Count} sayfa" + (empty > 0 ? $" • {empty} boş alan" : " • boş alan yok");
        }
        catch (Exception ex)
        {
            if (!_closing && !IsDisposed) _status.Text = "Belge üretilemedi: " + (ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            _rendering = false;
            if (!IsDisposed) UseWaitCursor = false;
        }
        if (_renderAgain) { _renderAgain = false; await RenderAsync(); }
    }

    private static void DisposeRender(EditorRender? r)
    {
        if (r is null) return;
        foreach (var p in r.Pages) p.Dispose();
        try { File.Delete(r.PdfPath); } catch { /* geçici dosya */ }
    }

    private bool HasBox(string key) => _render?.Boxes.Any(b => string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase)) ?? false;

    // ================= Yakınlaştırma ve sayfa yerleşimi =================
    private float FitZoom()
    {
        if (_render is null || _render.SizesPt.Count == 0) return 1;
        float pageWpx = _render.SizesPt.Max(s => s.Width) * 96f / 72f;
        return Math.Clamp((_scroll.ClientSize.Width - 2 * PageGap - SystemInformation.VerticalScrollBarWidth) / pageWpx, 0.3f, 3f);
    }

    private float CurrentZoom() => _zoom < 0 ? FitZoom() : _zoom;

    private void SetZoom(float z)
    {
        CommitEdit();
        _zoom = z < 0 ? -1 : Math.Clamp(z, 0.3f, 3f);
        Relayout();
    }

    private void Relayout()
    {
        _pageRects.Clear();
        float z = CurrentZoom();
        _zoomLabel.Text = $"%{Math.Round(z * 100)}";
        if (_render is null) { _canvas.Size = _scroll.ClientSize; _canvas.Invalidate(); return; }
        float y = PageGap, maxW = 0;
        foreach (var s in _render.SizesPt)
        {
            float w = s.Width * 96f / 72f * z, h = s.Height * 96f / 72f * z;
            _pageRects.Add(new RectangleF(0, y, w, h));
            y += h + PageGap;
            maxW = Math.Max(maxW, w);
        }
        int canvasW = (int)Math.Max(_scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth, maxW + 2 * PageGap);
        for (int i = 0; i < _pageRects.Count; i++)
            _pageRects[i] = _pageRects[i] with { X = (canvasW - _pageRects[i].Width) / 2 };
        _canvas.Size = new Size(canvasW, (int)y);
        _canvas.Invalidate();
    }

    /// <summary>Bir alan kutusunun tuvaldeki dikdörtgeni.</summary>
    private RectangleF ScreenRect(FieldBox b)
    {
        var page = _pageRects[b.Page];
        float k = page.Width / _render!.SizesPt[b.Page].Width;
        return new RectangleF(page.X + (float)b.X * k - 2, page.Y + (float)b.Y * k - 1, (float)b.W * k + 4, (float)b.H * k + 2);
    }

    private FieldBox? HitTest(Point p)
    {
        if (_render is null) return null;
        foreach (var b in _render.Boxes)
            if (b.Page < _pageRects.Count && ScreenRect(b).Contains(p)) return b;
        return null;
    }

    private EditTarget? FieldOf(string key) => _targets.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Hedefi aynı anahtarlı güncel hedefle değiştirir (hedefler her yenilemede yeniden kurulur).</summary>
    private EditTarget Fresh(EditTarget t) => FieldOf(t.Key) ?? t;

    // ================= Çizim =================
    private void PaintCanvas(Graphics g)
    {
        g.Clear(Desk);
        if (_render is null) return;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        for (int i = 0; i < _pageRects.Count && i < _render.Pages.Count; i++)
        {
            var r = _pageRects[i];
            using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(shadow, r.X + 3, r.Y + 3, r.Width, r.Height);
            g.FillRectangle(Brushes.White, r);
            g.DrawImage(_render.Pages[i], r);
        }

        var drawnPending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in _render.Boxes)
        {
            if (b.Page >= _pageRects.Count) continue;
            var rect = ScreenRect(b);
            if (_pending.TryGetValue(b.Key, out var newText))
            {
                // Yazılan değer hemen görünsün (sayfa birkaç saniye içinde Word ile yenilenir).
                g.FillRectangle(Brushes.White, rect);
                if (drawnPending.Add(b.Key))
                {
                    var target = FieldOf(b.Key);
                    var shown = newText.Length == 0 ? "[" + (target is { Placeholder.Length: > 0 } ? target.Placeholder : target?.Label ?? b.Key) + "]" : newText;
                    using var f = new Font("Segoe UI", Math.Max(6, rect.Height * 0.62f), GraphicsUnit.Pixel);
                    var size = g.MeasureString(shown, f);
                    var area = new RectangleF(rect.X, rect.Y, Math.Max(rect.Width, size.Width + 4), rect.Height);
                    g.FillRectangle(Brushes.White, area);
                    using var fill = new SolidBrush(FieldHover);
                    g.FillRectangle(fill, area);
                    using var brush = new SolidBrush(newText.Length == 0 ? Color.Gray : Color.FromArgb(20, 40, 120));
                    g.DrawString(shown, f, brush, area.X + 1, area.Y + (area.Height - size.Height) / 2);
                }
                continue;
            }
            bool hover = string.Equals(b.Key, _hoverKey, StringComparison.OrdinalIgnoreCase);
            using var br = new SolidBrush(hover ? FieldHover : b.Placeholder ? EmptyFill : FieldFill);
            g.FillRectangle(br, rect);
            if (hover)
            {
                using var pen = new Pen(Color.FromArgb(200, 140, 0), 1.2f);
                g.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
            }
        }
    }

    // ================= Fare =================
    private void CanvasMouseMove(Point p)
    {
        var hit = HitTest(p);
        var key = hit?.Key;
        _canvas.Cursor = hit is null ? Cursors.Default : Cursors.IBeam;
        if (string.Equals(key, _hoverKey, StringComparison.OrdinalIgnoreCase)) return;
        _hoverKey = key;
        var f = key is null ? null : FieldOf(key);
        _tip.SetToolTip(_canvas, f is null ? "" : $"{f.Label} — tıklayıp yazın" + (string.IsNullOrEmpty(f.Hint) ? "" : "\n" + f.Hint));
        _canvas.Invalidate();
    }

    private void CanvasMouseDown(Point p)
    {
        var hit = HitTest(p);
        if (hit is null)
        {
            CommitEdit();
            if (_render is not null && _pageRects.Any(r => r.Contains(p)))
                _status.Text = "🔒 Bu kısım şablonun sabit metni (kilitli). Yalnız sarı işaretli alanlar düzenlenir.";
            return;
        }
        if (FieldOf(hit.Key) is { } f) BeginEdit(f, ScreenRect(hit));
        else _status.Text = "🔒 Bu hücre otomatik doldurulur; ilgili saat alanından değiştirin.";
    }

    // ================= Yerinde düzenleme =================
    private void BeginEdit(EditTarget f, RectangleF rect)
    {
        CommitEdit();
        _editing = f;
        float fontPx = Math.Clamp(rect.Height * 0.62f, 11f, 22f);
        _editor = new TextBox
        {
            Text = _pending.TryGetValue(f.Key, out var p) ? p : f.Value,
            Font = new Font("Segoe UI", fontPx, GraphicsUnit.Pixel),
            BackColor = Color.FromArgb(255, 251, 220),
            BorderStyle = BorderStyle.FixedSingle,
            Multiline = f.Multiline,
            ScrollBars = f.Multiline ? ScrollBars.Vertical : ScrollBars.None,
            AcceptsReturn = f.Multiline
        };
        int width = (int)Math.Max(rect.Width + 8, 260);
        width = Math.Min(width, _canvas.Width - (int)rect.X - 8);
        int height = f.Multiline ? (int)(fontPx * 4.5f) : _editor.PreferredHeight;
        _editor.SetBounds((int)rect.X - 2, (int)(rect.Y + (rect.Height - height) / 2), Math.Max(120, width), height);
        _editor.KeyDown += EditorKeyDown;
        _editor.Leave += (_, _) => CommitEdit();
        _canvas.Controls.Add(_editor);
        _editor.Focus();
        _editor.SelectAll();
        _status.Text = $"✎ {f.Label}" + (string.IsNullOrEmpty(f.Hint) ? "" : " — " + f.Hint);
        _list.SelectedItem = f;
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (_editor is null || _editing is null) return;
        if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; CancelEdit(); }
        else if (e.KeyCode == Keys.Enter && !(_editing.Multiline && e.Shift)) { e.SuppressKeyPress = true; CommitEdit(); }
        else if (e.KeyCode == Keys.Tab)
        {
            e.SuppressKeyPress = true;
            var cur = _editing;
            CommitEdit();
            MoveToField(cur, e.Shift ? -1 : 1);
        }
    }

    private void CancelEdit()
    {
        if (_editor is null) return;
        var ed = _editor;
        _editor = null;
        _editing = null;
        _canvas.Controls.Remove(ed);
        ed.Dispose();
        _canvas.Focus();
    }

    private void CommitEdit()
    {
        if (_editor is null || _editing is null) return;
        var f = _editing;
        var value = _editor.Text.Trim();
        CancelEdit();
        var current = f.Value;
        if (value == current) return;
        _undo.Push((f, current, value));
        _redo.Clear();
        Apply(f, value);
    }

    /// <summary>Değeri yazar (rapor penceresindeki tablo da güncellenir) ve sayfanın yenilenmesini planlar.</summary>
    private void Apply(EditTarget f, string value)
    {
        f.Set(value);
        _pending[f.Key] = value;
        _list.Invalidate();
        _canvas.Invalidate();
        UpdateUndoButtons();
        if (_rendering) _renderAgain = true;
        if (!_closing) { _debounce.Stop(); _debounce.Start(); }
    }

    private void Undo()
    {
        CommitEdit();
        if (_undo.Count == 0) { _status.Text = "Geri alınacak değişiklik yok."; return; }
        var (t, old, now) = _undo.Pop();
        _redo.Push((t, old, now));
        t = Fresh(t);
        Apply(t, old);
        FocusField(t, edit: false);
        _status.Text = $"↶ Geri alındı: {t.Label} → {(old.Length == 0 ? "(boş)" : old)}";
    }

    private void Redo()
    {
        CommitEdit();
        if (_redo.Count == 0) { _status.Text = "Yinelenecek değişiklik yok."; return; }
        var (t, old, now) = _redo.Pop();
        _undo.Push((t, old, now));
        t = Fresh(t);
        Apply(t, now);
        FocusField(t, edit: false);
        _status.Text = $"↷ Yinelendi: {t.Label} → {(now.Length == 0 ? "(boş)" : now)}";
    }

    private void UpdateUndoButtons()
    {
        if (_btnUndo is null || _btnRedo is null) return;
        _btnUndo.Enabled = _undo.Count > 0;
        _btnRedo.Enabled = _redo.Count > 0;
    }

    /// <summary>Belgedeki sırayla (sayfa, yukarıdan aşağı, soldan sağa) alanlar.</summary>
    private List<EditTarget> DocumentOrder()
    {
        if (_render is null) return new();
        return _render.Boxes.OrderBy(b => b.Page).ThenBy(b => Math.Round(b.Y / 4)).ThenBy(b => b.X)
            .Select(b => FieldOf(b.Key)).OfType<EditTarget>().Distinct().ToList();
    }

    private void MoveToField(EditTarget from, int dir)
    {
        var order = DocumentOrder();
        if (order.Count == 0) return;
        int i = order.IndexOf(from);
        FocusField(order[((i < 0 ? 0 : i + dir) % order.Count + order.Count) % order.Count], edit: true);
    }

    private void GoToNextEmpty()
    {
        CommitEdit();
        var order = DocumentOrder();
        var empty = order.Where(f => string.IsNullOrWhiteSpace(_pending.TryGetValue(f.Key, out var p) ? p : f.Value)).ToList();
        if (empty.Count == 0) { _status.Text = "✓ Belgede boş alan kalmadı."; return; }
        int cur = _editing is null ? -1 : order.IndexOf(_editing);
        var next = empty.FirstOrDefault(f => order.IndexOf(f) > cur) ?? empty[0];
        FocusField(next, edit: true);
    }

    /// <summary>Alanı görünür yapar (gerekirse kaydırır) ve isteğe bağlı düzenlemeyi açar.</summary>
    private void FocusField(EditTarget f, bool edit)
    {
        var box = _render?.Boxes.FirstOrDefault(b => string.Equals(b.Key, f.Key, StringComparison.OrdinalIgnoreCase));
        if (box is null || box.Page >= _pageRects.Count)
        {
            _status.Text = $"“{f.Label}” bu şablonda yer almıyor; değeri rapor penceresindeki tablodan girilebilir.";
            return;
        }
        var rect = ScreenRect(box);
        int y = (int)rect.Y - _scroll.ClientSize.Height / 3;
        _scroll.AutoScrollPosition = new Point(Math.Max(0, (int)rect.X - 80), Math.Max(0, y));
        rect = ScreenRect(box); // kaydırma tuvali taşımaz; dikdörtgen tuval koordinatında aynı
        if (edit) BeginEdit(f, rect);
    }

    // ================= Sağdaki alan listesi =================
    private void FillList()
    {
        var keep = _list.SelectedItem;
        _list.BeginUpdate();
        _list.Items.Clear();
        // Yalnız tekil alanlar (tablo hücreleri sayfada tıklanarak düzenlenir; listeyi kalabalıklaştırmasın).
        var order = DocumentOrder().Where(f => !f.Key.Contains('[')).ToList();
        foreach (var f in order) _list.Items.Add(f);
        foreach (var f in _targets.Where(f => !f.Key.Contains('[') && !order.Contains(f))) _list.Items.Add(f); // şablonda yer almayanlar en altta
        _list.EndUpdate();
        if (keep is not null && _list.Items.Contains(keep)) _list.SelectedItem = keep;
    }

    private void DrawListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _list.Items[e.Index] is not EditTarget f) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        var value = _pending.TryGetValue(f.Key, out var p) ? p : f.Value;
        bool inDoc = _render is null || HasBox(f.Key);
        bool empty = string.IsNullOrWhiteSpace(value);
        using (var bg = new SolidBrush(selected ? Color.FromArgb(232, 240, 224) : Color.White)) e.Graphics.FillRectangle(bg, e.Bounds);
        using var bold = new Font(Font, FontStyle.Bold);
        var gray = inDoc ? Color.FromArgb(40, 40, 40) : Color.Gray;
        TextRenderer.DrawText(e.Graphics, f.Label + (inDoc ? "" : "  (şablonda yok)"), bold, new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, e.Bounds.Width - 8, 18), gray, TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(e.Graphics, empty ? "— boş —" : value.Replace('\n', ' '), Font,
            new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 20, e.Bounds.Width - 8, 18),
            empty ? (f.Optional ? Color.Gray : Color.FromArgb(190, 50, 40)) : Color.DimGray, TextFormatFlags.EndEllipsis);
        using var line = new Pen(Color.FromArgb(235, 235, 230));
        e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
    }

    // ================= Çıktı =================
    /// <summary>Boş zorunlu alan varsa sorar; false = kullanıcı önce doldurmak istedi (ilk boş alana gidilir).</summary>
    private bool ConfirmMissing()
    {
        CommitEdit();
        var missing = _targets.Where(f => !f.Optional && string.IsNullOrWhiteSpace(f.Value) && HasBox(f.Key)).ToList();
        if (missing.Count == 0) return true;
        var ans = MessageBox.Show(this,
            $"{missing.Count} alan boş: {string.Join(", ", missing.Take(4).Select(m => m.Label))}{(missing.Count > 4 ? "…" : "")}\n\n" +
            "Boş bırakılarak devam edilsin mi?\n(Hayır: ilk boş alana gidilir.)", "Boş alan", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (ans == DialogResult.Yes) return true;
        GoToNextEmpty();
        return false;
    }

    private async Task PrintAsync()
    {
        if (!ConfirmMissing()) return;
        await RunOutput("Yazdırılıyor (Word belgeyi hazırlıyor)…", () => _ctx.Print(_word, this));
    }

    private async Task SavePdfAsync()
    {
        if (!ConfirmMissing()) return;
        await RunOutput("PDF hazırlanıyor…", () => _ctx.SavePdf(_word, this));
    }

    private async Task RunOutput(string busy, Func<Task<string?>> work)
    {
        var before = _status.Text;
        _status.Text = busy;
        UseWaitCursor = true;
        try { _status.Text = await work() ?? before; }
        catch (Exception ex) { _status.Text = "İşlem tamamlanamadı: " + (ex.InnerException?.Message ?? ex.Message); }
        finally { UseWaitCursor = false; }
    }

    /// <summary>Sayfaların çizildiği tuval (çift tamponlu).</summary>
    private sealed class PageCanvas : Control
    {
        private readonly ReportEditorForm _owner;
        public PageCanvas(ReportEditorForm owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Desk;
        }
        protected override void OnPaint(PaintEventArgs e) => _owner.PaintCanvas(e.Graphics);
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); _owner.CanvasMouseMove(e.Location); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); _owner.CanvasMouseDown(e.Location); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _owner.CanvasMouseMove(new Point(-1, -1)); }
    }
}
