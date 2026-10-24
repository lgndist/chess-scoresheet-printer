using System.Drawing.Printing;
using System.Reflection;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// Gerçek yazıcı sürücüsüyle (Microsoft Print to PDF, dosyaya) notasyon basıp "Baskı ayrıntıları"
/// raporunun yazıcıdan dönen kağıdı ve yazıların yerini doğru yazdığını sınar. Yazıcı yoksa geçer.
/// </summary>
public class PrintReportTests
{
    private const string Pdf = "Microsoft Print to PDF";

    private static OverlayTemplate Template() => new()
    {
        Name = "Deneme",
        Fields = { new OverlayField { Kind = FieldKind.WhiteName, X = 0.2, Y = 0.3, W = 0.4, H = 0.04 } }
    };

    private static (Tournament, Pairing) Sample()
    {
        var p = new Pairing(1, new Player(1, "BEYAZ OYUNCU"), new Player(2, "SİYAH OYUNCU"));
        return (new Tournament("Deneme", 1, new List<Pairing> { p }), p);
    }

    [Theory]
    [InlineData("A5", false)]
    [InlineData("A4", false)]
    public void RealPrint_ReportsPaperAndTextPosition(string size, bool mismatch)
    {
        if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(Pdf)) return;
        var (t, _) = Sample();
        var printer = new SheetPrinter(Template(), t, size, offsetXmm: -20, offsetYmm: 0);
        var doc = (PrintDocument)typeof(SheetPrinter).GetMethod("BuildDocument", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(printer, null)!;
        var file = Path.Combine(Path.GetTempPath(), $"csp-rapor-{Guid.NewGuid():N}.pdf");
        try
        {
            doc.PrinterSettings.PrinterName = Pdf;
            doc.PrinterSettings.PrintToFile = true;
            doc.PrinterSettings.PrintFileName = file;
            var want = size == "A5" ? PaperKind.A5 : PaperKind.A4;
            doc.DefaultPageSettings.PaperSize = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().First(p => p.Kind == want);
            doc.PrintController = new StandardPrintController();
            doc.Print();

            var report = SheetPrinter.LastPrintReport!;
            Assert.Contains(Pdf, report);
            Assert.Equal(mismatch, printer.PaperMismatch);
            Assert.Contains(size == "A5" ? "148" : "210", report);
            Assert.Contains("BEYAZ OYUNCU", report);
            // Yazının başladığı yer: X = 0,2 × kağıt genişliği − 20 mm kaydırma (+ kutu çizgisine yapışmasın diye ~0,7 mm iç boşluk)
            double expect = (size == "A5" ? 148 : 210) * 0.2 - 20;
            var m = System.Text.RegularExpressions.Regex.Match(report, @"kutu soldan (-?[\d,]+) mm");
            Assert.True(m.Success, report);
            double got = double.Parse(m.Groups[1].Value, new System.Globalization.CultureInfo("tr-TR"));
            Assert.InRange(got, expect, expect + 1.0);
        }
        finally { try { File.Delete(file); } catch { } }
    }
}
