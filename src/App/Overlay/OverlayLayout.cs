using System.Drawing;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>Sayfaya yerleştirilmiş, çizime hazır tek bir metin.</summary>
public readonly record struct PlacedText(RectangleF RectPt, string Text, float FontPt, bool Bold, HAlign Align);

/// <summary>
/// Overlay şablonunu somut bir eşleştirme için PUNTO cinsinden yerleşime çevirir.
/// Hem PDF (QuestPDF) hem doğrudan yazdırma (GDI) bu hesabı kullanır → çıktı aynı olur.
/// </summary>
public static class OverlayLayout
{
    /// <summary>Alan kutusunun sol/sağ kenarından yazıya kadar boşluk (punto ≈ 0,7 mm).</summary>
    public const float TextInsetPt = 2f;

    /// <summary>Sayfadaki kağıt bölgeleri (perPage'e göre dikey bölünür), sayfa boyutuna göre.</summary>
    public static List<RectangleF> SheetRects(int perPage, string pageSize = "A4")
    {
        var (w, h) = PageGeometry.Points(pageSize);
        if (perPage == 2)
            return new()
            {
                new RectangleF(0, 0, w, h / 2f),
                new RectangleF(0, h / 2f, w, h / 2f),
            };
        return new() { new RectangleF(0, 0, w, h) };
    }

    /// <summary>Bir kağıt bölgesi için tüm alanları yerleştirir.</summary>
    public static List<PlacedText> Place(OverlayTemplate tpl, Tournament t, Pairing p, RectangleF sheet)
    {
        var list = new List<PlacedText>(tpl.Fields.Count);
        foreach (var f in tpl.Fields)
        {
            var rect = new RectangleF(
                sheet.X + (float)f.X * sheet.Width,
                sheet.Y + (float)f.Y * sheet.Height,
                (float)f.W * sheet.Width,
                (float)f.H * sheet.Height);

            // Yatayda küçük iç boşluk: yazı, hazır kağıttaki kutu çizgisine yapışmasın.
            var inner = RectangleF.Inflate(rect, -Math.Min(TextInsetPt, rect.Width / 4f), 0);

            var value = Resolve(f, t, p);
            var fit = FitField(f, value, inner);

            list.Add(new PlacedText(inner, fit.Text, fit.FontPt, f.Bold, f.Align));
        }
        return list;
    }

    /// <summary>
    /// Alanı kutuya sığdırır. Kişi adlarındaki baş harf kısaltması ("M. A. YILMAZ") turnuva adına ve
    /// ile uygulanmaz: turnuva adında önce genel sözcükler ("Satranç", "Turnuvası") atılır; il
    /// sığmazsa yaygın kısa adı ("AFYON", "K.MARAŞ") basılır. Hiçbiri sığmazsa "…" ile kısaltılır.
    /// </summary>
    public static FitResult FitField(OverlayField f, string value, RectangleF inner)
    {
        IEnumerable<string> candidates = f.Kind switch
        {
            FieldKind.TournamentName => TitleLadder(value),
            FieldKind.WhiteProvince or FieldKind.BlackProvince => new[] { value, Provinces.ShortForm(value) },
            _ => Array.Empty<string>()
        };
        var list = candidates.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
        if (list.Count == 0)
            return TextFitter.Fit(value, inner.Width, inner.Height, (float)f.FontSize, (float)f.MinFontSize, f.Bold, f.Overflow);

        foreach (var c in list)
        {
            var r = TextFitter.Fit(c, inner.Width, inner.Height, (float)f.FontSize, (float)f.MinFontSize, f.Bold, OverflowMode.Shrink);
            if (TextFitter.MeasureWidth(r.Text, r.FontPt, f.Bold) <= inner.Width - 1.5f) return r;
        }
        return TextFitter.Fit(list[^1], inner.Width, inner.Height, (float)f.FontSize, (float)f.MinFontSize, f.Bold, OverflowMode.ShrinkThenEllipsis);
    }

