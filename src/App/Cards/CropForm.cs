using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>
/// Afiş/logo kırpma aracı: kart üzerindeki resim yuvasının en-boy oranına (ya da 16:9, 4:3, 1:1)
/// kilitli veya serbest kırpma. Çerçeve sürüklenerek taşınır, köşelerden boyutlandırılır, fare
/// tekerleğiyle yakınlaştırılır. Sonuç asıl çözünürlükten yüksek kaliteyle (bicubic) kırpılır.
/// </summary>
public sealed class CropForm : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);

    private readonly Image _src;
    private readonly float _slotAspect;
    private readonly CropCanvas _canvas;
    private readonly ComboBox _mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly Label _info = new() { AutoSize = true, ForeColor = Color.DimGray };

    /// <summary>Kırpılmış görsel (Tamam'a basılınca); "Kırpmadan kullan" seçilirse asıl görselin kopyası.</summary>
    public Bitmap? Result { get; private set; }

    public CropForm(Image source, float slotAspect)
    {
        _src = source;
        _slotAspect = slotAspect;
        Text = "Afiş / logo kırp";
        Font = new Font("Segoe UI", 9.75f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(980, 720);
        this.FitToScreen(); // küçük ekranda / yüksek ölçekte taşmasın
        MinimumSize = new Size(640, 480);
        KeyPreview = true;

        _canvas = new CropCanvas(source) { Dock = DockStyle.Fill };
        _canvas.Changed += UpdateInfo;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(10, 8, 10, 4), WrapContents = false };
        top.Controls.Add(new Label { Text = "Oran:", AutoSize = true, Margin = new Padding(0, 7, 4, 0), Font = new Font(Font, FontStyle.Bold) });
        _mode.Items.AddRange(new object[]
        {
            $"Kart yuvası ({slotAspect:0.##}:1) — önerilen", "16:9", "4:3", "1:1", "Serbest kırpma"
        });
        top.Controls.Add(_mode);
        var fit = new Button { Text = "Tamamını sığdır", AutoSize = true, Height = 28, FlatStyle = FlatStyle.Flat };
        fit.Click += (_, _) => _canvas.ResetSelection();
        top.Controls.Add(fit);
        _info.Margin = new Padding(10, 7, 0, 0);
        top.Controls.Add(_info);

        var hint = new Label
        {
            Dock = DockStyle.Top, Height = 22, Padding = new Padding(12, 2, 0, 0), ForeColor = Color.DimGray,
            Text = "Çerçeveyi sürükleyerek taşıyın, köşelerden boyutlandırın, fare tekerleğiyle yakınlaştırın. Kilitli oranda görsel basık/esnemiş olmaz."
        };

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var ok = new Button { Text = "✂ Kırp ve kullan", Width = 170, Height = 36, BackColor = Green, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font(Font, FontStyle.Bold) };
        var raw = new Button { Text = "Kırpmadan kullan", Width = 150, Height = 36, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Vazgeç", Width = 100, Height = 36, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) => { Result = Crop(_src, _canvas.Selection); DialogResult = DialogResult.OK; };
        raw.Click += (_, _) => { Result = new Bitmap(_src); DialogResult = DialogResult.OK; };
        bottom.Controls.AddRange(new Control[] { ok, raw, cancel });
        CancelButton = cancel;

        Controls.Add(_canvas);
        Controls.Add(hint);
        Controls.Add(top);
        Controls.Add(bottom);

        _mode.SelectedIndexChanged += (_, _) => { _canvas.Aspect = AspectFor(_mode.SelectedIndex); _canvas.ResetSelection(); };
        _mode.SelectedIndex = 0;
    }

    private float? AspectFor(int i) => i switch
    {
        0 => _slotAspect,
        1 => 16f / 9f,
        2 => 4f / 3f,
        3 => 1f,
        _ => null
    };

    private void UpdateInfo()
    {
        var s = _canvas.Selection;
        _info.Text = $"Seçim: {s.Width:0} × {s.Height:0} px (asıl görsel {_src.Width} × {_src.Height})";
    }

    /// <summary>Seçili alanı asıl çözünürlükte, yüksek kaliteyle kırpar.</summary>
    public static Bitmap Crop(Image src, RectangleF r)
    {
        r = RectangleF.Intersect(r, new RectangleF(0, 0, src.Width, src.Height));
        int w = Math.Max(1, (int)Math.Round(r.Width)), h = Math.Max(1, (int)Math.Round(r.Height));
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        bmp.SetResolution(src.HorizontalResolution, src.VerticalResolution);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        using var attrs = new ImageAttributes();
        attrs.SetWrapMode(WrapMode.TileFlipXY); // kenarlarda yarı saydam çizgi oluşmasın
        g.DrawImage(src, new Rectangle(0, 0, w, h), r.X, r.Y, r.Width, r.Height, GraphicsUnit.Pixel, attrs);
        return bmp;
    }

    /// <summary>Görseli gösterip kırpma çerçevesini (görsel pikselleri cinsinden) yöneten tuval.</summary>
    private sealed class CropCanvas : Control
    {
        private const int HandleSize = 9;
        private readonly Image _img;
        private RectangleF _sel;                 // görsel koordinatı
        private enum Drag { None, Move, TL, TR, BL, BR }
        private Drag _drag;
        private PointF _dragStart;
        private RectangleF _selStart;

        public float? Aspect { get; set; }
        public RectangleF Selection => _sel;
        public event Action? Changed;

        public CropCanvas(Image img)
        {
            _img = img;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(60, 60, 60);
            ResetSelection();
        }

        /// <summary>Seçimi oran içinde görselin tamamına (ortalı, en büyük) ayarlar.</summary>
        public void ResetSelection()
        {
            float w = _img.Width, h = _img.Height;
            if (Aspect is float a)
            {
                if (w / h > a) w = h * a; else h = w / a;
            }
            _sel = new RectangleF((_img.Width - w) / 2, (_img.Height - h) / 2, w, h);
            Invalidate();
            Changed?.Invoke();
        }

        // ---- görsel ↔ ekran dönüşümü ----
        private (float Scale, float Ox, float Oy) View()
        {
            float pad = 16;
            float s = Math.Min((Width - 2 * pad) / _img.Width, (Height - 2 * pad) / _img.Height);
            s = Math.Max(s, 0.01f);
            return (s, (Width - _img.Width * s) / 2, (Height - _img.Height * s) / 2);
        }

        private RectangleF ToScreen(RectangleF r)
        {
            var (s, ox, oy) = View();
            return new RectangleF(ox + r.X * s, oy + r.Y * s, r.Width * s, r.Height * s);
        }

        private PointF ToImage(Point p)
        {
            var (s, ox, oy) = View();
            return new PointF((p.X - ox) / s, (p.Y - oy) / s);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var (s, ox, oy) = View();
            var imgRect = new RectangleF(ox, oy, _img.Width * s, _img.Height * s);
            g.DrawImage(_img, imgRect);
            var sr = ToScreen(_sel);
            using (var dim = new SolidBrush(Color.FromArgb(150, 0, 0, 0)))
            using (var region = new Region(imgRect))
            {
                region.Exclude(sr);
                g.FillRegion(dim, region);
            }
            using (var pen = new Pen(Color.White, 2)) g.DrawRectangle(pen, sr.X, sr.Y, sr.Width, sr.Height);
            using (var thirds = new Pen(Color.FromArgb(120, 255, 255, 255), 1))
                for (int i = 1; i < 3; i++)
                {
                    g.DrawLine(thirds, sr.X + sr.Width * i / 3, sr.Y, sr.X + sr.Width * i / 3, sr.Bottom);
                    g.DrawLine(thirds, sr.X, sr.Y + sr.Height * i / 3, sr.Right, sr.Y + sr.Height * i / 3);
                }
            using var hb = new SolidBrush(Color.White);
            foreach (var c in Corners(sr)) g.FillRectangle(hb, c.X - HandleSize / 2f, c.Y - HandleSize / 2f, HandleSize, HandleSize);
        }

        private static PointF[] Corners(RectangleF r) => new[]
        {
            new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Left, r.Bottom), new PointF(r.Right, r.Bottom)
        };

        private Drag HitTest(Point p)
        {
            var sr = ToScreen(_sel);
            var c = Corners(sr);
            Drag[] kinds = { Drag.TL, Drag.TR, Drag.BL, Drag.BR };
            for (int i = 0; i < 4; i++)
                if (Math.Abs(p.X - c[i].X) <= HandleSize && Math.Abs(p.Y - c[i].Y) <= HandleSize) return kinds[i];
            return sr.Contains(p) ? Drag.Move : Drag.None;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _drag = HitTest(e.Location);
            _dragStart = ToImage(e.Location);
            _selStart = _sel;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_drag == Drag.None)
            {
                Cursor = HitTest(e.Location) switch
                {
                    Drag.Move => Cursors.SizeAll,
                    Drag.TL or Drag.BR => Cursors.SizeNWSE,
                    Drag.TR or Drag.BL => Cursors.SizeNESW,
                    _ => Cursors.Default
                };
                return;
            }
            var p = ToImage(e.Location);
            float dx = p.X - _dragStart.X, dy = p.Y - _dragStart.Y;
            var r = _selStart;
            if (_drag == Drag.Move) r.Offset(dx, dy);
            else
            {
                // Karşı köşe sabit kalır
                float ax = _drag is Drag.TL or Drag.BL ? r.Right : r.Left;
                float ay = _drag is Drag.TL or Drag.TR ? r.Bottom : r.Top;
                float mx = Math.Clamp(p.X, 0, _img.Width), my = Math.Clamp(p.Y, 0, _img.Height);
                float w = Math.Max(8, Math.Abs(mx - ax)), h = Math.Max(8, Math.Abs(my - ay));
                if (Aspect is float a) { if (w / h > a) w = h * a; else h = w / a; }
                float x = mx < ax ? ax - w : ax, y = my < ay ? ay - h : ay;
                r = new RectangleF(x, y, w, h);
            }
            _sel = Clamp(r);
            Invalidate();
            Changed?.Invoke();
        }

        protected override void OnMouseUp(MouseEventArgs e) => _drag = Drag.None;

        /// <summary>Tekerlek: çerçeveyi merkezi sabit büyütür/küçültür (oran korunur).</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float k = e.Delta > 0 ? 0.9f : 1.1f;
            float w = Math.Clamp(_sel.Width * k, 16, _img.Width), h = _sel.Height * (w / _sel.Width);
            if (h > _img.Height) { h = _img.Height; w = _sel.Width * (h / _sel.Height); }
            var c = new PointF(_sel.X + _sel.Width / 2, _sel.Y + _sel.Height / 2);
            _sel = Clamp(new RectangleF(c.X - w / 2, c.Y - h / 2, w, h));
            Invalidate();
            Changed?.Invoke();
        }

        /// <summary>Çerçeveyi görselin içinde tutar (boyutu korunarak kaydırılır).</summary>
        private RectangleF Clamp(RectangleF r)
        {
            r.Width = Math.Min(r.Width, _img.Width);
            r.Height = Math.Min(r.Height, _img.Height);
            r.X = Math.Clamp(r.X, 0, _img.Width - r.Width);
            r.Y = Math.Clamp(r.Y, 0, _img.Height - r.Height);
            return r;
        }
    }
}
