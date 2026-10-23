using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.App.Overlay;

namespace NotasyonOtomasyonu.App.Badges;

/// <summary>Bir yaka kartı. <paramref name="PhotoPath"/> boşsa fotoğraf yeri boş çerçeve olarak basılır.</summary>
public sealed record BadgeSpec(string Name, string Role, string Grade, Color GradeColor, string? PhotoPath, int Copies);

/// <summary>
/// Hakem yaka kartları: standart 85×54 mm (ya da 90×60 mm) kartlar A4 dikey sayfaya 2 sütun hâlinde
/// (85×54: 2×5, 90×60: 2×4) bitişik dizilir; kesim için ince çizgiler ve kenarlarda köşe (crop) işaretleri.
/// Kart: üstte logo + turnuva adı + vesikalık fotoğraf; ortada ad soyad ve görev; altta dereceye göre
/// renkli şerit ve unvan.
/// </summary>
public sealed class BadgePrinter
{
    public const float Mm = 72f / 25.4f;
    private const float PageW = 595.28f, PageH = 841.89f;

    private readonly List<BadgeSpec> _cards;   // adet kadar açılmış
    private readonly string? _logoPath;
    private readonly string _eventName;
    private readonly bool _cropMarks;
    private readonly string _font;
    public float CardW { get; }
    public float CardH { get; }
    public int Cols { get; }
    public int Rows { get; }
    private int _pageIndex;
    private readonly Dictionary<string, Image?> _images = new();
    private readonly Image? _fallbackLogo;     // logo dosyası yoksa (TSF il logosu)

