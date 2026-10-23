using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Lists;

public sealed record ListColumn(string Header, float Weight, StringAlignment Align = StringAlignment.Center, bool Bold = false);

/// <summary>Bir tablo satırı. <see cref="Group"/>: tüm satıra yayılan ara başlık (takım maçı); <see cref="Muted"/>: soluk (BAY).</summary>
public sealed record ListRow(IReadOnlyList<string> Cells, string? Group = null, bool Muted = false);

/// <summary>Başlıklı bir tablo ("5. Tur Eşleştirmesi", "4. Turdan Sonra Sıralama").</summary>
public sealed record ListSection(string Heading, string? Info, IReadOnlyList<ListColumn> Columns, IReadOnlyList<ListRow> Rows);

/// <summary>Bir kategorinin listesi; her blok yeni sayfada başlar.</summary>
public sealed record ListBlock(string EventName, string Category, IReadOnlyList<ListSection> Sections);

/// <summary>
/// Salon panosu için eşleştirme / sonuç / sıralama listeleri (A4 dikey). Uzun tablolar sayfalara
/// bölünür, başlık satırı her sayfada tekrar eder; her kategori yeni sayfadan başlar.
/// Önizleme, "doğrudan yazdır" ve yazıcı ayarları notasyon/kart baskısıyla aynıdır.
/// </summary>
public sealed class ListPrinter
{
    private const float PageW = 595.28f, PageH = 841.89f, Margin = 34f;
    private const float RowH = 19f, HeadRowH = 21f, BlockHeadH = 58f, SectionHeadH = 30f, FooterH = 18f;
    private static readonly Color Green = Color.FromArgb(95, 122, 70);
    private static readonly Color Zebra = Color.FromArgb(243, 246, 239);
    private static readonly Color Line = Color.FromArgb(200, 205, 192);

    private sealed record Slice(ListSection Section, int From, int To, bool Continued);
    private sealed record Page(ListBlock Block, List<Slice> Slices, bool BlockContinued);

    private readonly List<Page> _pages;
    private readonly string _docName;
    private readonly DateTime _printedAt = DateTime.Now;
    private int _pageIndex;

    public ListPrinter(IReadOnlyList<ListBlock> blocks, string docName)
    {
        _docName = docName;
        _pages = Paginate(blocks);
    }

    public int PageCount => _pages.Count;

    // ================= Sayfalama =================
    private static List<Page> Paginate(IReadOnlyList<ListBlock> blocks)
    {
        var pages = new List<Page>();
        float bottom = PageH - Margin - FooterH;
        foreach (var block in blocks)
        {
            var page = new Page(block, new List<Slice>(), false);
            pages.Add(page);
            float y = Margin + BlockHeadH;
            foreach (var s in block.Sections)
            {
                int from = 0;
                bool continued = false;
                do
                {
                    float head = SectionHeadH + HeadRowH;
                    int minRows = Math.Min(3, Math.Max(1, s.Rows.Count - from));
                    if (y + head + minRows * RowH > bottom && page.Slices.Count > 0)
                    {
                        page = new Page(block, new List<Slice>(), true);
                        pages.Add(page);
                        y = Margin + BlockHeadH;
                    }
                    int fit = Math.Max(1, (int)((bottom - y - head) / RowH));
                    int to = Math.Min(s.Rows.Count, from + fit);
                    // Maç ara başlığı sayfanın son satırında yalnız kalmasın: sonraki sayfaya geçsin.
                    if (to < s.Rows.Count && to - 1 > from && s.Rows[to - 1].Group is not null) to--;
                    page.Slices.Add(new Slice(s, from, to, continued));
                    y += head + (to - from) * RowH + 12;
                    from = to;
                    continued = true;
                    if (from < s.Rows.Count)
                    {
                        page = new Page(block, new List<Slice>(), true);
                        pages.Add(page);
                        y = Margin + BlockHeadH;
                    }
                } while (from < s.Rows.Count);
            }
        }
        return pages;
    }

    // ================= Önizleme + yazıcı + PDF =================
    /// <summary>Önizleme açar ("doğrudan yazdır" açıksa önizlemesiz basar). true = yazıcıya gönderildi.</summary>
    public bool PrintWithPreview(IWin32Window owner)
    {
        using var doc = BuildDocument();
        if (PrintRouter.IsSilent) return PrintRouter.Print(doc, owner, PreparePaper);
        return PreviewDialog.Show(owner, doc, PageCount, $"{PageCount} sayfa • A4 dikey",
                                  f => PrintRouter.Print(doc, f, PreparePaper));
    }

