using System.Drawing;
using System.Drawing.Printing;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Hazır (önceden basılı) notasyon kağıdına yalnızca değişken yazıları GDI ile doğrudan basar.
/// Koordinatlar fiziksel kağıdın sol-üst köşesine göredir (yazıcı sabit kenar boşluğu telafi edilir),
/// böylece baskı, kağıttaki boşluklarla milimetrik hizalanır.
/// </summary>
public sealed class SheetPrinter
{
    private readonly Tournament _t;
    private readonly string _pageSize;
    private readonly float _offsetXPt, _offsetYPt;
    private readonly List<SheetPage> _pages;
    private readonly Dictionary<string, Image?> _bgs = new();   // şablon görseli (yol → resim)
    private bool _preview;     // önizleme geçişi mi (soluk kağıt gösterilir, baskıya gitmez)
    private int _pageIndex;

    /// <param name="offsetXmm">Yazıcının kağıdı kaydırmasını düzeltmek için tüm yazıları sağa (+) / sola (−) kaydırma.</param>
    /// <param name="offsetYmm">Aşağı (+) / yukarı (−) kaydırma.</param>
    public SheetPrinter(OverlayTemplate tpl, Tournament t, string pageSize = "A4",
                        double offsetXmm = 0, double offsetYmm = 0)
        : this(_ => tpl, t, pageSize, offsetXmm, offsetYmm) { }

    /// <param name="templateFor">Her kağıdın şablonu (ör. sisteme göre: Berger/takım farklı kağıt).</param>
    public SheetPrinter(Func<Pairing, OverlayTemplate> templateFor, Tournament t, string pageSize = "A4",
                        double offsetXmm = 0, double offsetYmm = 0)
    {
        _t = t;
        _pageSize = pageSize;
        _offsetXPt = (float)(offsetXmm / 25.4 * 72.0);
        _offsetYPt = (float)(offsetYmm / 25.4 * 72.0);
        _pages = SheetPages.Build(t.Pairings, templateFor);
    }

    /// <summary>Basılacak sayfa (yaprak) sayısı.</summary>
    public int PageCount => _pages.Count;

    /// <summary>
    /// Yazıcının gerçek baskıda bildirdiği kağıt: ad, boyut ve basılamayan kenar ("A5, 148×210 mm, kenar 4,2/4,2 mm").
    /// Yazılar kayarsa nedeni görülsün (ör. yazıcı A4 basıyorsa).
    /// </summary>
    public string? PrintedPaper { get; private set; }

    /// <summary>Yazıcının bildirdiği kağıt, notasyon boyutundan (A5/A4) 5 mm'den fazla farklı mı.</summary>
    public bool PaperMismatch { get; private set; }

    /// <summary>Bu baskıda kullanılan şablonların adları (önizleme başlığı ve işlem kaydı için).</summary>
    public string TemplateNames => string.Join(", ", _pages.Select(p => p.Template.Name).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct());

    /// <summary>Önizleme başlığı: sayfa, kağıt, şablon ve (varsa) yazıcı kaydırması — kayma olursa nedeni görülsün.</summary>
    private string Caption()
    {
        var parts = new List<string> { $"{PageCount} sayfa", _pageSize };
        if (TemplateNames.Length > 0) parts.Add("şablon: " + TemplateNames);
        if (_offsetXPt != 0 || _offsetYPt != 0)
            parts.Add($"kaydırma {_offsetXPt / 72.0 * 25.4:+0.0;−0.0;0} / {_offsetYPt / 72.0 * 25.4:+0.0;−0.0;0} mm");
        parts.Add("soluk kağıt yalnız önizlemededir");
        return string.Join(" • ", parts);
    }

    /// <summary>Yazıcı seçtirip yazdırır. true = yazdırma başladı, false = iptal.</summary>
    public bool PrintWithDialog(IWin32Window owner)
    {
        using var doc = BuildDocument();
        return ChoosePrinterAndPrint(doc, owner);
    }

