using System.Drawing.Printing;
using System.Text;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// İşlem kaydı penceresindeki "Baskı ayrıntıları": notasyon ayarları (kağıt, kaydırma, şablonlar),
/// yazıcı ayarları ve yazıcının kendisinden okunanlar (varsayılan kağıt, A5'i tanıyor mu, basılamayan
/// kenar) ile son baskının yazıcıdan dönen ölçüleri ve yazıların kağıttaki yeri. Kayma sorunu buradan
/// çözülür ya da kopyalanıp gönderilir.
/// </summary>
public partial class MainForm
{
    private static string Mm(double hundredthsInch) => (hundredthsInch * 0.254).ToString("0.0");

    private string PrintDiagnostics()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Chess Scoresheet Printer v{NotasyonOtomasyonu.App.Update.Updater.Current} • {DateTime.Now:dd.MM.yyyy HH:mm}");
        sb.AppendLine($"Ayar klasörü: {AppPaths.DataDir}");
        sb.AppendLine();

        var l = _config.Layout;
        sb.AppendLine("— NOTASYON AYARLARI —");
        sb.AppendLine($"Kağıt boyutu: {l.PageSize}   Yazıcı kaydırması: sağa {l.PrintOffsetXmm:+0.0;−0.0;0} mm, aşağı {l.PrintOffsetYmm:+0.0;−0.0;0} mm   Nüsha: {l.CopiesPerBoard}");
        var o = _config.Overlay;
        sb.AppendLine($"Etkin şablon: {o.Name} ({o.Fields.Count} alan, kağıda {(o.PerPage is 2 ? 2 : 1)} notasyon, arka plan {(o.PrintBackground ? "basılır" : "basılmaz")})");
        foreach (var (sys, label) in new[] { (TournamentSystem.Swiss, "İsviçre"), (TournamentSystem.RoundRobin, "Berger"), (TournamentSystem.TeamSwiss, "Takım") })
        {
            var t = _config.TemplateFor(sys);
            sb.AppendLine($"  {label,-8}: {t.Name}{(ReferenceEquals(t, o) ? " (etkin)" : "")}, kağıda {(t.PerPage is 2 ? 2 : 1)} notasyon");
        }
        sb.AppendLine();

        var pc = _config.Printing;
        var target = Overlay.PrintRouter.TargetName;
        sb.AppendLine("— YAZICI —");
        sb.AppendLine($"Hedef: {target}{(string.IsNullOrWhiteSpace(pc.PrinterName) ? " (Windows varsayılanı)" : "")}");
        sb.AppendLine($"Tepsi: {pc.PaperSource ?? "otomatik"}   A5 tepsisi: {pc.PaperSourceA5 ?? "aynı"}   Kopya: {pc.Copies}   Doğrudan yazdır: {(pc.Silent ? "açık" : "kapalı")}   " +
                      $"Kayıtlı yazıcı tercihleri: {(pc.DevModes.ContainsKey(target) ? "var (her baskıda uygulanır)" : "yok")}");
        try
        {
            var ps = new PrinterSettings { PrinterName = target };
            if (!ps.IsValid) sb.AppendLine("⚠ Yazıcı bulunamadı ya da erişilemiyor.");
            else
            {
                var def = ps.DefaultPageSettings;
                sb.AppendLine($"Yazıcının varsayılan kağıdı: {def.PaperSize.PaperName} ({Mm(def.PaperSize.Width)}×{Mm(def.PaperSize.Height)} mm), {(def.Landscape ? "yatay" : "dikey")}, tepsi: {def.PaperSource.SourceName}");
                var want = PageGeometry.IsA5(l.PageSize) ? PaperKind.A5 : PaperKind.A4;
                var paper = ps.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == want);
                if (paper is null) sb.AppendLine($"⚠ Yazıcı {l.PageSize} kağıdını listelemiyor: notasyon yanlış boyutta basılabilir.");
                else
                {
                    var pg = (PageSettings)def.Clone();
                    pg.PaperSize = paper;
                    pg.Landscape = false;
                    sb.AppendLine($"{l.PageSize} kağıdı: \"{paper.PaperName}\" ({Mm(paper.Width)}×{Mm(paper.Height)} mm); basılamayan kenar sol {pg.HardMarginX * 0.254:0.0} mm, üst {pg.HardMarginY * 0.254:0.0} mm");
                }
            }
        }
        catch (Exception ex) { sb.AppendLine("Yazıcı okunamadı: " + ex.Message); }
        sb.AppendLine();

        sb.AppendLine("— SON NOTASYON BASKISI —");
        sb.AppendLine(Overlay.SheetPrinter.LastPrintReport ?? "Bu açılışta henüz notasyon basılmadı. (Ayarlar → Notasyon → Hizalama testi de buraya yazar.)");
        sb.AppendLine();

        sb.AppendLine("— NASIL OKUNUR —");
        sb.AppendLine("• Yukarıda bir yazının kağıdın solundan ve üstünden kaç mm'de olması gerektiği yazar. Kağıtta cetvelle ölçün;");
        sb.AppendLine("  fark gereken kaydırmadır (sağa kayıksa eksi, sola kayıksa artı) → Ayarlar → Notasyon → Yazıcı hizalama.");
        sb.AppendLine("• Bütün yazılar aynı miktarda kayıksa: kağıt tepside ortalanmamış (kılavuzları A5'e yaslayın) ya da yazıcı kaydırıyor.");
        sb.AppendLine("• Kayma kağıdın sağına/altına doğru artıyorsa: yazıcı A5 yerine başka boyutta basıyor (yukarıdaki \"yazıcının bildirdiği sayfa\").");
        sb.AppendLine("• Önizlemede yazılar soluk kağıttaki kutularda değilse sorun şablondadır (Ayarlar → Şablonlar).");
        return sb.ToString();
    }
}
