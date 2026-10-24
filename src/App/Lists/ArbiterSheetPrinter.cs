using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Lists;

/// <summary>Bir kategorinin hakem kağıdı: turun eşleştirmesi. <paramref name="Group"/>: elle sayfa grubu (aynı numara aynı sayfaya).</summary>
public sealed record ArbiterSheet(string Category, int Round, string? When, Tournament Pairings, int Group = 1);

/// <summary>Kategorilerin kağıtlara dağılımı.</summary>
public enum SheetLayout
{
    /// <summary>Her kategori ayrı sayfada başlar (döner turnuvada aynı kategorinin turları birlikte kalır).</summary>
    Separate,
    /// <summary>Sığdıkça aynı sayfaya (kağıt tasarrufu); sığmayan kategori yeni sayfadan başlar.</summary>
    Fit,
    /// <summary>Elle gruplar: aynı grup numaralı kategoriler aynı sayfada, her grup yeni sayfada.</summary>
    Groups
}

/// <summary>Ceza bildirim formu.</summary>
public enum PenaltyForm
{
    None,
    /// <summary>Her eşleştirme sayfasının altında boş form; arada silik kesik katlama/kesme çizgisi.</summary>
    Below,
    /// <summary>Ayrı tam sayfa boş form.</summary>
    BlankPage,
    /// <summary>Ayrı sayfalarda, basılan turların sporcularıyla (masa no, ad soyad, tur dolu) form.</summary>
    FilledPage
}

/// <summary>
/// Hakemlerin tur başında kullandığı eşleştirme kağıtları (A4 dikey): Swiss-Manager çıktısından iri
/// yazı, her masada sonucun yazılacağı kalın çerçeveli kutu, her kategorinin üstünde büyük ve kalın
/// kategori adı (aynı kağıttaki kategoriler karışmasın). Kategoriler ayrı sayfalara, sığdıkça aynı
/// sayfaya ya da elle seçilen gruplara göre dizilir; uzun kategori sonraki sayfada "(devam)" ile sürer.
/// İsteğe bağlı ceza bildirim formu: sayfaların altında (en çok yarım sayfa, katlama çizgisiyle) ya da
/// ayrı sayfalarda boş veya sporcularla doldurulmuş. Ceza formunun satırları 1,2 cm: tükenmez kalemle
/// rahat yazılsın.
/// </summary>
public sealed class ArbiterSheetPrinter
{
    private const float PageW = 595.28f, PageH = 841.89f, Margin = 30f;
    private const float PageHeadH = 34f, CatHeadH = 40f, ColHeadH = 18f, BlockGap = 16f, FooterH = 16f;
    // Ceza bildirim formu (punto): 34 pt ≈ 1,2 cm satır
    private const float PenRowH = 34f, PenTitleH = 26f, PenSubH = 16f, PenHeadH = 26f, PenSignH = 40f, FoldGap = 14f;
    private const int PenMinRows = 3;
    private static readonly Color Green = Color.FromArgb(95, 122, 70);
    private static readonly Color Zebra = Color.FromArgb(245, 247, 242);
    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    private sealed record Slice(ArbiterSheet Sheet, int From, int To, bool Continued);

    /// <summary>Ceza formunun dolu satırı.</summary>
    private sealed record PenaltyRow(string Name, string Board, int Round);

    /// <summary>Sayfadaki ceza formu: altta (Below) ya da tam sayfa; tam sayfada dolu satırlar ve alt başlık.</summary>
    private sealed record PenaltyBlock(bool Below, string? Subtitle, List<PenaltyRow> Rows);

    /// <summary>Sayfa: eşleştirme dilimleri ve ceza formu; <paramref name="Lower"/>: sayfaya 2 formda alttaki form.</summary>
    private sealed record Page(List<Slice> Slices, PenaltyBlock? Penalty, PenaltyBlock? Lower = null);

    private readonly List<Page> _pages;
    private readonly string _eventName;
    private readonly float _pt;          // oyuncu adı puntosu
    private readonly float _rowH;
    private int _pageIndex;