    /// <summary>
    /// Uygulama içi ÖNİZLEME penceresi (Win11'in native penceresi Win32 uygulamalarda önizleme
    /// göstermediği için). Üstte "🖨 Yazdır" (yazıcı seçtirir) ve "Kapat" butonları vardır.
    /// true = kullanıcı yazdırdı, false = yazdırmadan kapattı.
    /// </summary>
    public bool PrintWithPreview(IWin32Window owner)
    {
        using var doc = BuildDocument();
        // Doğrudan yazdır: önizleme ve yazıcı penceresi açılmadan hedef yazıcıya.
        if (PrintRouter.IsSilent) return ChoosePrinterAndPrint(doc, owner);
        return PreviewDialog.Show(owner, doc, PageCount, Caption(), f => ChoosePrinterAndPrint(doc, f));
    }

    /// <summary>
    /// Yazıcı seçtirir; seçilen yazıcıya göre kağıdı (A4/A5) YENİDEN bulur ve basar.
    /// Seçilen yazıcı istenen kağıdı desteklemiyorsa kullanıcıyı uyarır.
    /// </summary>
    private bool ChoosePrinterAndPrint(PrintDocument doc, IWin32Window owner)
        => PrintRouter.Print(doc, owner, (d, o) =>
        {
            // Pencerede başka yazıcı seçilmiş olabilir: kağıt, o yazıcının listesinden seçilmeli.
            var paper = FindPaper(d);
            if (paper is null)
            {
                var ans = MessageBox.Show(o,
                    $"“{d.PrinterSettings.PrinterName}” yazıcısı {_pageSize} kağıt boyutunu listelemiyor.\n\n" +
                    $"Yine de basılırsa yazılar {_pageSize} ölçüsüne göre sayfanın sol-üst köşesine yerleşir; " +
                    "hazır kağıtla hizalama kayabilir.\n\nDevam edilsin mi?",
                    "Kağıt boyutu", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (ans != DialogResult.Yes) return false;
            }
            else d.DefaultPageSettings.PaperSize = paper;
            d.DefaultPageSettings.Landscape = false;
            return true;
        });

    private PrintDocument BuildDocument()
    {
        var doc = new PrintDocument { DocumentName = $"{_t.Name} - {_t.RoundNo}. tur notasyon" };
        doc.DefaultPageSettings.PaperSize = FindPaper(doc) ?? doc.DefaultPageSettings.PaperSize;
        doc.DefaultPageSettings.Landscape = false;
        doc.OriginAtMargins = false;

        // Arka planı her geçişte (önizleme + baskı) yeniden yükle/temizle ki çok geçiş bozulmasın.
        doc.BeginPrint += (s, _) =>
        {
            _pageIndex = 0;
            // Önizlemede kağıt görseli her zaman (soluk) gösterilir ki yazıların kutulara oturduğu
            // görülsün; gerçek baskıda yalnız şablonda "arka planı da bas" açıksa çizilir.
            _preview = (s as PrintDocument)?.PrintController?.IsPreview == true;
        };
        doc.EndPrint += (_, _) =>
        {
            foreach (var img in _bgs.Values) img?.Dispose();
            _bgs.Clear();
            _pageIndex = 0;
        };
        doc.PrintPage += OnPrintPage;
        return doc;
    }

    private void OnPrintPage(object? sender, PrintPageEventArgs e)
    {
        var g = e.Graphics!;
        g.PageUnit = GraphicsUnit.Point;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

        // Gerçek yazıcıda (0,0) yazdırılabilir alanın köşesidir → sabit kenar boşluğunu telafi et
        // ki (0,0) fiziksel kağıdın sol-üstü olsun. ÖNİZLEMEDE (0,0) zaten kağıdın köşesi; orada
        // da kaydırılırsa önizleme sola-yukarı kayık ve kesik görünüyordu.
        bool preview = (sender as PrintDocument)?.PrintController?.IsPreview == true;
        float hx = preview ? 0 : e.PageSettings.HardMarginX / 100f * 72f;
        float hy = preview ? 0 : e.PageSettings.HardMarginY / 100f * 72f;
        g.TranslateTransform(-hx, -hy);
        if (!preview && _pageIndex == 0)
        {
            // Sayfa ölçüsü yüzde bir inç → mm
            double wmm = e.PageBounds.Width * 0.254, hmm = e.PageBounds.Height * 0.254;
            var (pw, ph) = NotasyonOtomasyonu.Core.PageGeometry.Points(_pageSize);
            double ew = pw / 72.0 * 25.4, eh = ph / 72.0 * 25.4;
            PaperMismatch = Math.Abs(wmm - ew) > 5 || Math.Abs(hmm - eh) > 5;
            PrintedPaper = $"{e.PageSettings.PaperSize.PaperName}, {wmm:0}×{hmm:0} mm, kenar {hx / 72 * 25.4:0.0}/{hy / 72 * 25.4:0.0} mm";
        }

        if (_pageIndex < _pages.Count)
        {
            var page = _pages[_pageIndex];
            var tpl = page.Template;
            var sheets = OverlayLayout.SheetRects(tpl.PerPage is 1 or 2 ? tpl.PerPage : 1, _pageSize);
            var bg = tpl.PrintBackground || _preview ? Background(tpl) : null;

            for (int i = 0; i < page.Pairings.Count && i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                if (bg is not null) DrawBackground(g, bg, sheet, ghost: _preview && !tpl.PrintBackground);

                // Yazıcı kaydırma düzeltmesi yalnızca yazılara uygulanır (arka plan = kağıdın kendisi).
                var state = g.Save();
                g.TranslateTransform(_offsetXPt, _offsetYPt);
                foreach (var placed in OverlayLayout.Place(tpl, _t, page.Pairings[i], sheet))
                    DrawPlaced(g, placed);
                g.Restore(state);
            }
        }

        _pageIndex++;
        e.HasMorePages = _pageIndex < _pages.Count;
    }

    private static void DrawBackground(Graphics g, Image bg, RectangleF sheet, bool ghost)
    {
        if (!ghost) { g.DrawImage(bg, sheet.X, sheet.Y, sheet.Width, sheet.Height); return; }
        using var attrs = new System.Drawing.Imaging.ImageAttributes();
        attrs.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.35f }); // %35 opak
        var dest = new[] { new PointF(sheet.X, sheet.Y), new PointF(sheet.Right, sheet.Y), new PointF(sheet.X, sheet.Bottom) };
        g.DrawImage(bg, dest, new RectangleF(0, 0, bg.Width, bg.Height), GraphicsUnit.Pixel, attrs);
    }

    internal static void DrawPlaced(Graphics g, PlacedText p)
    {
        if (string.IsNullOrEmpty(p.Text)) return;
        using var font = TextFitter.CreateFont(p.FontPt, p.Bold);
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit,
            Trimming = StringTrimming.None,
            LineAlignment = StringAlignment.Center,
            Alignment = p.Align switch
            {
                HAlign.Center => StringAlignment.Center,
                HAlign.Right => StringAlignment.Far,
                _ => StringAlignment.Near
            }
        };
        g.DrawString(p.Text, font, Brushes.Black, p.RectPt, fmt);
    }

    /// <summary>Şablonun kağıt görseli (geçiş boyunca bir kez yüklenir; yoksa null).</summary>
    private Image? Background(OverlayTemplate tpl)
    {
        var path = tpl.BackgroundImagePath;
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (_bgs.TryGetValue(path, out var cached)) return cached;
        Image? img = null;
        try { if (File.Exists(path)) img = Image.FromFile(path); } catch { img = null; }
        // Önizlemede soluk kağıt ekran çözünürlüğüne küçültülür: her sayfada tam boy görseli yeniden
        // ölçeklemek "Generating preview" süresini uzatıyordu.
        if (_preview && img is not null && img.Width > 1400)
        {
            var small = new Bitmap(1400, (int)(img.Height * 1400.0 / img.Width));
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(img, 0, 0, small.Width, small.Height);
            }
            img.Dispose();
            img = small;
        }
        _bgs[path] = img;
        return img;
    }

    private PaperSize? FindPaper(PrintDocument doc)
    {
        var want = NotasyonOtomasyonu.Core.PageGeometry.IsA5(_pageSize) ? PaperKind.A5 : PaperKind.A4;
        try
        {
            foreach (PaperSize ps in doc.PrinterSettings.PaperSizes)
                if (ps.Kind == want) return ps;
        }
        catch (InvalidPrinterException) { /* yazıcı yok/erişilemiyor */ }
        return null;
    }
}