    public BadgePrinter(IEnumerable<BadgeSpec> badges, string? logoPath, string eventName, string size = "85x54", bool cropMarks = true, string? font = null,
                        Image? fallbackLogo = null)
    {
        _fallbackLogo = fallbackLogo;
        _cards = badges.SelectMany(b => Enumerable.Repeat(b, Math.Max(0, b.Copies))).ToList();
        _logoPath = !string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath) ? logoPath : null;
        _eventName = eventName;
        _cropMarks = cropMarks;
        _font = font ?? CardFonts.Default;
        (CardW, CardH) = size switch
        {
            "90x60" => (90 * Mm, 60 * Mm),
            "95x135" => (95 * Mm, 135 * Mm), // büyük dikey: A4'e 2×2, A6 kılıfa uyar; kenarda kesim işaretine yer kalır
            _ => (85 * Mm, 54 * Mm)
        };
        Cols = 2;
        Rows = (int)((PageH - 2 * 12 * Mm) / CardH);   // en az 12 mm üst/alt pay (yazıcı kenarı + kesim işareti)
    }

    public int PerPage => Cols * Rows;
    public int PageCount => (_cards.Count + PerPage - 1) / PerPage;
    public int CardCount => _cards.Count;

    /// <summary>Kartların sayfadaki sol üst köşesi (ızgara sayfaya ortalı).</summary>
    private PointF Origin => new((PageW - Cols * CardW) / 2, (PageH - Rows * CardH) / 2);

    // ================= Önizleme + yazıcı =================
    public bool PrintWithPreview(IWin32Window owner)
    {
        using var doc = BuildDocument();
        if (PrintRouter.IsSilent) return PrintRouter.Print(doc, owner, PreparePaper);
        return PreviewDialog.Show(owner, doc, PageCount, $"{CardCount} kart • {PageCount} sayfa A4 • {Cols}×{Rows} dizilim",
                                  f => PrintRouter.Print(doc, f, PreparePaper));
    }

    private static bool PreparePaper(PrintDocument doc, IWin32Window owner)
    {
        try
        {
            foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
                if (ps.Kind == PaperKind.A4) { doc.DefaultPageSettings.PaperSize = ps; break; }
        }
        catch (InvalidPrinterException) { }
        doc.DefaultPageSettings.Landscape = false;
        return true;
    }

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = $"{_eventName} - hakem yaka kartları" };
        PrintRouter.ApplyTarget(doc);
        PreparePaper(doc, null!);
        doc.OriginAtMargins = false;
        doc.BeginPrint += (_, _) => _pageIndex = 0;
        doc.EndPrint += (_, _) => { foreach (var i in _images.Values) i?.Dispose(); _images.Clear(); _pageIndex = 0; };
        doc.PrintPage += (s, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Point;
            Prepare(g);
            bool preview = (s as PrintDocument)?.PrintController?.IsPreview == true;
            if (!preview) g.TranslateTransform(-e.PageSettings.HardMarginX / 100f * 72f, -e.PageSettings.HardMarginY / 100f * 72f);
            DrawPage(g, _pageIndex);
            _pageIndex++;
            e.HasMorePages = _pageIndex < PageCount;
        };
        return doc;
    }

    private static void Prepare(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    }

    private Image? Img(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_images.TryGetValue(path, out var img)) return img;
        try { using var fs = File.OpenRead(path); using var src = Image.FromStream(fs); img = new Bitmap(src); }
        catch { img = null; }
        return _images[path] = img;
    }

    // ================= Sayfa ve kart çizimi =================
    public void DrawPage(Graphics g, int page)
    {
        var o = Origin;
        int start = page * PerPage;
        for (int i = 0; i < PerPage && start + i < _cards.Count; i++)
        {
            int r = i / Cols, c = i % Cols;
            var rect = new RectangleF(o.X + c * CardW, o.Y + r * CardH, CardW, CardH);
            DrawCard(g, rect, _cards[start + i], Img(_logoPath) ?? _fallbackLogo, Img(_cards[start + i].PhotoPath), _eventName, _font);
        }
        DrawCutLines(g, o, Math.Min(Rows, (Math.Min(PerPage, _cards.Count - start) + Cols - 1) / Cols));
    }

    /// <summary>Kesim çizgileri (kartlar arası ince gri) ve ızgara dışında köşe işaretleri.</summary>
    private void DrawCutLines(Graphics g, PointF o, int rows)
    {
        if (rows <= 0) return;
        float w = Cols * CardW, h = rows * CardH;
        using var cut = new Pen(Color.FromArgb(170, 170, 170), 0.3f) { DashStyle = DashStyle.Dash };
        for (int c = 0; c <= Cols; c++) g.DrawLine(cut, o.X + c * CardW, o.Y, o.X + c * CardW, o.Y + h);
        for (int r = 0; r <= rows; r++) g.DrawLine(cut, o.X, o.Y + r * CardH, o.X + w, o.Y + r * CardH);
        if (!_cropMarks) return;
        using var mark = new Pen(Color.Black, 0.5f);
        float gap = 2 * Mm, len = 6 * Mm;
        for (int c = 0; c <= Cols; c++)
        {
            float x = o.X + c * CardW;
            g.DrawLine(mark, x, o.Y - gap, x, o.Y - gap - len);
            g.DrawLine(mark, x, o.Y + h + gap, x, o.Y + h + gap + len);
        }
        for (int r = 0; r <= rows; r++)
        {
            float y = o.Y + r * CardH;
            g.DrawLine(mark, o.X - gap, y, o.X - gap - len, y);
            g.DrawLine(mark, o.X + w + gap, y, o.X + w + gap + len, y);
        }
    }

    /// <summary>Tek kart (punto). Ölçüler kart boyutuna oranlıdır; 85×54 ve 90×60 aynı düzeni kullanır.</summary>
    public static void DrawCard(Graphics g, RectangleF r, BadgeSpec b, Image? logo, Image? photo, string eventName, string? font)
    {
        if (r.Height > r.Width) { DrawPortrait(g, r, b, logo, photo, eventName, font); return; }
        float u = r.Height / 54f;                     // 1 "mm" (54 mm yüksekliğe göre)
        float m = 3 * u;
        using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, r);

        // --- üst bölüm: logo | turnuva adı | fotoğraf ---
        float topH = 26 * u;
        var photoBox = new RectangleF(r.Right - m - topH * 0.75f, r.Y + m, topH * 0.75f, topH); // vesikalık 3:4
        var logoBox = new RectangleF(r.X + m, r.Y + m + 1.5f * u, topH - 3 * u, topH - 3 * u);
        if (logo is not null)
            g.DrawImage(logo, CardLayout.FitImage(new SizeF(logo.Width, logo.Height), logoBox));
        else logoBox.Width = 0;
        var titleBox = RectangleF.FromLTRB(logoBox.Right + (logo is null ? 0 : 2 * u), r.Y + m, photoBox.Left - 2 * u, r.Y + m + topH);
        var title = CardLayout.Fit(eventName.ToUpper(new System.Globalization.CultureInfo("tr-TR")), titleBox, 9, font, 4.5f, 4);
        CardLayout.DrawLines(g, title, titleBox, Color.FromArgb(55, 55, 55), font);

        if (photo is not null)
        {
            // Fotoğraf kutuyu doldurur (taşan kenarlar kırpılır).
            var state = g.Save();
            g.SetClip(photoBox);
            float s = Math.Max(photoBox.Width / photo.Width, photoBox.Height / photo.Height);
            float pw = photo.Width * s, ph = photo.Height * s;
            g.DrawImage(photo, photoBox.X + (photoBox.Width - pw) / 2, photoBox.Y + (photoBox.Height - ph) / 2, pw, ph);
            g.Restore(state);
        }
        else
        {
            using var dash = new Pen(Color.FromArgb(160, 160, 160), 0.6f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dash, photoBox.X, photoBox.Y, photoBox.Width, photoBox.Height);
            using var hint = new Font("Segoe UI", 5f * u / Mm, GraphicsUnit.Point); // kart büyüdükçe orantılı
            using var hb = new SolidBrush(Color.FromArgb(150, 150, 150));
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("FOTOĞRAF", hint, hb, photoBox, fmt);
        }

        // --- orta: ad soyad ve görev ---
        var nameBox = new RectangleF(r.X + m, r.Y + m + topH + 1 * u, r.Width - 2 * m, 8 * u);
        CardLayout.DrawLines(g, CardLayout.Fit(b.Name, nameBox, 14, font, 6, 1), nameBox, Color.FromArgb(20, 20, 20), font);
        var roleBox = new RectangleF(r.X + m, nameBox.Bottom, r.Width - 2 * m, 6 * u);
        CardLayout.DrawLines(g, CardLayout.Fit(ReportUpper(b.Role), roleBox, 9, font, 5, 1), roleBox, b.GradeColor, font);

        // --- alt: dereceye göre renkli şerit ---
        var strip = RectangleF.FromLTRB(r.X, r.Bottom - 9 * u, r.Right, r.Bottom);
        using (var sb = new SolidBrush(b.GradeColor)) g.FillRectangle(sb, strip);
        var stripText = RectangleF.Inflate(strip, -m, -1.5f * u);
        CardLayout.DrawLines(g, CardLayout.Fit(ReportUpper(b.Grade), stripText, 10, font, 5, 1), stripText, CardLayout.TextColorFor(b.GradeColor), font);
    }

    /// <summary>
    /// Büyük dikey kart (95×135 mm): üstte logo ve turnuva adı, ortada büyük vesikalık, altında
    /// ad soyad ve görev, en altta dereceye göre renkli şerit. Ölçüler 95 mm genişliğe oranlıdır.
    /// </summary>
    private static void DrawPortrait(Graphics g, RectangleF r, BadgeSpec b, Image? logo, Image? photo, string eventName, string? font)
    {
        float u = r.Width / 95f, m = 5 * u;
        using (var white = new SolidBrush(Color.White)) g.FillRectangle(white, r);
        float y = r.Y + m;

        // Logo (ortalı) + turnuva adı
        if (logo is not null)
        {
            var logoBox = new RectangleF(r.X + m, y, r.Width - 2 * m, 20 * u);
            g.DrawImage(logo, CardLayout.FitImage(new SizeF(logo.Width, logo.Height), logoBox));
            y = logoBox.Bottom + 2 * u;
        }
        var titleBox = new RectangleF(r.X + m, y, r.Width - 2 * m, logo is null ? 24 * u : 14 * u);
        CardLayout.DrawLines(g, CardLayout.Fit(eventName.ToUpper(new System.Globalization.CultureInfo("tr-TR")), titleBox, 13, font, 6, 3),
            titleBox, Color.FromArgb(55, 55, 55), font);
        y = titleBox.Bottom + 3 * u;

        // Vesikalık (3:4), ortalı; ince derece renginde çerçeve
        float ph = 50 * u, pw = ph * 0.75f;
        var photoBox = new RectangleF(r.X + (r.Width - pw) / 2, y, pw, ph);
        if (photo is not null)
        {
            var state = g.Save();
            g.SetClip(photoBox);
            float s = Math.Max(photoBox.Width / photo.Width, photoBox.Height / photo.Height);
            float iw = photo.Width * s, ih = photo.Height * s;
            g.DrawImage(photo, photoBox.X + (photoBox.Width - iw) / 2, photoBox.Y + (photoBox.Height - ih) / 2, iw, ih);
            g.Restore(state);
            using var frame = new Pen(b.GradeColor, 1.2f);
            g.DrawRectangle(frame, photoBox.X, photoBox.Y, photoBox.Width, photoBox.Height);
        }
        else
        {
            using var dash = new Pen(Color.FromArgb(160, 160, 160), 0.8f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(dash, photoBox.X, photoBox.Y, photoBox.Width, photoBox.Height);
            using var hint = new Font("Segoe UI", 9f, GraphicsUnit.Point);
            using var hb = new SolidBrush(Color.FromArgb(150, 150, 150));
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("FOTOĞRAF", hint, hb, photoBox, fmt);
        }
        y = photoBox.Bottom + 3 * u;

        // Ad soyad (en çok 2 satır) ve görev
        var strip = RectangleF.FromLTRB(r.X, r.Bottom - 14 * u, r.Right, r.Bottom);
        var roleBox = new RectangleF(r.X + m, strip.Top - 3 * u - 9 * u, r.Width - 2 * m, 9 * u);
        var nameBox = RectangleF.FromLTRB(r.X + m, y, r.Right - m, roleBox.Top - 1 * u);
        CardLayout.DrawLines(g, CardLayout.Fit(b.Name, nameBox, 22, font, 9, 2), nameBox, Color.FromArgb(20, 20, 20), font);
        CardLayout.DrawLines(g, CardLayout.Fit(ReportUpper(b.Role), roleBox, 16, font, 8, 1), roleBox, b.GradeColor, font);

        using (var sb = new SolidBrush(b.GradeColor)) g.FillRectangle(sb, strip);
        var stripText = RectangleF.Inflate(strip, -m, -2.5f * u);
        CardLayout.DrawLines(g, CardLayout.Fit(ReportUpper(b.Grade), stripText, 16, font, 8, 1), stripText, CardLayout.TextColorFor(b.GradeColor), font);
    }

    private static string ReportUpper(string s) => (s ?? "").ToUpper(new System.Globalization.CultureInfo("tr-TR"));

    // ================= PDF =================
    public void SavePdf(string path)
    {
        try
        {
            GdiPdf.Save(path, Enumerable.Range(0, PageCount).ToList(), PageW, PageH, (g, p) => { Prepare(g); DrawPage(g, p); }, dpi: 300);
        }
        finally { foreach (var i in _images.Values) i?.Dispose(); _images.Clear(); }
    }
}
