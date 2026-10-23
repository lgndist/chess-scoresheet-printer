using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Lists;

/// <summary>Bir kategorinin hakem kağıdı: turun eşleştirmesi. <paramref name="Group"/>: elle sayfa grubu (aynı numara aynı sayfaya).</summary>
public sealed record ArbiterSheet(string Category, int Round, string? When, Tournament Pairings, int Group = 1);

/// <summary>Kategorilerin kağıtlara dağılımı.</summary>
public enum SheetLayout
{
    /// <summary>Her kategori ayrı sayfada başlar.</summary>
    Separate,
    /// <summary>Sığdıkça aynı sayfaya (kağıt tasarrufu); sığmayan kategori yeni sayfadan başlar.</summary>
    Fit,
    /// <summary>Elle gruplar: aynı grup numaralı kategoriler aynı sayfada, her grup yeni sayfada.</summary>
    Groups
}

/// <summary>
/// Hakemlerin tur başında kullandığı eşleştirme kağıtları (A4 dikey): Swiss-Manager çıktısından iri
/// yazı, her masada sonucun yazılacağı kalın çerçeveli kutu, her kategorinin üstünde büyük ve kalın
/// kategori adı (aynı kağıttaki kategoriler karışmasın). Kategoriler ayrı sayfalara, sığdıkça aynı
/// sayfaya ya da elle seçilen gruplara göre dizilir; uzun kategori sonraki sayfada "(devam)" ile sürer.
/// </summary>
public sealed class ArbiterSheetPrinter
{
    private const float PageW = 595.28f, PageH = 841.89f, Margin = 30f;
    private const float PageHeadH = 34f, CatHeadH = 40f, ColHeadH = 18f, BlockGap = 16f, FooterH = 16f;
    private static readonly Color Green = Color.FromArgb(95, 122, 70);
    private static readonly Color Zebra = Color.FromArgb(245, 247, 242);

    private sealed record Slice(ArbiterSheet Sheet, int From, int To, bool Continued);

    private readonly List<List<Slice>> _pages;
    private readonly string _eventName;
    private readonly float _pt;          // oyuncu adı puntosu
    private readonly float _rowH;
    private int _pageIndex;

    /// <param name="fontPt">Ad puntosu: 11 (normal), 12.5 (büyük, varsayılan), 14 (çok büyük).</param>
    public ArbiterSheetPrinter(IReadOnlyList<ArbiterSheet> sheets, string eventName, SheetLayout layout, float fontPt = 12.5f)
    {
        _eventName = eventName;
        _pt = fontPt;
        _rowH = (float)Math.Round(fontPt * 2.0f, 1); // 12.5 pt → 25 pt satır: el yazısına yer
        _pages = Paginate(sheets, layout);
    }

    public int PageCount => _pages.Count;

    private float BlockHeight(int rows) => CatHeadH + ColHeadH + rows * _rowH;

    private List<List<Slice>> Paginate(IReadOnlyList<ArbiterSheet> sheets, SheetLayout layout)
    {
        var pages = new List<List<Slice>>();
        float top = Margin + PageHeadH, bottom = PageH - Margin - FooterH;
        List<Slice>? page = null;
        float y = top;
        int? group = null;

        void NewPage() { page = new List<Slice>(); pages.Add(page); y = top; }

        foreach (var s in sheets)
        {
            var rows = Rows(s.Pairings);
            bool newPage = page is null
                || layout == SheetLayout.Separate
                || (layout == SheetLayout.Groups && s.Group != group)
                || y + BlockHeight(Math.Min(rows.Count, 3)) > bottom;          // başlık + en az 3 masa sığmıyor
            // Sığdır / grup: kategori sayfada kalan yere tümüyle sığmıyorsa ve boş sayfaya sığacaksa yeni sayfadan başlasın.
            if (!newPage && y + BlockHeight(rows.Count) > bottom && BlockHeight(rows.Count) <= bottom - top) newPage = true;
            if (newPage) NewPage();
            group = s.Group;

            int from = 0;
            bool continued = false;
            do
            {
                int fit = Math.Max(1, (int)((bottom - y - CatHeadH - ColHeadH) / _rowH));
                int to = Math.Min(rows.Count, from + fit);
                // Maç ara başlığı sayfanın son satırında yalnız kalmasın: sonraki sayfaya geçsin.
                if (to < rows.Count && to - 1 > from && rows[to - 1].Match is not null) to--;
                page!.Add(new Slice(s, from, to, continued));
                y += BlockHeight(to - from) + BlockGap;
                from = to;
                continued = true;
                if (from < rows.Count) NewPage();
            } while (from < rows.Count);
        }
        if (pages.Count == 0) pages.Add(new List<Slice>());
        return pages;
    }