    /// <param name="fontPt">Ad puntosu: 11 (normal), 12.5 (büyük, varsayılan), 14 (çok büyük).</param>
    /// <param name="penalty">Ceza bildirim formu.</param>
    /// <param name="penaltyOnly">Yalnız ceza formu sayfaları (ayrı sayfa seçeneklerinde; eşleştirme kağıtları basılmaz).</param>
    /// <param name="twoPerPage">Ayrı sayfa ceza formunda bir A4'e iki form (ortadan kesilir).</param>
    public ArbiterSheetPrinter(IReadOnlyList<ArbiterSheet> sheets, string eventName, SheetLayout layout, float fontPt = 12.5f,
                               PenaltyForm penalty = PenaltyForm.None, bool penaltyOnly = false, bool twoPerPage = false)
    {
        _eventName = eventName;
        _pt = fontPt;
        _rowH = (float)Math.Round(fontPt * 2.0f, 1); // 12.5 pt → 25 pt satır: el yazısına yer
        bool separatePages = penalty is PenaltyForm.BlankPage or PenaltyForm.FilledPage;
        _pages = penaltyOnly && separatePages ? new List<Page>() : Paginate(sheets, layout, penalty == PenaltyForm.Below);
        if (separatePages) _pages.AddRange(PenaltyPages(sheets, penalty == PenaltyForm.FilledPage, twoPerPage));
        if (_pages.Count == 0) _pages.Add(new Page(new List<Slice>(), null));
    }

    public int PageCount => _pages.Count;

    private float BlockHeight(int rows) => CatHeadH + ColHeadH + rows * _rowH;

    /// <summary>Sayfa altındaki ceza formunun en az yüksekliği (katlama boşluğu dahil): başlık + 3 satır + imza.</summary>
    private static float BelowMinH => FoldGap * 2 + PenTitleH + PenHeadH + PenMinRows * PenRowH + PenSignH;

