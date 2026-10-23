using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Yerleşik hazır kağıt şablonları ("Ana Örnek" notasyon kağıdı). Görsel gömülü kaynaktan exe
/// yanına çıkarılır; böylece başka bilgisayarda da çalışır.
///  • <see cref="DefaultName"/> ("Ana Örnek 2"): sahada ayarlanmış güncel yerleşim — VARSAYILAN.
///  • <see cref="LegacyName"/> ("Ana Örnek"): v1.0/v1.1 yerleşimi, ikinci seçenek olarak durur.
/// İkisinde de takım turnuvası için "Kulüp" kutularına takım adı alanları vardır (bireyselde boş).
/// </summary>
public static class OverlayDefaults
{
    public const string DefaultName = "Ana Örnek 2";
    public const string LegacyName = "Ana Örnek";
    public const string TsfName = "TSF Resmi Form (ortalı)";
    private const string ResourceName = "NotasyonOtomasyonu.App.AnaOrnek.png";

    /// <summary>Bu sürümün şablon seti; config'teki değer küçükse eksik yerleşik şablonlar bir kez eklenir.</summary>
    public const int CurrentTemplateSetVersion = 5;

    /// <summary>
    /// Açılışta çağrılır. Kütüphane boşsa iki yerleşik şablonu ekler ("Ana Örnek 2" etkin).
    /// Eski config'e (şablon seti &lt; 2) yeni varsayılanı bir kez ekleyip etkin yapar ve nüshayı 4'e
    /// çeker; kullanıcının kendi şablonlarına dokunmaz. Arka plan dosyası kaybolmuşsa yeniden bağlar.
    /// </summary>
    public static void EnsureDefaults(AppConfig cfg, string baseDir)
    {
        var bgPath = ExtractBackground(baseDir);

        if (cfg.Templates.Count == 0)
        {
            if (cfg.Overlay.IsConfigured)
            {
                if (string.IsNullOrWhiteSpace(cfg.Overlay.Name)) cfg.Overlay.Name = "Şablonum";
                cfg.Templates.Add(cfg.Overlay.DeepClone());
            }
            else cfg.Templates.Add(BuildLegacy(bgPath));
        }

        if (cfg.TemplateSetVersion < 2)
        {
            var def = cfg.Templates.FirstOrDefault(t => t.Name == DefaultName);
            if (def is null)
            {
                def = BuildDefault(bgPath);
                cfg.Templates.Insert(0, def);
            }
            cfg.Overlay = def.DeepClone();               // yeni yerleşim varsayılan
            cfg.Layout.CopiesPerBoard = LayoutConfig.DefaultCopies;
        }
        if (cfg.TemplateSetVersion < 3 && cfg.Templates.All(t => t.Name != TsfName))
            cfg.Templates.Add(BuildTsf(bgPath));         // 3. seçenek; etkin şablonu değiştirmez
        if (cfg.TemplateSetVersion < 5)
            foreach (var t in cfg.Templates.Append(cfg.Overlay))
                UseFullDate(t, bgPath);                  // TSF formu: gün/ay/yıl(2) parçaları yerine tam tarih
        if (cfg.TemplateSetVersion < 4)
            foreach (var t in cfg.Templates.Append(cfg.Overlay))
                AddProvinceFields(t, bgPath);            // yerleşik şablonlara "İl / City" kutuları
        if (cfg.TemplateSetVersion < CurrentTemplateSetVersion)
            cfg.TemplateSetVersion = CurrentTemplateSetVersion;

        // Yerleşik şablonların arka plan yolu kırıksa (taşınmış exe) gömülü görseli geri bağla.
        if (bgPath is not null)
        {
            foreach (var t in cfg.Templates.Append(cfg.Overlay))
                if (t.Name is DefaultName or LegacyName or TsfName &&
                    (string.IsNullOrWhiteSpace(t.BackgroundImagePath) || !File.Exists(t.BackgroundImagePath)))
                    t.BackgroundImagePath = bgPath;
        }
    }