    /// <summary>Kağıttaki bir satır: masa ya da (takım turnuvasında) maç ara başlığı.</summary>
    private sealed record Row(Pairing? Pairing, string? Match);

    /// <summary>
    /// Basılacak satırlar: masa sırasıyla, BAY en sonda; takım turnuvasında her maçın önüne
    /// "1. Maç: A TAKIMI — B TAKIMI" ara başlığı (hangi masanın hangi maça ait olduğu karışmasın).
    /// </summary>
    private static List<Row> Rows(Tournament t)
    {
        var rows = new List<Row>();
        string? last = null;
        foreach (var p in t.Pairings.OrderBy(p => p.IsBye).ThenBy(p => p.Board))
        {
            if (p.BoardLabel is { } lbl && lbl.Contains('.'))
            {
                var match = lbl[..lbl.IndexOf('.')];
                if (match != last)
                {
                    rows.Add(new Row(null, $"{match}. Maç: {p.WhiteTeam} — {p.BlackTeam}"));
                    last = match;
                }
            }
            rows.Add(new Row(p, null));
        }
        return rows;
    }

    // ================= Önizleme + yazıcı + PDF =================
    public bool PrintWithPreview(IWin32Window owner)
    {
        using var doc = BuildDocument();
        if (PrintRouter.IsSilent) return PrintRouter.Print(doc, owner, PreparePaper);
        return PreviewDialog.Show(owner, doc, PageCount, $"{PageCount} sayfa • A4 dikey • hakem eşleştirme kağıdı",
                                  f => PrintRouter.Print(doc, f, PreparePaper));
    }

    public void SavePdf(string path)
        => GdiPdf.Save(path, Enumerable.Range(0, _pages.Count).ToList(), PageW, PageH, (g, i) => DrawPage(g, i));

    /// <summary>Bir sayfayı bitmap olarak çizer (pencere içi küçük önizleme).</summary>
    public Bitmap RenderPage(int index, int widthPx)
    {
        float s = widthPx / PageW;
        var bmp = new Bitmap(widthPx, (int)Math.Ceiling(PageH * s));
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        g.PageUnit = GraphicsUnit.Point;
        float k = s * 72f / bmp.HorizontalResolution;
        g.ScaleTransform(k, k);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        DrawPage(g, Math.Clamp(index, 0, _pages.Count - 1));
        return bmp;
    }