    public void SavePdf(string path)
        => GdiPdf.Save(path, Enumerable.Range(0, _pages.Count).ToList(), PageW, PageH, (g, i) => DrawPage(g, i));

    private static bool PreparePaper(PrintDocument doc, IWin32Window owner)
    {
        if (FindA4(doc) is { } a4) doc.DefaultPageSettings.PaperSize = a4;
        doc.DefaultPageSettings.Landscape = false;
        return true;
    }

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = _docName };
        PrintRouter.ApplyTarget(doc);
        if (FindA4(doc) is { } a4) doc.DefaultPageSettings.PaperSize = a4;
        doc.DefaultPageSettings.Landscape = false;
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

    private static PaperSize? FindA4(PrintDocument doc)
    {
        try
        {
            foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
                if (ps.Kind == PaperKind.A4) return ps;
        }
        catch (InvalidPrinterException) { }
        return null;
    }

    // ================= Çizim (punto) =================
    private void DrawPage(Graphics g, int index)
    {
        var page = _pages[index];
        float x = Margin, w = PageW - 2 * Margin, y = Margin;

        using (var fEvent = new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Point))
        using (var fCat = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Point))
        using (var gray = new SolidBrush(Color.FromArgb(90, 90, 90)))
        using (var green = new SolidBrush(Green))
        {
            DrawText(g, page.Block.EventName, fEvent, gray, new RectangleF(x, y, w, 18), StringAlignment.Near);
            DrawText(g, page.Block.Category + (page.BlockContinued ? "  (devam)" : ""), fCat, green, new RectangleF(x, y + 18, w, 30), StringAlignment.Near);
        }
        using (var pen = new Pen(Green, 1.5f)) g.DrawLine(pen, x, y + BlockHeadH - 8, x + w, y + BlockHeadH - 8);
        y += BlockHeadH;

        foreach (var s in page.Slices)
        {
            DrawSection(g, s, x, y, w);
            y += SectionHeadH + HeadRowH + (s.To - s.From) * RowH + 12;
        }

        using var fFoot = new Font("Segoe UI", 7.5f, GraphicsUnit.Point);
        using var footBrush = new SolidBrush(Color.Gray);
        var foot = new RectangleF(x, PageH - Margin - 10, w, 12);
        DrawText(g, $"chess-results verisi • {_printedAt:dd.MM.yyyy HH:mm}", fFoot, footBrush, foot, StringAlignment.Near);
        DrawText(g, $"Sayfa {index + 1} / {_pages.Count}", fFoot, footBrush, foot, StringAlignment.Far);
    }

    private static void DrawSection(Graphics g, Slice sl, float x, float y, float w)
    {
        var s = sl.Section;
        using var fHead = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Point);
        using var fInfo = new Font("Segoe UI", 9.5f, GraphicsUnit.Point);
        using var fCol = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        using var fCell = new Font("Segoe UI", 10.5f, GraphicsUnit.Point);
        using var fCellB = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);
        using var gray = new SolidBrush(Color.FromArgb(110, 110, 110));
        using var white = new SolidBrush(Color.White);
        using var green = new SolidBrush(Green);
        using var zebra = new SolidBrush(Zebra);
        using var line = new Pen(Line, 0.5f);

        var headSize = g.MeasureString(s.Heading + (sl.Continued ? " (devam)" : ""), fHead);
        DrawText(g, s.Heading + (sl.Continued ? " (devam)" : ""), fHead, black, new RectangleF(x, y + 2, w, 24), StringAlignment.Near);
        if (!string.IsNullOrWhiteSpace(s.Info) && !sl.Continued)
            DrawText(g, s.Info!, fInfo, gray, new RectangleF(x + headSize.Width + 8, y + 7, w - headSize.Width - 8, 18), StringAlignment.Near);
        y += SectionHeadH;

        // Sütun genişlikleri ağırlıklarla.
        float total = s.Columns.Sum(c => c.Weight);
        var xs = new float[s.Columns.Count + 1];
        xs[0] = x;
        for (int i = 0; i < s.Columns.Count; i++) xs[i + 1] = xs[i] + w * s.Columns[i].Weight / total;

        g.FillRectangle(green, x, y, w, HeadRowH);
        for (int i = 0; i < s.Columns.Count; i++)
            DrawText(g, s.Columns[i].Header, fCol, white, Cell(xs, i, y, HeadRowH), s.Columns[i].Align);
        y += HeadRowH;

        for (int r = sl.From; r < sl.To; r++)
        {
            var row = s.Rows[r];
            if ((r - sl.From) % 2 == 1) g.FillRectangle(zebra, x, y, w, RowH);
            if (row.Group is not null)
                DrawText(g, row.Group, fCellB, green, new RectangleF(x + 4, y, w - 8, RowH), StringAlignment.Near);
            else
                for (int i = 0; i < s.Columns.Count && i < row.Cells.Count; i++)
                {
                    var c = s.Columns[i];
                    DrawText(g, row.Cells[i], c.Bold ? fCellB : fCell, row.Muted ? gray : black, Cell(xs, i, y, RowH), c.Align);
                }
            g.DrawLine(line, x, y + RowH, x + w, y + RowH);
            y += RowH;
        }
    }

    private static RectangleF Cell(float[] xs, int i, float y, float h) => new(xs[i] + 3, y, xs[i + 1] - xs[i] - 6, h);

    /// <summary>Tek satır; sığmazsa puntoyu 7'ye kadar küçültür, yine sığmazsa "…" ile kısaltır.</summary>
    private static void DrawText(Graphics g, string text, Font font, Brush brush, RectangleF r, StringAlignment align)
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

    // ================= Liste kurucular =================
    private static readonly ListColumn[] PairColumns =
    {
        new("Masa", 0.8f), new("No", 0.5f), new("Beyaz", 3.3f, StringAlignment.Near, Bold: true), new("Rtg", 0.7f), new("Puan", 0.75f),
        new("Sonuç", 0.95f, Bold: true),
        new("Puan", 0.75f), new("Siyah", 3.3f, StringAlignment.Near, Bold: true), new("Rtg", 0.7f), new("No", 0.5f)
    };

    /// <summary>Bir turun eşleştirme (results=false) ya da sonuç (results=true) tablosu.</summary>
    public static ListSection RoundSection(Tournament t, int round, bool results, string? info)
    {
        var rows = new List<ListRow>();
        string? lastMatch = null;
        foreach (var p in t.Pairings.OrderBy(p => p.Board))
        {
            // Takım: maç değişince "3. Maç: A TAKIMI — B TAKIMI" ara başlığı.
            if (p.BoardLabel is { } lbl && lbl.Contains('.'))
            {
                var match = lbl[..lbl.IndexOf('.')];
                if (match != lastMatch)
                {
                    rows.Add(new ListRow(Array.Empty<string>(), $"{match}. Maç: {p.WhiteTeam} — {p.BlackTeam}"));
                    lastMatch = match;
                }
            }
            string res = results ? (p.Result ?? "") : (p.IsBye ? "" : "–");
            rows.Add(new ListRow(new[]
            {
                p.BoardText, p.White.StartNo?.ToString() ?? "", p.White.Name, p.White.Rating?.ToString() ?? "", p.WhitePoints ?? "",
                res,
                p.BlackPoints ?? "", p.IsBye ? "BAY" : p.Black!.Name, p.Black?.Rating?.ToString() ?? "", p.Black?.StartNo?.ToString() ?? ""
            }, Muted: p.IsBye));
        }
        var title = results ? $"{round}. Tur Sonuçları" : $"{round}. Tur Eşleştirmesi";
        // Puan sütunları hiç dolu değilse (ör. Berger sayfasında tur başı puan yok) boş sütun basılmasın.
        if (rows.All(r => r.Group is not null || (r.Cells[4].Length == 0 && r.Cells[6].Length == 0)))
        {
            int[] keep = { 0, 1, 2, 3, 5, 7, 8, 9 };
            return new ListSection(title, info, keep.Select(i => PairColumns[i]).ToList(),
                rows.Select(r => r.Group is not null ? r : r with { Cells = keep.Select(i => r.Cells[i]).ToList() }).ToList());
        }
        return new ListSection(title, info, PairColumns, rows);
    }

    /// <summary>chess-results sıralama tablosu (sütunlar olduğu gibi; isim sütunu geniş ve sola yaslı).</summary>
    public static ListSection StandingsSection(ListTable table)
    {
        var cols = table.Headers.Select(h =>
        {
            bool name = h is "İsim" or "Name" or "Takım" or "Team" or "Kulüp/Şehir" or "Club/City";
            bool pts = h is "Puan" or "Pts." or "Pts";
            return new ListColumn(h, name ? 3.6f : h.Length > 4 ? 1f : 0.7f, name ? StringAlignment.Near : StringAlignment.Center, Bold: name || pts);
        }).ToList();
        return new ListSection(table.Title, null, cols, table.Rows.Select(r => new ListRow(r)).ToList());
    }
}