    /// <summary>"Isparta 29 Ekim Cumhuriyet Bayramı Satranç Turnuvası" → "… Bayramı Turnuvası" → "… Bayramı".</summary>
    private static IEnumerable<string> TitleLadder(string title)
    {
        yield return title;
        var noChess = System.Text.RegularExpressions.Regex.Replace(title, @"\s+Satran[çc]\b", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        yield return noChess;
        yield return System.Text.RegularExpressions.Regex.Replace(noChess, @"\s+(Turnuvas[ıi]|Turnuva)$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }

    /// <summary>Bir alanın bu eşleştirmedeki metin değeri.</summary>
    public static string Resolve(OverlayField f, Tournament t, Pairing p) => f.Kind switch
    {
        FieldKind.FreeText => f.StaticText ?? "",
        FieldKind.TournamentName => t.Name ?? "",
        FieldKind.Category => p.Category ?? "",
        FieldKind.Date => t.Date ?? "",
        FieldKind.RoundNo => (p.Round ?? t.RoundNo).ToString(),
        FieldKind.BoardNo => p.BoardText, // takımda "3.2"
        FieldKind.WhiteName => p.White.Name,
        FieldKind.WhiteTitle => p.White.Title ?? "",
        FieldKind.WhiteRating => p.White.Rating?.ToString() ?? "",
        FieldKind.WhiteStartNo => p.White.StartNo?.ToString() ?? "",
        FieldKind.BlackName => p.IsBye ? "BAY" : (p.Black?.Name ?? ""),
        FieldKind.BlackTitle => p.Black?.Title ?? "",
        FieldKind.BlackRating => p.Black?.Rating?.ToString() ?? "",
        FieldKind.BlackStartNo => p.Black?.StartNo?.ToString() ?? "",
        FieldKind.WhiteTeam => p.WhiteTeam ?? "",   // bireysel turnuvada boş kalır
        FieldKind.BlackTeam => p.IsBye ? "" : (p.BlackTeam ?? ""),
        FieldKind.DateDay => DatePart(t.Date, 0),
        FieldKind.DateMonth => DatePart(t.Date, 1),
        FieldKind.DateYear2 => DatePart(t.Date, 2),
        FieldKind.WhiteProvince => p.White.Province ?? "", // MainForm.WithProvinces ile doldurulur
        FieldKind.BlackProvince => p.IsBye ? "" : (p.Black?.Province ?? ""),
        _ => "" // Yer/zaman kontrolü/hakem: bilinçli olarak boş
    };

    /// <summary>
    /// Tarihin parçası: 0 gün ("03"), 1 ay ("10"), 2 yılın son iki hanesi ("26").
    /// Kağıttaki ". . . / . . . / 20 . . ." kılavuzuna ayrı ayrı yazmak için.
    /// </summary>
    public static string DatePart(string? date, int part)
    {
        var days = TournamentDates.Parse(date);
        if (days.Count == 0) return "";
        var d = days[0];
        return part switch
        {
            0 => d.Day.ToString("00"),
            1 => d.Month.ToString("00"),
            _ => d.Year.ToString("0000") // her zaman 4 hane (2026)
        };
    }

    /// <summary>Tasarımcı önizlemesi için örnek veri (uzun isimlerle sığdırma testi).</summary>
    public static (Tournament, Pairing) Sample()
    {
        var t = new Tournament(
            Name: "Örnek Açık Satranç Turnuvası",
            RoundNo: 3,
            Pairings: Array.Empty<Pairing>(),
            Date: "14.06.2026");
        // chess-results ile aynı "SOYAD, AD" biçimi
        var p = new Pairing(
            Board: 7,
            White: new Player(12, "HACIMÜFTÜOĞLU, ABDURRAHMAN", "FM", 2345, Province: "AFYONKARAHİSAR"),
            Black: new Player(3, "ÇETİNKAYA, AYŞE NUR", null, 2110, Province: "ISPARTA"),
            Category: "A",
            // Tasarımcıda takım alanlarının yeri görünsün diye örnek takım adları
            WhiteTeam: "ÖRNEK SATRANÇ SPOR KULÜBÜ",
            BlackTeam: "DENEME GENÇLİK SPOR KULÜBÜ");
        return (t, p);
    }

    /// <summary>İnsana okunur alan adı (tasarımcı listesi/etiket için).</summary>
    public static string DisplayName(FieldKind k) => k switch
    {
        FieldKind.FreeText => "Sabit metin",
        FieldKind.TournamentName => "Turnuva adı",
        FieldKind.Category => "Kategori",
        FieldKind.Location => "Yer",
        FieldKind.Date => "Tarih",
        FieldKind.RoundNo => "Tur no",
        FieldKind.TimeControl => "Zaman kontrolü",
        FieldKind.Arbiter => "Hakem",
        FieldKind.BoardNo => "Masa no",
        FieldKind.WhiteName => "Beyaz ad",
        FieldKind.WhiteTitle => "Beyaz unvan",
        FieldKind.WhiteRating => "Beyaz ELO",
        FieldKind.WhiteStartNo => "Beyaz SNo",
        FieldKind.BlackName => "Siyah ad",
        FieldKind.BlackTitle => "Siyah unvan",
        FieldKind.BlackRating => "Siyah ELO",
        FieldKind.BlackStartNo => "Siyah SNo",
        FieldKind.WhiteTeam => "Beyaz takım",
        FieldKind.BlackTeam => "Siyah takım",
        FieldKind.DateDay => "Tarih: gün",
        FieldKind.DateMonth => "Tarih: ay",
        FieldKind.DateYear2 => "Tarih: yıl (4 hane)",
        FieldKind.WhiteProvince => "Beyaz il",
        FieldKind.BlackProvince => "Siyah il",
        _ => k.ToString()
    };
}
