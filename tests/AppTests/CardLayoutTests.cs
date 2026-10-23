using NotasyonOtomasyonu.App.Branding;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Masa kartı yerleşimi: üst bölüm boyu, logo yerleşimleri, yazıların logolardan kaçması.</summary>
public class CardLayoutTests
{
    private const string Event = "Isparta Yıldızlar İl Birinciliği Satranç Turnuvası";
    private static readonly SizeF Wide = new(1600, 500), Tall = new(800, 1000);

    public static IEnumerable<object?[]> AllCases()
    {
        var modes = CardBranding.Modes.Select(m => m.Mode).Append(CardBranding.Auto);
        foreach (var mode in modes)
        foreach (var landscape in new[] { true, false })
        foreach (var poster in new SizeF?[] { null, Wide, Tall })
        foreach (var full in new[] { false, true })
        foreach (var (gsb, tsf) in new[] { (true, true), (true, false), (false, true) })
        {
            if (poster is null && full) continue;
            yield return new object?[] { mode, landscape, poster, full, gsb, tsf };
        }
    }

    [Theory]
    [MemberData(nameof(AllCases))]
    public void TextNeverOverlapsLogos_AndStaysOnPage(string mode, bool landscape, SizeF? poster, bool full, bool gsb, bool tsf)
    {
        var br = new CardBranding(gsb, tsf, "Isparta", mode);
        var l = CardLayout.For(landscape, poster, full, br, Event, "Montserrat");
        var page = new RectangleF(0, 0, l.PageW, l.PageH);
        Assert.InRange(l.HeaderH, l.PageH * CardLayout.MinHeaderRatio - 0.01f, l.PageH * CardLayout.MaxHeaderRatio + 0.01f);
        Assert.Equal((gsb ? 1 : 0) + (tsf ? 1 : 0), l.Logos.Count);
        foreach (var logo in l.Logos)
        {
            Assert.True(page.Contains(logo.Rect), $"logo sayfa dışında: {logo.Rect}");
            Assert.False(l.BandTextRect.IntersectsWith(logo.Rect), $"kategori yazısı {logo.Kind} logosunun üstünde ({mode})");
            if (poster is null) Assert.False(l.NameRect.IntersectsWith(logo.Rect), $"turnuva adı {logo.Kind} logosunun üstünde ({mode})");
        }
        Assert.True(l.BandTextRect.Width > 100 && l.BandTextRect.Height > 60, $"kategori alanı çok küçük: {l.BandTextRect}");
        if (l.Logos.Count == 2)
            Assert.False(l.Logos[0].Rect.IntersectsWith(l.Logos[1].Rect), "logolar üst üste");
    }

    [Theory]
    [InlineData(CardBranding.SeamCenter)]
    [InlineData(CardBranding.SeamSides)]
    [InlineData(CardBranding.TopCorners)]
    [InlineData(CardBranding.BottomCorners)]
    [InlineData(CardBranding.HeaderSides)]
    public void TwoLogos_SymmetricAboutCentre_SingleLogoCentred(string mode)
    {
        var two = CardLayout.For(true, null, false, new CardBranding(true, true, "Isparta", mode), Event, null);
        float c = two.PageW / 2;
        float mid = (two.Logos[0].Rect.Left + two.Logos[1].Rect.Right) / 2;
        Assert.InRange(mid, c - 1, c + 1);
        Assert.Equal(two.Logos[0].Rect.Height, two.Logos[1].Rect.Height, 2); // aynı boyda

        var one = CardLayout.For(true, null, false, new CardBranding(false, true, "Isparta", mode), Event, null);
        var r = one.Logos.Single().Rect;
        Assert.InRange(r.Left + r.Width / 2, c - 1, c + 1);
    }