    private static bool PreparePaper(PrintDocument doc, IWin32Window owner)
    {
        foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
            if (ps.Kind == PaperKind.A4) { doc.DefaultPageSettings.PaperSize = ps; break; }
        doc.DefaultPageSettings.Landscape = false;
        return true;
    }

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = $"{_eventName} - hakem eşleştirme kağıtları" };
        PrintRouter.ApplyTarget(doc);
        try { PreparePaper(doc, null!); } catch (InvalidPrinterException) { }
        doc.QueryPageSettings += (_, e) => e.PageSettings.Landscape = false;
        doc.OriginAtMargins = false;
        doc.BeginPrint += (_, _) => _pageIndex = 0;
        doc.PrintPage += (s, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = GraphicsUnit.Point;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            bool preview = (s as PrintDocument)?.PrintController?.IsPreview == true;
            if (!preview) g.TranslateTransform(-e.PageSettings.HardMarginX / 100f * 72f, -e.PageSettings.HardMarginY / 100f * 72f);
            if (_pageIndex < _pages.Count) DrawPage(g, _pageIndex);
            _pageIndex++;
            e.HasMorePages = _pageIndex < _pages.Count;
        };
        return doc;
    }

    // ================= Çizim (punto) =================
    private void DrawPage(Graphics g, int index)
    {
        float x = Margin, w = PageW - 2 * Margin, y = Margin;
        using (var fEvent = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point))
        using (var fSmall = new Font("Segoe UI", 8.5f, GraphicsUnit.Point))
        using (var gray = new SolidBrush(Color.FromArgb(80, 80, 80)))
        {
            Text(g, _eventName, fEvent, gray, new RectangleF(x, y, w - 150, 18), StringAlignment.Near);
            Text(g, "HAKEM EŞLEŞTİRME KAĞIDI", fSmall, gray, new RectangleF(x + w - 160, y, 160, 18), StringAlignment.Far);
        }
        using (var thin = new Pen(Color.FromArgb(170, 170, 170), 0.6f)) g.DrawLine(thin, x, y + 22, x + w, y + 22);
        y += PageHeadH;

        foreach (var sl in _pages[index])
        {
            DrawBlock(g, sl, x, y, w);
            y += BlockHeight(sl.To - sl.From) + BlockGap;
        }

        using var fFoot = new Font("Segoe UI", 7.5f, GraphicsUnit.Point);
        using var footBrush = new SolidBrush(Color.Gray);
        var foot = new RectangleF(x, PageH - Margin - 10, w, 12);
        Text(g, $"chess-results verisi • {DateTime.Now:dd.MM.yyyy HH:mm}", fFoot, footBrush, foot, StringAlignment.Near);
        Text(g, $"Sayfa {index + 1} / {_pages.Count}", fFoot, footBrush, foot, StringAlignment.Far);
    }

    // Sütunlar: Masa | Beyaz (ad + puan) | ELO | SONUÇ kutusu | Siyah (ad + puan) | ELO
    private static readonly float[] Weights = { 0.62f, 3.3f, 0.62f, 1.25f, 3.3f, 0.62f };

    private void DrawBlock(Graphics g, Slice sl, float x, float y, float w)
    {
        var s = sl.Sheet;
        var rows = Rows(s.Pairings);
        using var fCat = new Font("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Point);
        using var fRound = new Font("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point);
        using var fWhen = new Font("Segoe UI", 9f, GraphicsUnit.Point);
        using var fCol = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var fName = new Font("Segoe UI", _pt, GraphicsUnit.Point);
        using var fBoard = new Font("Segoe UI", _pt + 1, FontStyle.Bold, GraphicsUnit.Point);
        using var fSmall = new Font("Segoe UI", _pt - 2.5f, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);
        using var gray = new SolidBrush(Color.FromArgb(100, 100, 100));
        using var green = new SolidBrush(Green);
        using var white = new SolidBrush(Color.White);
        using var zebra = new SolidBrush(Zebra);
        using var line = new Pen(Color.FromArgb(185, 190, 178), 0.6f);
        using var box = new Pen(Color.Black, 1.4f);

        // Kategori başlığı: sol kenarda renkli şerit, büyük kalın ad, sağda tur
        g.FillRectangle(green, x, y + 4, 5, CatHeadH - 12);
        var round = $"{s.Round}. TUR";
        var roundW = g.MeasureString(round, fRound).Width + 4;
        Text(g, s.Category.ToUpper(new System.Globalization.CultureInfo("tr-TR")) + (sl.Continued ? "  (devam)" : ""), fCat, black,
             new RectangleF(x + 12, y, w - roundW - 16, CatHeadH - 6), StringAlignment.Near);
        Text(g, round, fRound, green, new RectangleF(x + w - roundW, y - 2, roundW, 24), StringAlignment.Far);
        if (!string.IsNullOrWhiteSpace(s.When) && !sl.Continued)
            Text(g, s.When!, fWhen, gray, new RectangleF(x + w - 200, y + 20, 200, 14), StringAlignment.Far);
        y += CatHeadH;

        float total = Weights.Sum();
        var xs = new float[Weights.Length + 1];
        xs[0] = x;
        for (int i = 0; i < Weights.Length; i++) xs[i + 1] = xs[i] + w * Weights[i] / total;
        string[] heads = { "Masa", "Beyaz", "ELO", "SONUÇ", "Siyah", "ELO" };
        g.FillRectangle(green, x, y, w, ColHeadH);
        for (int i = 0; i < heads.Length; i++)
            Text(g, heads[i], fCol, white, Cell(xs, i, y, ColHeadH), i is 1 or 4 ? StringAlignment.Near : StringAlignment.Center);
        y += ColHeadH;

        using var fMatch = new Font("Segoe UI", _pt - 1, FontStyle.Bold, GraphicsUnit.Point);
        using var matchBg = new SolidBrush(Color.FromArgb(232, 240, 224));
        for (int r = sl.From; r < sl.To; r++)
        {
            if (rows[r].Match is { } match)
            {
                g.FillRectangle(matchBg, x, y, w, _rowH);
                Text(g, match, fMatch, green, new RectangleF(x + 6, y, w - 12, _rowH), StringAlignment.Near);
                g.DrawLine(line, x, y + _rowH, x + w, y + _rowH);
                y += _rowH;
                continue;
            }
            var p = rows[r].Pairing!;
            if ((r - sl.From) % 2 == 1) g.FillRectangle(zebra, x, y, w, _rowH);
            Text(g, p.IsBye ? "" : p.BoardText, fBoard, black, Cell(xs, 0, y, _rowH), StringAlignment.Center);
            Player(g, p.White.Name, p.WhitePoints, fName, fSmall, black, gray, Cell(xs, 1, y, _rowH));
            Text(g, p.White.Rating?.ToString() ?? "", fSmall, gray, Cell(xs, 2, y, _rowH), StringAlignment.Center);
            var res = Cell(xs, 3, y, _rowH);
            if (p.IsBye)
                Text(g, "BAY", fName, gray, res, StringAlignment.Center);
            else
            {
                // Sonucun yazılacağı kutu: kalın çerçeve, ortasında silik tire
                var b = RectangleF.Inflate(res, -4, -3.5f);
                g.FillRectangle(white, b);
                g.DrawRectangle(box, b.X, b.Y, b.Width, b.Height);
                using var faint = new SolidBrush(Color.FromArgb(200, 200, 200));
                Text(g, "–", fSmall, faint, b, StringAlignment.Center);
            }
            if (!p.IsBye)
            {
                Player(g, p.Black!.Name, p.BlackPoints, fName, fSmall, black, gray, Cell(xs, 4, y, _rowH));
                Text(g, p.Black.Rating?.ToString() ?? "", fSmall, gray, Cell(xs, 5, y, _rowH), StringAlignment.Center);
            }
            g.DrawLine(line, x, y + _rowH, x + w, y + _rowH);
            y += _rowH;
        }
        using var frame = new Pen(Green, 0.8f);
        float top = y - (sl.To - sl.From) * _rowH - ColHeadH;
        g.DrawRectangle(frame, x, top, w, y - top);
    }

    /// <summary>Oyuncu adı ve (varsa) tur başı puanı: "AD SOYAD  (2½)".</summary>
    private static void Player(Graphics g, string name, string? points, Font f, Font fSmall, Brush brush, Brush gray, RectangleF r)
    {
        float pw = 0;
        if (!string.IsNullOrWhiteSpace(points))
        {
            var pts = $"({points})";
            pw = g.MeasureString(pts, fSmall).Width + 2;
            Text(g, pts, fSmall, gray, new RectangleF(r.Right - pw, r.Y, pw, r.Height), StringAlignment.Far);
        }
        Text(g, name, f, brush, new RectangleF(r.X, r.Y, r.Width - pw, r.Height), StringAlignment.Near);
    }

    private static RectangleF Cell(float[] xs, int i, float y, float h) => new(xs[i] + 3, y, xs[i + 1] - xs[i] - 6, h);

    /// <summary>Tek satır; sığmazsa puntoyu 7'ye kadar küçültür, yine sığmazsa "…" ile kısaltır.</summary>
    private static void Text(Graphics g, string text, Font font, Brush brush, RectangleF r, StringAlignment align)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var fmt = new StringFormat(StringFormatFlags.NoWrap) { Alignment = align, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        float size = font.Size;
        Font f = font;
        try
        {
            while (g.MeasureString(text, f, int.MaxValue, fmt).Width > r.Width && size > 7f)
            {
                if (!ReferenceEquals(f, font)) f.Dispose();
                size -= 0.5f;
                f = new Font(font.FontFamily, size, font.Style, GraphicsUnit.Point);
            }
            g.DrawString(text, f, brush, r, fmt);
        }
        finally { if (!ReferenceEquals(f, font)) f.Dispose(); }
    }
}