    /// <summary>
    /// Yerleşik bir şablonda il alanları yoksa, o şablonun güncel yerleşimindeki il alanlarını ekler.
    /// Kullanıcının kendi şablonlarına dokunmaz (tasarımcıdan "Beyaz il / Siyah il" eklenebilir).
    /// </summary>
    public static void AddProvinceFields(OverlayTemplate t, string? backgroundPath)
    {
        if (t.Fields.Any(f => f.Kind is FieldKind.WhiteProvince or FieldKind.BlackProvince)) return;
        var fresh = BuildBuiltIn(t.Name ?? "", backgroundPath);
        if (fresh is null) return;
        t.Fields.AddRange(fresh.Fields.Where(f => f.Kind is FieldKind.WhiteProvince or FieldKind.BlackProvince));
    }

    /// <summary>
    /// TSF formundaki üç parçalı tarihi (gün / ay / yılın son iki hanesi) tek tam tarih alanıyla
    /// değiştirir: çıktıda yıl her zaman 4 hane ("03.10.2026"). Yalnız yerleşik TSF şablonu.
    /// </summary>
    public static void UseFullDate(OverlayTemplate t, string? backgroundPath)
    {
        if (t.Name != TsfName || !t.Fields.Any(f => f.Kind is FieldKind.DateDay or FieldKind.DateMonth or FieldKind.DateYear2)) return;
        var date = BuildTsf(backgroundPath).Fields.First(f => f.Kind == FieldKind.Date);
        t.Fields.RemoveAll(f => f.Kind is FieldKind.DateDay or FieldKind.DateMonth or FieldKind.DateYear2);
        if (!t.Fields.Any(f => f.Kind == FieldKind.Date)) t.Fields.Add(date);
    }