    [Fact]
    public void SeamCenter_StraddlesPosterEdge()
    {
        var l = CardLayout.For(true, Wide, false, new CardBranding(true, true, "Isparta", CardBranding.SeamCenter), Event, null);
        foreach (var p in l.Logos) Assert.InRange(p.Rect.Top + p.Rect.Height / 2, l.SeamY - 0.5f, l.SeamY + 0.5f);
        // Afiş bitişi afişin gerçek alt kenarı (yuvaya oranla sığdırılmış)
        Assert.InRange(l.SeamY, CardLayout.Margin, l.HeaderH);
    }

    [Fact]
    public void Auto_PicksHeaderSidesWithoutPoster_SeamCenterWithPoster()
    {
        var br = new CardBranding(true, true, "Isparta");
        Assert.Equal(CardBranding.HeaderSides, CardLayout.For(true, null, false, br, Event, null).Mode);
        Assert.Equal(CardBranding.SeamCenter, CardLayout.For(true, Wide, false, br, Event, null).Mode);
    }

    [Fact]
    public void Header_FitsPoster_NoEmptySpaceAtTop()
    {
        var l = CardLayout.For(true, Wide, false, CardBranding.None, Event, null);
        var drawn = CardLayout.FitImage(Wide, l.LogoRect);
        Assert.InRange(drawn.Top, CardLayout.Margin - 0.5f, CardLayout.Margin + 0.5f); // afiş en üstten başlar
        var full = CardLayout.For(true, Wide, true, CardBranding.None, Event, null);
        Assert.InRange(full.HeaderH, full.PageW * Wide.Height / Wide.Width - 0.5f, full.PageW * Wide.Height / Wide.Width + 0.5f);
    }

    [Fact]
    public void Custom_SpotsRoundTrip()
    {
        var spots = new Dictionary<string, LogoSpot> { ["gsb:L"] = new() { X = 0.2, Y = 0.5, H = 0.15 } };
        var l = CardLayout.For(true, null, false, new CardBranding(true, true, "Isparta", CardBranding.Custom, 1, true, spots), Event, null);
        var g = l.Logos.First(p => p.Kind == "gsb").Rect;
        Assert.InRange(g.Left + g.Width / 2, 0.2f * l.PageW - 0.5f, 0.2f * l.PageW + 0.5f);
        Assert.InRange(g.Height, 0.15f * l.PageH - 0.5f, 0.15f * l.PageH + 0.5f);
        Assert.Contains(l.Logos, p => p.Kind == "tsf"); // kaydı olmayan logo da yerleşir
    }

    [Fact]
    public void Custom_SpotOutsidePage_IsClamped()
    {
        var spots = new Dictionary<string, LogoSpot> { ["tsf:L"] = new() { X = 1.4, Y = -0.3, H = 0.2 } };
        var l = CardLayout.For(true, null, false, new CardBranding(false, true, null, CardBranding.Custom, 1, true, spots), Event, null);
        Assert.True(new RectangleF(0, 0, l.PageW, l.PageH).Contains(l.Logos.Single().Rect));
    }

    [Fact]
    public void Avoid_PicksLargestRemainingPart()
    {
        var box = new RectangleF(0, 0, 400, 200);
        Assert.Equal(box, CardLayout.Avoid(box, new[] { new RectangleF(500, 500, 10, 10) }));            // değmiyor
        var below = CardLayout.Avoid(box, new[] { new RectangleF(150, -20, 100, 60) }, pad: 0);         // üstte ortada
        Assert.Equal(40, below.Top);
        Assert.Equal(0, CardLayout.Avoid(box, new[] { new RectangleF(-10, -10, 500, 300) }).Width);     // tamamen kaplıyor
    }

    [Fact]
    public void Draw_AllModes_NoException()
    {
        using var bmp = new Bitmap(842, 595);
        using var g = Graphics.FromImage(bmp);
        g.PageUnit = GraphicsUnit.Point;
        foreach (var (mode, _) in CardBranding.Modes)
        {
            var br = new CardBranding(true, true, "Kahramanmaraş", mode);
            var l = CardLayout.For(true, null, false, br, Event, "Montserrat");
            var (gsb, tsf) = br.Load(200);
            using (gsb) using (tsf)
                CardPrinter.Draw(g, l, new CardSpec("15 YAŞ GENEL", Color.Red, 1), null, Event, "Montserrat", filled: true,
                                 bandPt: CardPrinter.CommonBandPt(l, new[] { "15 YAŞ GENEL" }, "Montserrat"), branding: br, gsb: gsb, tsf: tsf);
        }
    }

