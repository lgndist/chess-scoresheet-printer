namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Bir overlay alanına hangi verinin yazılacağı. Değerler config.json'a sayı olarak yazıldığından
/// sıra değiştirilmemeli. Location/TimeControl/Arbiter artık kullanılmıyor (her zaman boş basılır);
/// eski şablonlar bozulmasın diye yerlerinde duruyorlar.
/// </summary>
public enum FieldKind
{
    FreeText,        // sabit metin (StaticText)
    TournamentName,
    Category,
    Location,        // kullanılmıyor
    Date,
    RoundNo,
    TimeControl,     // kullanılmıyor
    Arbiter,         // kullanılmıyor
    BoardNo,
    WhiteName,
    WhiteTitle,
    WhiteRating,
    WhiteStartNo,
    BlackName,
    BlackTitle,
    BlackRating,
    BlackStartNo,
    WhiteTeam,       // takım turnuvası: beyazın takımı (bireyselde boş)
    BlackTeam,       // takım turnuvası: siyahın takımı (bireyselde boş)
    // Kağıtta ". . . / . . . / 20 . . ." kılavuzlu tarih kutusu için üç parça:
    DateDay,         // "03"
    DateMonth,       // "10"
    DateYear2,       // yıl, 4 hane ("2026"); adı eski kayıtlarla uyum için korunur
    WhiteProvince,   // beyazın ili (TSF kayıt listesinden; "İl / City" kutusu)
    BlackProvince    // siyahın ili
}

public static class FieldKinds
{
    /// <summary>Kağıda artık basılmayan (her zaman boş kalan) alan türleri.</summary>
    public static bool IsRetired(FieldKind k) => k is FieldKind.Location or FieldKind.TimeControl or FieldKind.Arbiter;
}

public enum HAlign { Left, Center, Right }

/// <summary>Uzun metin kutuya sığmazsa ne yapılacağı.</summary>
public enum OverflowMode
{
    Shrink,          // sadece fontu küçült
    ShrinkThenEllipsis, // küçült, yine sığmazsa "…" ile kes
    ShrinkThenAbbreviate // küçült, yine sığmazsa adları kısalt (A. Soyad)
}

/// <summary>
/// Hazır (önceden basılı) notasyon kağıdı üzerine yazılacak tek bir alan/kutu.
/// Koordinatlar bir KAĞIDA göre normalize (0..1); böylece çözünürlükten bağımsızdır.
/// </summary>
public sealed class OverlayField
{
    public FieldKind Kind { get; set; } = FieldKind.FreeText;
    public string? StaticText { get; set; }

    // Kutunun kağıt içindeki konumu/boyutu (0..1)
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 0.25;
    public double H { get; set; } = 0.04;

    /// <summary>İstenen punto (sığmazsa küçülür).</summary>
    public double FontSize { get; set; } = 11;
    public bool Bold { get; set; }
    public HAlign Align { get; set; } = HAlign.Left;
    public OverflowMode Overflow { get; set; } = OverflowMode.ShrinkThenAbbreviate;
    /// <summary>En küçük punto (bunun altına inilmez).</summary>
    public double MinFontSize { get; set; } = 6;

    public OverlayField Clone() => (OverlayField)MemberwiseClone();
}

/// <summary>
/// Bir hazır notasyon kağıdı şablonu: arka plan görseli (hizalama referansı / opsiyonel baskı),
/// sayfa başına kağıt sayısı ve alan kutuları.
/// </summary>
public sealed class OverlayTemplate
{
    /// <summary>Şablon adı (kütüphanede ayırt etmek için). Örn: "Ana Örnek".</summary>
    public string Name { get; set; } = "Şablon";

    /// <summary>Boş notasyon kağıdının taranmış görseli (PNG/JPG). Tasarımda referans, baskıda opsiyonel.</summary>
    public string? BackgroundImagePath { get; set; }

    /// <summary>Baskıda arka planı da çiz (düz kağıda test için). Hazır kağıda basarken kapalı tutun.</summary>
    public bool PrintBackground { get; set; }

    /// <summary>A4'e kaç kağıt: 1 veya 2 (kağıt KAĞIT bölgesine göre dikey dizilir).</summary>
    public int PerPage { get; set; } = 1;

    public List<OverlayField> Fields { get; set; } = new();

    /// <summary>Tanımlı en az bir alan var mı? (overlay modunun anlamlı olması için)</summary>
    public bool IsConfigured => Fields.Count > 0;

    public OverlayTemplate DeepClone() => new()
    {
        Name = Name,
        BackgroundImagePath = BackgroundImagePath,
        PrintBackground = PrintBackground,
        PerPage = PerPage,
        Fields = Fields.Select(f => f.Clone()).ToList()
    };
}
