using NotasyonOtomasyonu.App.Branding;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Tasarım ekranında gösterilecek kart: masa kartları penceresindeki o anki afiş, yazı tipi, yön ve stil.</summary>
public sealed record CardDesignContext(
    Image? Poster,
    string EventName,
    string? Font,
    bool Landscape,
    bool Filled,
    bool FullWidth,
    CardSpec Sample,
    IReadOnlyList<string> Categories,
    string? Province);

/// <summary>
/// Kart tasarımı: logoların (GSB, TSF il) yeri ve boyutu. Solda kartın büyük canlı önizlemesi — logolar
/// sürüklenir, fare tekerleğiyle büyütülür, ok tuşlarıyla kaydırılır; afiş bitişine, sayfa ortasına,
/// kenar boşluklarına ve birbirine mıknatısla hizalanır. Sağda resimli hazır yerleşimler ve az sayıda
/// ayar. Değişiklikler "Tamam"a basılınca kaydedilir.
/// </summary>
public sealed class CardDesignForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);
    private static readonly Color Border = Color.FromArgb(208, 208, 198);

    private readonly CardDesignContext _ctx;
    private readonly CardConfig _cfg;

    // Çalışma kopyası (Tamam'da yazılır)
    private string _mode;
    private float _scale;
    private readonly Dictionary<string, LogoSpot> _spots;

    private readonly CardCanvas _canvas;
    private readonly CheckBox _chkGsb = new() { Text = "T.C. Gençlik ve Spor Bakanlığı", AutoSize = true };
    private readonly CheckBox _chkTsf = new() { Text = "TSF il logosu", AutoSize = true };
    private readonly Label _lblProvince = new() { AutoSize = false, Width = 300, Height = 38, ForeColor = Color.DimGray };
    private readonly TrackBar _trkSize = new() { Minimum = 50, Maximum = 160, TickFrequency = 10, SmallChange = 5, LargeChange = 10, Width = 200, AutoSize = false, Height = 32 };
    private readonly Label _lblSize = new() { AutoSize = true, Margin = new Padding(4, 8, 0, 0) };
    private readonly CheckBox _chkBacking = new() { Text = "Logoların arkası beyaz", AutoSize = true };
    private readonly Label _lblMode = new() { AutoSize = false, Width = 300, Height = 36, ForeColor = GreenDark };
    private readonly List<(string Mode, Button Button)> _gallery = new();
    private (RectangleF Box, float Pt) _bandPt;   // ortak kategori puntosu (bant yazı alanına göre önbellek)
    private Image? _gsbImg, _tsfImg;

    public CardDesignForm(CardDesignContext ctx, CardConfig cfg)
    {
        _ctx = ctx;
        _cfg = cfg;
        _mode = string.IsNullOrWhiteSpace(cfg.LogoLayout) ? CardBranding.Auto : cfg.LogoLayout;
        _scale = (float)Math.Clamp(cfg.LogoScale, 0.5, 1.6);
        _spots = cfg.LogoSpots.ToDictionary(kv => kv.Key, kv => new LogoSpot { X = kv.Value.X, Y = kv.Value.Y, H = kv.Value.H });

        Text = $"Kart Tasarımı — logolar ({(ctx.Landscape ? "yatay" : "dikey")} A4)";
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 760);
        MinimumSize = new Size(960, 640);
        BackColor = Color.FromArgb(250, 250, 247);
        KeyPreview = true;

        _canvas = new CardCanvas(this) { Dock = DockStyle.Fill };
        BuildLayout();

        _chkGsb.Checked = cfg.ShowGsbLogo;
        _chkTsf.Checked = cfg.ShowTsfLogo;
        _chkBacking.Checked = cfg.LogoBacking;
        _trkSize.Value = (int)Math.Round(_scale * 100);
        _lblProvince.Text = (ctx.Province is null ? "TSF logosu: genel (1991)" : $"TSF logosundaki il: {ctx.Province}") +
                            " — Ayarlar → Bölge'den değiştirilir.";
        LoadImages();

        _chkGsb.CheckedChanged += (_, _) => { LoadImages(); Changed(thumbs: true); };
        _chkTsf.CheckedChanged += (_, _) => { LoadImages(); Changed(thumbs: true); };
        _chkBacking.CheckedChanged += (_, _) => Changed(thumbs: true);
        _trkSize.ValueChanged += (_, _) => ApplyScale((float)_trkSize.Value / 100);
        FormClosed += (_, _) => { _gsbImg?.Dispose(); _tsfImg?.Dispose(); foreach (var (_, b) in _gallery) b.Image?.Dispose(); };
        Shown += (_, _) => { Changed(thumbs: true); _canvas.Focus(); };
    }

    // ================= Yerleşim (pencere) =================
    private void BuildLayout()
    {
        var right = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = 346, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            AutoScroll = true, Padding = new Padding(12, 12, 8, 8), BackColor = Color.FromArgb(244, 244, 238)
        };
        right.Controls.Add(Header("Hazır yerleşimler", 0));
        var grid = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new Size(306, 0), Margin = new Padding(0) };
        foreach (var (mode, label) in CardBranding.Modes)
        {
            var b = new Button
            {
                Text = label, TextImageRelation = TextImageRelation.ImageAboveText, Size = new Size(146, 118),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 6, 6),
                Font = new Font("Segoe UI", 8.5f), Tag = mode
            };
            b.FlatAppearance.BorderColor = Border;
            b.Click += (_, _) => { _mode = mode; Changed(thumbs: false); };
            _gallery.Add((mode, b));
            grid.Controls.Add(b);
        }
        right.Controls.Add(grid);
        right.Controls.Add(_lblMode);

        right.Controls.Add(Header("Logolar", 8));
        right.Controls.Add(_chkGsb);
        right.Controls.Add(_chkTsf);
        right.Controls.Add(_lblProvince);

        right.Controls.Add(Header("Boyut", 8));
        var sizeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        sizeRow.Controls.AddRange(new Control[] { _trkSize, _lblSize });
        right.Controls.Add(sizeRow);
        right.Controls.Add(_chkBacking);
        new ToolTip().SetToolTip(_chkBacking, "Afişin ya da renkli bandın üstüne gelen logolar beyaz bir zemin üstünde okunaklı durur.");
        right.Controls.Add(new Label
        {
            AutoSize = false, Width = 300, Height = 74, ForeColor = Color.DimGray, Margin = new Padding(0, 8, 0, 0),
            Text = "Logoları kartın üstünde sürükleyin; fare tekerleğiyle büyütüp küçültün, ok tuşlarıyla ince ayar yapın " +
                   "(Shift ile 10 kat). Afiş bitişine, sayfa ortasına ve kenarlara kendiliğinden hizalanır."
        });

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 60, Padding = new Padding(12), BackColor = Color.FromArgb(242, 242, 236) };
        var reset = new Button { Text = "↺ Varsayılana dön", AutoSize = true, Height = 36, Dock = DockStyle.Left };
        reset.Click += (_, _) =>
        {
            _mode = CardBranding.Auto;
            _spots.Clear();
            _trkSize.Value = 100; // ApplyScale → Changed
            _chkBacking.Checked = true;
            Changed(thumbs: true);
        };
        var ok = new Button { Text = "✔ Tamam", Width = 130, Height = 36, Dock = DockStyle.Right, BackColor = Green, ForeColor = Color.White, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Vazgeç", Width = 110, Height = 36, Dock = DockStyle.Right, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => Save();
        foreach (var b in new[] { reset, ok, cancel })
        {
            b.FlatStyle = FlatStyle.Flat; b.Cursor = Cursors.Hand;
            b.FlatAppearance.BorderColor = b == ok ? GreenDark : Border;
            if (b != ok) b.BackColor = Color.White;
        }
        ok.Font = new Font(Font, FontStyle.Bold);
        bottom.Controls.Add(reset);
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(new Panel { Width = 8, Dock = DockStyle.Right });
        bottom.Controls.Add(ok);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_canvas);
        Controls.Add(right);
        Controls.Add(bottom);
    }

    private Label Header(string text, int top) => new()
    {
        Text = text, AutoSize = true, Font = new Font(Font, FontStyle.Bold), ForeColor = GreenDark, Margin = new Padding(0, top, 0, 6)
    };

    // ================= Durum =================
    private CardBranding Branding(string? mode = null)
        => new(_chkGsb.Checked, _chkTsf.Checked, _ctx.Province, mode ?? _mode, _scale, _chkBacking.Checked, _spots);

    private SizeF? PosterSize => _ctx.Poster is { } p ? new SizeF(p.Width, p.Height) : null;

    internal CardLayout CardLayoutFor(string? mode = null)
        => CardLayout.For(_ctx.Landscape, PosterSize, _ctx.FullWidth, Branding(mode), _ctx.EventName, _ctx.Font);

    private void LoadImages()
    {
        _gsbImg?.Dispose(); _tsfImg?.Dispose();
        (_gsbImg, _tsfImg) = (null, null);
        try { (_gsbImg, _tsfImg) = Branding().Load(400); } catch { /* önizlemesiz */ }
    }

    private void Changed(bool thumbs)
    {
        var br = Branding();
        string effective = br.EffectiveMode(_ctx.Poster is not null);
        bool single = br.Single;
        if (single && CardBranding.SameAsCenterWhenSingle(effective)) effective = CardBranding.SeamCenter;
        foreach (var (mode, b) in _gallery)
        {
            // Tek logoda "iki yanda" kipleri ortaya gelir: aynısı olan düğme gizlenir, adlar ona göre yazılır.
            b.Visible = !(single && CardBranding.SameAsCenterWhenSingle(mode));
            b.Text = CardBranding.Label(mode, single);
            bool on = mode == effective;
            b.FlatAppearance.BorderColor = on ? GreenDark : Border;
            b.FlatAppearance.BorderSize = on ? 3 : 1;
            b.Font = new Font("Segoe UI", 8.5f, on ? FontStyle.Bold : FontStyle.Regular);
        }
        _lblMode.Text = _mode == CardBranding.Custom ? "✋ Elle yerleştirildi (hazır bir yerleşim seçerek dönebilirsiniz)"
                      : _mode == CardBranding.Auto ? "Otomatik: " + CardBranding.Label(effective, single)
                      : "Seçili: " + CardBranding.Label(effective, single);
        _lblSize.Text = $"%{_trkSize.Value}";
        bool any = _chkGsb.Checked || _chkTsf.Checked;
        foreach (var (_, b) in _gallery) b.Enabled = any;
        _trkSize.Enabled = _chkBacking.Enabled = any;
        if (thumbs) RenderThumbs();
        _canvas.Invalidate();
    }

    /// <summary>Boyut değişti: hazır yerleşimde çarpan, elle yerleşimde her logo kendi merkezinde orantılı büyür.</summary>
    private void ApplyScale(float scale)
    {
        if (_mode == CardBranding.Custom && _scale > 0)
            foreach (var s in _spots.Values) s.H = Math.Clamp(s.H * scale / _scale, 0.03, 0.7);
        _scale = scale;
        Changed(thumbs: _mode != CardBranding.Custom);
    }

    private void RenderThumbs()
    {
        foreach (var (mode, b) in _gallery)
        {
            var old = b.Image;
            b.Image = Render(mode, 132);
            old?.Dispose();
        }
    }

    /// <summary>Kartı verilen genişlikte (piksel) çizer — hazır yerleşim küçük resimleri için.</summary>
    private Bitmap Render(string mode, int widthPx)
    {
        var l = CardLayoutFor(mode);
        float s = widthPx / l.PageW;
        var bmp = new Bitmap(widthPx, (int)Math.Ceiling(l.PageH * s));
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        DrawCard(g, l, Branding(mode), new RectangleF(0, 0, bmp.Width, bmp.Height), s);
        using var pen = new Pen(Color.FromArgb(190, 190, 190));
        g.ResetTransform(); g.PageUnit = GraphicsUnit.Pixel;
        g.DrawRectangle(pen, 0, 0, bmp.Width - 1, bmp.Height - 1);
        return bmp;
    }

    /// <summary>Kartı <paramref name="px"/> piksel dikdörtgenine (<paramref name="s"/> piksel/punto) çizer.</summary>
    internal void DrawCard(Graphics g, CardLayout l, CardBranding? branding, RectangleF px, float s)
    {
        var state = g.Save();
        g.PageUnit = GraphicsUnit.Point;
        float u = 72f / g.DpiX; // 1 piksel = u punto
        g.TranslateTransform(px.X * u, px.Y * u);
        g.ScaleTransform(s * u, s * u);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        CardPrinter.Draw(g, l, _ctx.Sample, _ctx.Poster, _ctx.EventName, _ctx.Font, _ctx.Filled, _ctx.FullWidth, BandPt(l),
                         branding ?? Branding(), _gsbImg, _tsfImg);
        g.Restore(state);
    }

    /// <summary>Kategori adlarının ortak puntosu (masa kartları penceresindekiyle aynı hesap); bant alanı değişmedikçe yeniden ölçülmez.</summary>
    private float BandPt(CardLayout l)
    {
        var box = l.BandTextRect;
        if (box != _bandPt.Box || _bandPt.Pt <= 0)
            _bandPt = (box, CardPrinter.CommonBandPt(l, _ctx.Categories.Append(_ctx.Sample.Category), _ctx.Font));
        return _bandPt.Pt;
    }

    // ================= Elle yerleşim =================
    /// <summary>Hazır yerleşimden elle yerleşime geçer: logolar o anki yerlerinden başlar.</summary>
    internal void EnsureCustom()
    {
        bool wasCustom = _mode == CardBranding.Custom;
        var l = CardLayoutFor();
        foreach (var p in l.Logos)
        {
            var key = CardBranding.SpotKey(p.Kind, _ctx.Landscape);
            if (wasCustom && _spots.ContainsKey(key)) continue; // elle yerleşimde yalnız eksik logolar tamamlanır
            _spots[key] = new LogoSpot
            {
                X = (p.Rect.X + p.Rect.Width / 2) / l.PageW, Y = (p.Rect.Y + p.Rect.Height / 2) / l.PageH, H = p.Rect.Height / l.PageH
            };
        }
        if (wasCustom) return;
        _mode = CardBranding.Custom;
        Changed(thumbs: false);
    }

    internal LogoSpot? Spot(string kind) => _spots.GetValueOrDefault(CardBranding.SpotKey(kind, _ctx.Landscape));

    internal void Moved() => Changed(thumbs: false);

    private void Save()
    {
        _cfg.ShowGsbLogo = _chkGsb.Checked;
        _cfg.ShowTsfLogo = _chkTsf.Checked;
        _cfg.LogoLayout = _mode;
        _cfg.LogoScale = _scale;
        _cfg.LogoBacking = _chkBacking.Checked;
        _cfg.LogoSpots = _spots.ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    // ================= Tuval =================
    /// <summary>Kartın büyük önizlemesi; logolar fareyle taşınır ve boyutlanır.</summary>
    private sealed class CardCanvas : Control
    {
        private readonly CardDesignForm _f;
        private string? _hover, _selected, _drag;
        private PointF _grab;                      // logo merkezine göre tutma noktası (punto)
        private readonly List<(bool Vertical, float At)> _guides = new();
        private CardLayout? _layout;
        private RectangleF _page;                  // kartın tuvaldeki yeri (piksel)
        private float _s = 1;                      // piksel / punto

        public CardCanvas(CardDesignForm f)
        {
            _f = f;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(226, 226, 220);
            TabStop = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            _layout = _f.CardLayoutFor();
            const int pad = 28;
            _s = Math.Min((Width - 2f * pad) / _layout.PageW, (Height - 2f * pad) / _layout.PageH);
            if (_s <= 0) return;
            _page = new RectangleF((Width - _layout.PageW * _s) / 2, (Height - _layout.PageH * _s) / 2, _layout.PageW * _s, _layout.PageH * _s);
            using (var shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0))) g.FillRectangle(shadow, _page.X + 5, _page.Y + 5, _page.Width, _page.Height);
            g.FillRectangle(Brushes.White, _page);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            _f.DrawCard(g, _layout, null, _page, _s);

            // Afiş bitişi çizgisi ve hizalama kılavuzları (yalnız sürüklerken)
            if (_drag is not null)
            {
                using var seam = new Pen(Color.FromArgb(150, 0, 150, 220), 1) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
                float sy = _page.Y + _layout.SeamY * _s;
                g.DrawLine(seam, _page.Left, sy, _page.Right, sy);
                using var guide = new Pen(Color.FromArgb(230, 0, 160), 1.5f);
                foreach (var (vertical, at) in _guides)
                {
                    if (vertical) { float x = _page.X + at * _s; g.DrawLine(guide, x, _page.Top, x, _page.Bottom); }
                    else { float y = _page.Y + at * _s; g.DrawLine(guide, _page.Left, y, _page.Right, y); }
                }
            }
            foreach (var p in _layout.Logos)
            {
                bool sel = p.Kind == _selected, hov = p.Kind == _hover;
                if (!sel && !hov) continue;
                var r = ToPx(p.Rect);
                using var pen = new Pen(sel ? Color.FromArgb(0, 120, 215) : Color.FromArgb(140, 0, 120, 215), sel ? 2f : 1f)
                { DashStyle = sel ? System.Drawing.Drawing2D.DashStyle.Solid : System.Drawing.Drawing2D.DashStyle.Dash };
                g.DrawRectangle(pen, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            }
        }

        private RectangleF ToPx(RectangleF r) => new(_page.X + r.X * _s, _page.Y + r.Y * _s, r.Width * _s, r.Height * _s);
        private PointF ToPt(Point p) => new((p.X - _page.X) / _s, (p.Y - _page.Y) / _s);

        private LogoPlacement? Hit(Point p)
        {
            if (_layout is null) return null;
            var pt = ToPt(p);
            return _layout.Logos.LastOrDefault(l => RectangleF.Inflate(l.Rect, 4, 4).Contains(pt));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var hit = Hit(e.Location);
            _selected = hit?.Kind;
            if (hit is not null && e.Button == MouseButtons.Left)
            {
                _f.EnsureCustom();
                var pt = ToPt(e.Location);
                _grab = new PointF(pt.X - (hit.Rect.X + hit.Rect.Width / 2), pt.Y - (hit.Rect.Y + hit.Rect.Height / 2));
                _drag = hit.Kind;
                Capture = true;
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag is not null && _layout is not null && _f.Spot(_drag) is { } spot)
            {
                var pt = ToPt(e.Location);
                float cx = pt.X - _grab.X, cy = pt.Y - _grab.Y;
                float h = (float)spot.H * _layout.PageH, w = h * OfficialLogos.Aspect(_drag);
                (cx, cy) = Snap(_drag, cx, cy, w, h);
                spot.X = Math.Clamp(cx, w / 2, _layout.PageW - w / 2) / _layout.PageW;
                spot.Y = Math.Clamp(cy, h / 2, _layout.PageH - h / 2) / _layout.PageH;
                _f.Moved();
                return;
            }
            var hov = Hit(e.Location)?.Kind;
            Cursor = hov is null ? Cursors.Default : Cursors.SizeAll;
            if (hov != _hover) { _hover = hov; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _drag = null;
            _guides.Clear();
            Capture = false;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { _hover = null; Invalidate(); }

        /// <summary>Mıknatıs: afiş bitişi, sayfa ortası, kenar boşlukları, diğer logonun hizası ve simetriği.</summary>
        private (float X, float Y) Snap(string kind, float cx, float cy, float w, float h)
        {
            _guides.Clear();
            var l = _layout!;
            float tol = 7 / _s; // ekranda 7 piksel
            var other = l.Logos.FirstOrDefault(p => p.Kind != kind);
            var xs = new List<(float Center, float Guide)>
            {
                (l.PageW / 2, l.PageW / 2),
                (CardLayout.Margin + w / 2, CardLayout.Margin),
                (l.PageW - CardLayout.Margin - w / 2, l.PageW - CardLayout.Margin),
            };
            var ys = new List<(float Center, float Guide)>
            {
                (l.SeamY, l.SeamY),
                (CardLayout.Margin + h / 2, CardLayout.Margin),
                (l.PageH - CardLayout.Margin - h / 2, l.PageH - CardLayout.Margin),
            };
            if (other is not null)
            {
                float ox = other.Rect.X + other.Rect.Width / 2, oy = other.Rect.Y + other.Rect.Height / 2;
                xs.Add((l.PageW - ox, l.PageW - ox));   // sayfa ortasına göre simetrik
                xs.Add((ox, ox));
                ys.Add((oy, oy));
                ys.Add((other.Rect.Top + h / 2, other.Rect.Top));
                ys.Add((other.Rect.Bottom - h / 2, other.Rect.Bottom));
            }
            var bx = xs.OrderBy(c => Math.Abs(c.Center - cx)).First();
            if (Math.Abs(bx.Center - cx) <= tol) { cx = bx.Center; _guides.Add((true, bx.Guide)); }
            var by = ys.OrderBy(c => Math.Abs(c.Center - cy)).First();
            if (Math.Abs(by.Center - cy) <= tol) { cy = by.Center; _guides.Add((false, by.Guide)); }
            return (cx, cy);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var kind = Hit(e.Location)?.Kind ?? _selected;
            if (kind is null) return;
            _f.EnsureCustom();
            _selected = kind;
            if (_f.Spot(kind) is { } spot)
            {
                spot.H = Math.Clamp(spot.H * (1 + 0.08 * Math.Sign(e.Delta)), 0.03, 0.7);
                _f.Moved();
            }
        }

        protected override bool IsInputKey(Keys keyData)
            => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_selected is null || _layout is null) return;
            float step = e.Shift ? 10 : 1;
            var (dx, dy) = e.KeyCode switch
            {
                Keys.Left => (-step, 0f), Keys.Right => (step, 0f), Keys.Up => (0f, -step), Keys.Down => (0f, step), _ => (0f, 0f)
            };
            if (dx == 0 && dy == 0) return;
            _f.EnsureCustom();
            if (_f.Spot(_selected) is { } spot)
            {
                spot.X = Math.Clamp(spot.X + dx / _layout.PageW, 0, 1);
                spot.Y = Math.Clamp(spot.Y + dy / _layout.PageH, 0, 1);
                _f.Moved();
            }
            e.Handled = true;
        }
    }
}
