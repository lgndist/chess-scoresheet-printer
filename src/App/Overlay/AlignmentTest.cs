using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Hizalama testi: seçilen şablonla örnek verili tek kağıt basar (kutu hizasını ve kaydırma ayarını
/// gerçek kağıtta görmek için). Ayarlar'dan ve şablon listesinden aynı yolla çağrılır.
/// </summary>
internal static class AlignmentTest
{
    public static void Print(IWin32Window owner, OverlayTemplate tpl, string pageSize, double offsetXmm, double offsetYmm)
    {
        if (!tpl.IsConfigured)
        {
            MessageBox.Show(owner, "Bu şablonda henüz alan yok; önce şablonu düzenleyin.", "Şablon boş",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (t, p) = OverlayLayout.Sample();
        t = t with { Pairings = new[] { p } };
        new SheetPrinter(tpl, t, pageSize, offsetXmm, offsetYmm).PrintWithPreview(owner);
    }

    /// <summary>Şablonun nerede kullanıldığı: "etkin", "Berger", "takım" (listelerde gösterilir).</summary>
    public static string Uses(AppConfig cfg, OverlayTemplate t)
    {
        var uses = new List<string>();
        if (t.Name == cfg.Overlay.Name) uses.Add("etkin");
        foreach (var (key, label) in new[] { ("Swiss", "İsviçre"), ("RoundRobin", "Berger"), ("Team", "takım") })
            if (cfg.TemplateBySystem.TryGetValue(key, out var n) && string.Equals(n, t.Name, StringComparison.OrdinalIgnoreCase))
                uses.Add(label);
        return uses.Count == 0 ? "" : $"  ({string.Join(" • ", uses)})";
    }
}