    [Fact]
    public void Pdf_WithLogos_IsWritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"csp-kart-{Guid.NewGuid():N}.pdf");
        try
        {
            var p = new CardPrinter(new[] { new CardSpec("15 YAŞ GENEL", Color.Blue, 2), new CardSpec("11 YAŞ KIZ", Color.Red, 1) },
                                    null, Event, "Montserrat", true, false, false, new CardBranding(true, true, "Isparta"));
            Assert.Equal(3, p.PageCount);
            p.SavePdf(path);
            var head = File.ReadAllBytes(path);
            Assert.True(head.Length > 10_000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(head, 0, 4));
        }
        finally { File.Delete(path); }
    }
}

/// <summary>İl logoları: 81 ilin tümü, boyutlar, yazının yaya sığması.</summary>
public class LogoTests
{
    public static IEnumerable<object[]> AllProvinces() => Provinces.List.Skip(1).Select(p => new object[] { p });

    [Theory]
    [MemberData(nameof(AllProvinces))]
    public void EveryProvinceLogo_Renders_WithNameOnLowerArc(string province)
    {
        using var bmp = OfficialLogos.Tsf(province, 400);
        Assert.Equal(400, bmp.Width);
        // Alt yay bandında (yarıçap ~%80–93, alt yarı) koyu piksel = il adı yazılmış; "1991" tabandan silinmiş olduğundan
        // yazı yoksa bu bölge boş kalırdı.
        int dark = 0;
        for (int y = 300; y < 400; y++)
        for (int x = 80; x < 320; x++)
        {
            double r = Math.Sqrt((x - 200.0) * (x - 200) + (y - 200.0) * (y - 200)) / 200;
            if (r < 0.80 || r > 0.93) continue;
            var c = bmp.GetPixel(x, y);
            if (c.A > 200 && c.R + c.G + c.B < 200) dark++;
        }
        Assert.True(dark > 150, $"{province}: alt yayda yazı bulunamadı ({dark})");
        Assert.Equal(Provinces.Print(province), OfficialLogos.LogoText(province));
    }

    [Fact]
    public void General_Logo_SaysFoundingYear()
    {
        Assert.Equal("1991", OfficialLogos.LogoText(null));
        Assert.Equal("1991", OfficialLogos.LogoText("Bilinmeyen İl"));
        Assert.Equal("Türkiye Satranç Federasyonu", OfficialLogos.Caption(null));
        Assert.Equal("TSF Isparta İl Temsilciliği", OfficialLogos.Caption("ISPARTA"));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(46)]
    [InlineData(138)]
    [InlineData(1200)]
    [InlineData(5000)] // sınırlanır
    public void Sizes_AreExact(int px)
    {
        using var t = OfficialLogos.Tsf("Isparta", px);
        using var g = OfficialLogos.Gsb(px);
        using var a = OfficialLogos.AppLogo(px);
        int exp = Math.Clamp(px, 32, 2400);
        Assert.Equal(exp, t.Width);
        Assert.Equal(Math.Clamp(px, 16, 2400), g.Width);
        Assert.Equal(Math.Clamp(px, 16, 1024), a.Width);
        Assert.Equal(1f, OfficialLogos.Aspect("gsb"), 2);
    }

    [Fact]
    public void Logo_CornersTransparent_CentreOpaque()
    {
        using var b = OfficialLogos.Tsf("Isparta", 200);
        Assert.Equal(0, b.GetPixel(2, 2).A);   // daire dışı saydam (başlıktaki yeşil zeminde kare görünmesin)
        Assert.Equal(255, b.GetPixel(100, 100).A);
    }
}