    private List<Page> Paginate(IReadOnlyList<ArbiterSheet> sheets, SheetLayout layout, bool penaltyBelow)
    {
        var pages = new List<Page>();
        float top = Margin + PageHeadH, bottom = PageH - Margin - FooterH - (penaltyBelow ? BelowMinH : 0);
        List<Slice>? page = null;
        float y = top;
        int? group = null;
        string? category = null;

        void NewPage()
        {
            page = new List<Slice>();
            pages.Add(new Page(page, penaltyBelow ? new PenaltyBlock(true, null, new()) : null));
            y = top;
        }

        foreach (var s in sheets)
        {
            var rows = Rows(s.Pairings);
            bool newPage = page is null
                || (layout == SheetLayout.Separate && s.Category != category)                  // aynı kategorinin turları birlikte
                || (layout == SheetLayout.Groups && s.Group != group)
                || y + BlockHeight(Math.Min(rows.Count, 3)) > bottom;          // başlık + en az 3 masa sığmıyor
            // Sığdır / grup: kategori sayfada kalan yere tümüyle sığmıyorsa ve boş sayfaya sığacaksa yeni sayfadan başlasın.
            if (!newPage && y + BlockHeight(rows.Count) > bottom && BlockHeight(rows.Count) <= bottom - top) newPage = true;
            if (newPage) NewPage();
            group = s.Group;
            category = s.Category;

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
        return pages;
    }

    /// <summary>
    /// Ayrı sayfa ceza formları: boş form (tam sayfa ya da sayfaya iki) veya her kağıdın (kategori + tur)
    /// sporcuları masa sırasıyla. Sayfaya ikide formlar yarım sayfalara sırayla dizilir (iki kategori aynı
    /// kağıdı paylaşabilir); ortada silik kesme çizgisi.
    /// </summary>
    private static IEnumerable<Page> PenaltyPages(IReadOnlyList<ArbiterSheet> sheets, bool filled, bool twoPerPage)
    {
        bool sub = filled && sheets.Count > 0;
        float area = twoPerPage ? HalfBottom - Margin - FooterH : PageH - 2 * Margin - FooterH;   // alttaki formun altında sayfa numarası
        int perForm = FormRows(area, sub);
        var forms = new List<PenaltyBlock>();
        if (!sub)
            forms.Add(new PenaltyBlock(false, null, new()));
        else
            foreach (var s in sheets)
            {
                var rows = new List<PenaltyRow>();
                foreach (var p in s.Pairings.Pairings.Where(p => !p.IsBye).OrderBy(p => p.Board))
                {
                    rows.Add(new PenaltyRow(p.White.Name, p.BoardText, s.Round));
                    rows.Add(new PenaltyRow(p.Black!.Name, p.BoardText, s.Round));
                }
                var title = $"{s.Category.ToUpper(Tr)} • {s.Round}. TUR";
                for (int i = 0; i < Math.Max(1, rows.Count); i += perForm)
                    forms.Add(new PenaltyBlock(false, i == 0 ? title : title + "  (devam)", rows.Skip(i).Take(perForm).ToList()));
            }

        if (!twoPerPage)
        {
            foreach (var f in forms) yield return new Page(new List<Slice>(), f);
            yield break;
        }
        if (!sub) forms.Add(forms[0]);   // boş form: sayfada iki kopya
        for (int i = 0; i < forms.Count; i += 2)
            yield return new Page(new List<Slice>(), forms[i], i + 1 < forms.Count ? forms[i + 1] : null);
    }

    /// <summary>Sayfaya iki formda üst formun bitişi (kesme çizgisinin üstü).</summary>
    private static float HalfBottom => PageH / 2 - FoldGap;

    private static int FormRows(float height, bool subtitle)
        => (int)((height - PenTitleH - (subtitle ? PenSubH : 0) - PenHeadH - PenSignH) / PenRowH);

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
        g.SmoothingMode = SmoothingMode.AntiAlias;
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
            g.SmoothingMode = SmoothingMode.AntiAlias;
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
        var page = _pages[index];
        float x = Margin, w = PageW - 2 * Margin, y = Margin;
        float bottom = PageH - Margin - FooterH;
        bool fullPenalty = page.Penalty is { Below: false };

        if (fullPenalty && page.Lower is { } lower)
        {
            // Sayfaya iki form: üstteki yarım sayfada, alttaki kesme çizgisinin altında
            DrawPenalty(g, page.Penalty!, x, y, w, HalfBottom - FooterH);   // iki form aynı boyda
            DrawFold(g, PageH / 2);
            DrawPenalty(g, lower, x, PageH / 2 + FoldGap, w, bottom);
        }
        else if (fullPenalty)
            DrawPenalty(g, page.Penalty!, x, y, w, bottom);
        else
        {
            using (var fEvent = new Font("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point))
            using (var fSmall = new Font("Segoe UI", 8.5f, GraphicsUnit.Point))
            using (var gray = new SolidBrush(Color.FromArgb(80, 80, 80)))
            {
                Text(g, _eventName, fEvent, gray, new RectangleF(x, y, w - 150, 18), StringAlignment.Near);
                Text(g, "HAKEM EŞLEŞTİRME KAĞIDI", fSmall, gray, new RectangleF(x + w - 160, y, 160, 18), StringAlignment.Far);
            }
            using (var thin = new Pen(Color.FromArgb(170, 170, 170), 0.6f)) g.DrawLine(thin, x, y + 22, x + w, y + 22);
            y += PageHeadH;

            foreach (var sl in page.Slices)
            {
                DrawBlock(g, sl, x, y, w);
                y += BlockHeight(sl.To - sl.From) + BlockGap;
            }

            if (page.Penalty is { Below: true } below)
            {
                // Katlama/kesme çizgisi: sayfanın ortası, eşleştirme uzunsa onun hemen altı (form en çok yarım sayfa)
                float fold = Math.Max(PageH / 2, y - BlockGap + FoldGap);
                DrawFold(g, fold);
                DrawPenalty(g, below, x, fold + FoldGap, w, bottom);
            }
        }

        using var fFoot = new Font("Segoe UI", 7.5f, GraphicsUnit.Point);
        using var footBrush = new SolidBrush(Color.Gray);
        var foot = new RectangleF(x, PageH - Margin - 10, w, 12);
        if (!fullPenalty || page.Penalty!.Rows.Count > 0)
            Text(g, $"chess-results verisi • {DateTime.Now:dd.MM.yyyy HH:mm}", fFoot, footBrush, foot, StringAlignment.Near);
        Text(g, $"Sayfa {index + 1} / {_pages.Count}", fFoot, footBrush, foot, StringAlignment.Far);
    }

    /// <summary>Katlama/kesme çizgisi: kağıdın bir kenarından ötekine çok ince, silik, kesik kesik.</summary>
    private static void DrawFold(Graphics g, float y)
    {
        using var dash = new Pen(Color.FromArgb(205, 205, 205), 0.35f) { DashPattern = new[] { 6f, 5f } };
        g.DrawLine(dash, 0, y, PageW, y);
    }

    // Sütunlar: Masa | Beyaz (ad + puan) | ELO | SONUÇ kutusu | Siyah (ad + puan) | ELO
    private static readonly float[] Weights = { 0.62f, 3.2f, 0.62f, 1.5f, 3.2f, 0.62f };

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
        using var fDash = new Font("Segoe UI", _pt, FontStyle.Bold, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);
        using var gray = new SolidBrush(Color.FromArgb(100, 100, 100));
        using var green = new SolidBrush(Green);
        using var white = new SolidBrush(Color.White);
        using var zebra = new SolidBrush(Zebra);
        using var faint = new SolidBrush(Color.FromArgb(150, 150, 150));
        using var line = new Pen(Color.FromArgb(185, 190, 178), 0.6f);
        using var box = new Pen(Color.Black, 1.4f);

        // Kategori başlığı: sol kenarda renkli şerit, büyük kalın ad, sağda tur
        g.FillRectangle(green, x, y + 4, 5, CatHeadH - 12);
        var round = $"{s.Round}. TUR";
        var roundW = g.MeasureString(round, fRound).Width + 4;
        Text(g, s.Category.ToUpper(Tr) + (sl.Continued ? "  (devam)" : ""), fCat, black,
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
                // Sonucun yazılacağı kutu: kalın çerçeve, ortasında beyaz/siyah sonucunu ayıran tire
                var b = RectangleF.Inflate(res, -3, -3f);
                g.FillRectangle(white, b);
                g.DrawRectangle(box, b.X, b.Y, b.Width, b.Height);
                Text(g, "–", fDash, faint, b, StringAlignment.Center);
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

    // Ceza formu sütunları: Adı Soyadı | Tarih | Masa No | Hamle No | Tur | Ceza Nedeni | Verilen Ceza
    private static readonly string[] PenHeads = { "ADI SOYADI", "TARİH", "MASA NO", "HAMLE NO", "TUR", "CEZA NEDENİ", "VERİLEN CEZA" };
    private static readonly float[] PenWeights = { 3.3f, 1.35f, 0.95f, 0.95f, 0.72f, 3.1f, 2.2f };

    /// <summary>Ceza bildirim formu: başlık, tablo (sığdığı kadar satır) ve sol altta hakem imza alanı.</summary>
    private void DrawPenalty(Graphics g, PenaltyBlock pb, float x, float y, float w, float bottom)
    {
        using var fTitle = new Font("Segoe UI", 12.5f, FontStyle.Bold, GraphicsUnit.Point);
        using var fSub = new Font("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Point);
        using var fHead = new Font("Segoe UI", 8f, FontStyle.Bold, GraphicsUnit.Point);
        using var fCell = new Font("Segoe UI", 11f, GraphicsUnit.Point);
        using var fSign = new Font("Segoe UI", 9f, FontStyle.Bold, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);
        using var green = new SolidBrush(Green);
        using var headBg = new SolidBrush(Color.FromArgb(236, 238, 232));
        using var grid = new Pen(Color.FromArgb(90, 90, 90), 0.6f);
        using var frame = new Pen(Color.Black, 1.1f);

        Text(g, $"{_eventName.ToUpper(Tr)} CEZA BİLDİRİM FORMU", fTitle, black, new RectangleF(x, y, w, PenTitleH - 4), StringAlignment.Center);
        y += PenTitleH;
        if (pb.Subtitle is { } sub)
        {
            Text(g, sub, fSub, green, new RectangleF(x, y - 4, w, PenSubH), StringAlignment.Center);
            y += PenSubH;
        }

        float total = PenWeights.Sum();
        var xs = new float[PenWeights.Length + 1];
        xs[0] = x;
        for (int i = 0; i < PenWeights.Length; i++) xs[i + 1] = xs[i] + w * PenWeights[i] / total;

        int rows = Math.Max(PenMinRows, (int)((bottom - PenSignH - y - PenHeadH) / PenRowH));
        rows = Math.Max(rows, pb.Rows.Count);
        float tableTop = y, tableH = PenHeadH + rows * PenRowH;

        g.FillRectangle(headBg, x, y, w, PenHeadH);
        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            for (int i = 0; i < PenHeads.Length; i++)
                g.DrawString(PenHeads[i], fHead, black, new RectangleF(xs[i] + 1, y, xs[i + 1] - xs[i] - 2, PenHeadH), fmt);
        y += PenHeadH;

        for (int r = 0; r < rows; r++)
        {
            if (r < pb.Rows.Count)
            {
                var pr = pb.Rows[r];
                Text(g, pr.Name, fCell, black, PenCell(xs, 0, y), StringAlignment.Near);
                Text(g, pr.Board, fCell, black, PenCell(xs, 2, y), StringAlignment.Center);
                Text(g, pr.Round.ToString(), fCell, black, PenCell(xs, 4, y), StringAlignment.Center);
            }
            y += PenRowH;
            g.DrawLine(grid, x, y, x + w, y);
        }
        g.DrawLine(grid, x, tableTop + PenHeadH, x + w, tableTop + PenHeadH);
        for (int i = 1; i < PenWeights.Length; i++) g.DrawLine(grid, xs[i], tableTop, xs[i], tableTop + tableH);
        g.DrawRectangle(frame, x, tableTop, w, tableH);

        // Sol alt: hakem adı-soyadı ve imza (altında imza için boşluk)
        Text(g, "HAKEM AD-SOYAD-İMZA", fSign, black, new RectangleF(x, y + 6, 220, 16), StringAlignment.Near);
    }

    private static RectangleF PenCell(float[] xs, int i, float y) => new(xs[i] + 5, y, xs[i + 1] - xs[i] - 10, PenRowH);

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