    /// <summary>Gömülü Ana Örnek görselini exe yanındaki templates klasörüne çıkarır.</summary>
    public static string? ExtractBackground(string baseDir)
    {
        try
        {
            var dir = Path.Combine(baseDir, "templates");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "AnaOrnek.png");

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream(ResourceName);
            if (s is null) return File.Exists(path) ? path : null;

            // Boyut farklıysa (güncel sürüm) yeniden yaz; aynıysa dokunma.
            if (!File.Exists(path) || new FileInfo(path).Length != s.Length)
            {
                using var fs = File.Create(path);
                s.CopyTo(fs);
            }
            return path;
        }
        catch { return null; }
    }

    /// <summary>Varsayılan şablonun yeni bir kopyası ("Ana Örnek 2", sıfırlama için).</summary>
    public static OverlayTemplate BuildDefault(string? backgroundPath) => new()
    {
        Name = DefaultName,
        BackgroundImagePath = backgroundPath,
        PrintBackground = false, // hazır kağıda basılır, arka plan çizilmez
        PerPage = 1,
        Fields = DefaultFields()
    };

    /// <summary>Eski yerleşimin yeni bir kopyası ("Ana Örnek").</summary>
    public static OverlayTemplate BuildLegacy(string? backgroundPath) => new()
    {
        Name = LegacyName,
        BackgroundImagePath = backgroundPath,
        PrintBackground = false,
        PerPage = 1,
        Fields = LegacyFields()
    };

    /// <summary>TSF resmi formuna fotoğraftan ölçülerek ortalanmış yerleşim.</summary>
    public static OverlayTemplate BuildTsf(string? backgroundPath) => new()
    {
        Name = TsfName,
        BackgroundImagePath = backgroundPath,
        PrintBackground = false,
        PerPage = 1,
        Fields = TsfFields()
    };

    /// <summary>Adına göre yerleşik şablonu yeniden kurar (sıfırlama); yerleşik değilse null.</summary>
    public static OverlayTemplate? BuildBuiltIn(string name, string? backgroundPath) => name switch
    {
        DefaultName => BuildDefault(backgroundPath),
        LegacyName => BuildLegacy(backgroundPath),
        TsfName => BuildTsf(backgroundPath),
        _ => null
    };

    /// <summary>
    /// Türkiye Satranç Federasyonu resmi notasyon formu (mavi başlıklı). Değerler, bu formun basılmış
    /// iki fotoğrafından ölçüldü: hamle tablosuna göre perspektif düzeltildi, basılı yazıların bilinen
    /// konumlarından kağıda eşlendi (hata ≈0,25 pt) ve her alan kendi kutusunun içine ORTALANDI.
    /// Tarih tek alanda tam yazılır ("03.10.2026", 4 haneli yıl); noktalı kılavuzun üstünde ortalı.
    /// Not: arka plan görseli (önizleme) bu formun değil Ana Örnek'in; kutular birkaç pt farklı görünebilir.
    /// </summary>
    private static List<OverlayField> TsfFields() => new()
    {
        C(FieldKind.TournamentName, 0.517488, 0.123677, 0.415410, 0.028621, 11, false),
        C(FieldKind.Category,       0.445174, 0.157994, 0.171090, 0.029258, 12, true),
        C(FieldKind.RoundNo,        0.683297, 0.158238, 0.124953, 0.028945, 12, true),
        C(FieldKind.BoardNo,        0.857484, 0.158913, 0.075257, 0.028601, 20, true),
        C(FieldKind.WhiteName,      0.167772, 0.193770, 0.448068, 0.029088, 12, false),
        C(FieldKind.WhiteRating,    0.682681, 0.193442, 0.123867, 0.028852, 12, false),
        C(FieldKind.WhiteTeam,      0.167725, 0.224875, 0.447564, 0.029168, 9, false, OverflowMode.ShrinkThenEllipsis),
        C(FieldKind.BlackName,      0.165095, 0.261425, 0.449754, 0.029095, 12, false),
        C(FieldKind.BlackRating,    0.681652, 0.260524, 0.124281, 0.028843, 12, false),
        C(FieldKind.BlackTeam,      0.165442, 0.292536, 0.448798, 0.028053, 9, false, OverflowMode.ShrinkThenEllipsis),
        // "İl / City" kutuları (Rating kutusunun altında)
        C(FieldKind.WhiteProvince,  0.682681, 0.224875, 0.123867, 0.029168, 10, false, OverflowMode.ShrinkThenEllipsis),
        C(FieldKind.BlackProvince,  0.681652, 0.292536, 0.124281, 0.028053, 10, false, OverflowMode.ShrinkThenEllipsis),
        // Tarih: tek alanda tam ve 4 haneli yıl ("03.10.2026"), noktalı kılavuzun üstünde ortalı
        C(FieldKind.Date,           0.154730, 0.157208, 0.184459, 0.020159, 10, false, OverflowMode.ShrinkThenEllipsis),
    };

    private static OverlayField C(FieldKind kind, double x, double y, double w, double h,
                                  double font, bool bold, OverflowMode overflow = OverflowMode.ShrinkThenAbbreviate)
    {
        var f = F(kind, x, y, w, h, font, bold, overflow);
        f.Align = HAlign.Center;
        return f;
    }

    /// <summary>
    /// Sahada (Isparta, Ekim 2026) hazır kağıda göre ayarlanmış yerleşim: yazılar kutuların
    /// etiket sütunundan biraz içeride başlar. Normalize (0..1) koordinatlar.
    /// </summary>
    private static List<OverlayField> DefaultFields() => new()
    {
        F(FieldKind.TournamentName, 0.5333333, 0.1220736, 0.4095238, 0.0267559, 11, false),
        F(FieldKind.Date,           0.1761905, 0.1588629, 0.1714286, 0.0234114, 11, false),
        F(FieldKind.Category,       0.4547619, 0.1605351, 0.1547619, 0.0301003, 11, true),
        F(FieldKind.RoundNo,        0.7071429, 0.1588629, 0.1000000, 0.0301003, 11, true),
        F(FieldKind.BoardNo,        0.8833333, 0.1571906, 0.0571429, 0.0284281, 20, true),
        F(FieldKind.WhiteName,      0.1880952, 0.1939799, 0.4190476, 0.0284281, 11, false),
        F(FieldKind.WhiteRating,    0.7000000, 0.1939799, 0.1071429, 0.0301003, 11, false),
        F(FieldKind.BlackName,      0.1952381, 0.2608696, 0.4142857, 0.0317726, 11, false),
        F(FieldKind.BlackRating,    0.7047619, 0.2642140, 0.1023810, 0.0317726, 11, false),
        // Takım turnuvası: "Kulüp / Club" kutularına takım adı (bireyselde boş kalır)
        F(FieldKind.WhiteTeam,      0.1880952, 0.2250000, 0.4190476, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
        F(FieldKind.BlackTeam,      0.1952381, 0.2950000, 0.4142857, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
        // İl: hücrenin tamamı (sol çizgiden 0,8 mm içeride, Rating ile aynı sağ kenar), ortalı — A5 analizine göre
        C(FieldKind.WhiteProvince,  0.6844595, 0.2250000, 0.1226834, 0.0284281, 10, false, OverflowMode.ShrinkThenEllipsis),
        C(FieldKind.BlackProvince,  0.6844595, 0.2950000, 0.1226834, 0.0284281, 10, false, OverflowMode.ShrinkThenEllipsis),
    };

    /// <summary>v1.0/v1.1 yerleşimi (kullanıcının ilk onayladığı).</summary>
    private static List<OverlayField> LegacyFields() => new()
    {
        F(FieldKind.TournamentName, 0.5047619, 0.1187291, 0.4380952, 0.0284281, 11, false),
        F(FieldKind.Date,           0.1500000, 0.1538462, 0.1952381, 0.0284281, 11, false),
        F(FieldKind.Category,       0.4333333, 0.1555184, 0.1761905, 0.0284281, 11, true),
        F(FieldKind.RoundNo,        0.6809524, 0.1555184, 0.1238095, 0.0284281, 11, true),
        F(FieldKind.BoardNo,        0.8619048, 0.1538462, 0.0761905, 0.0301003, 20, true),
        F(FieldKind.WhiteName,      0.1500000, 0.1923077, 0.4571429, 0.0284281, 11, false),
        F(FieldKind.WhiteRating,    0.6809524, 0.1906355, 0.1285714, 0.0284281, 11, false),
        F(FieldKind.BlackName,      0.1500000, 0.2625418, 0.4595238, 0.0301003, 11, false),
        F(FieldKind.BlackRating,    0.6785714, 0.2608696, 0.1309524, 0.0317726, 11, false),
        F(FieldKind.WhiteTeam,      0.1500000, 0.2250000, 0.4571429, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
        F(FieldKind.BlackTeam,      0.1500000, 0.2950000, 0.4595238, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
        F(FieldKind.WhiteProvince,  0.6809524, 0.2250000, 0.1285714, 0.0284281, 10, false, OverflowMode.ShrinkThenEllipsis),
        F(FieldKind.BlackProvince,  0.6785714, 0.2950000, 0.1309524, 0.0284281, 10, false, OverflowMode.ShrinkThenEllipsis),
    };

    private static OverlayField F(FieldKind kind, double x, double y, double w, double h,
                                  double font, bool bold, OverflowMode overflow = OverflowMode.ShrinkThenAbbreviate) => new()
    {
        Kind = kind, X = x, Y = y, W = w, H = h,
        FontSize = font, Bold = bold, Align = HAlign.Left,
        Overflow = overflow, MinFontSize = 6
    };
}
