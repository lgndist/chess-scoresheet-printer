using NotasyonOtomasyonu.App.Lists;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Hakem eşleştirme kağıtları: sayfa düzenleri, uzun kategori, BAY, PDF.</summary>
public class ArbiterSheetTests
{
    private static Tournament Pairings(int boards, bool bye = false)
    {
        var list = Enumerable.Range(1, boards).Select(b => new Pairing(
            b, new Player(b, $"BEYAZ {b}", Rating: 1500 + b), new Player(b + 100, $"SİYAH {b}", Rating: 1400 + b),
            WhitePoints: "2", BlackPoints: "1½")).ToList();
        if (bye) list.Add(new Pairing(boards + 1, new Player(999, "BAY ALAN"), null));
        return new Tournament("Deneme", 3, list);
    }

    private static ArbiterSheet Sheet(string cat, int boards, int group = 1, bool bye = false) => new(cat, 3, "23.10.2026 • 10.00", Pairings(boards, bye), group);

    [Fact]
    public void Separate_OnePagePerCategory()
    {
        var p = new ArbiterSheetPrinter(new[] { Sheet("A", 4), Sheet("B", 5), Sheet("C", 3) }, "Turnuva", SheetLayout.Separate);
        Assert.Equal(3, p.PageCount);
    }

    [Fact]
    public void Fit_SmallCategoriesShareAPage()
    {
        var p = new ArbiterSheetPrinter(new[] { Sheet("A", 4), Sheet("B", 5), Sheet("C", 3) }, "Turnuva", SheetLayout.Fit);
        Assert.Equal(1, p.PageCount);
    }

    [Fact]
    public void Groups_StartNewPagePerGroup()
    {
        // 3 kategori bir sayfaya (grup 1), 2'si diğerine (grup 2)
        var sheets = new[] { Sheet("A", 3, 1), Sheet("B", 3, 1), Sheet("C", 3, 1), Sheet("D", 3, 2), Sheet("E", 3, 2) };
        Assert.Equal(2, new ArbiterSheetPrinter(sheets, "Turnuva", SheetLayout.Groups).PageCount);
        Assert.Equal(1, new ArbiterSheetPrinter(sheets, "Turnuva", SheetLayout.Fit).PageCount);
    }

    [Fact]
    public void LongCategory_ContinuesOnNextPages()
    {
        var p = new ArbiterSheetPrinter(new[] { Sheet("A", 70, bye: true) }, "Turnuva", SheetLayout.Fit);
        Assert.True(p.PageCount >= 3, $"70 masa en az 3 sayfa olmalı ({p.PageCount})");
        for (int i = 0; i < p.PageCount; i++) using (p.RenderPage(i, 300)) { }
    }

    [Fact]
    public void Fit_CategoryThatDoesNotFitRemainingSpace_StartsNewPage()
    {
        // İlk kategori sayfanın çoğunu kaplar; ikincisi kalan yere sığmaz ama tek başına sığar → bölünmeden yeni sayfada
        var p = new ArbiterSheetPrinter(new[] { Sheet("A", 22), Sheet("B", 12) }, "Turnuva", SheetLayout.Fit);
        Assert.Equal(2, p.PageCount);
    }

    [Theory]
    [InlineData(11f)]
    [InlineData(12.5f)]
    [InlineData(14f)]
    public void LargerFont_NeedsMorePages(float pt)
    {
        var p = new ArbiterSheetPrinter(new[] { Sheet("A", 40) }, "Turnuva", SheetLayout.Fit, pt);
        Assert.True(p.PageCount >= (pt >= 14 ? 2 : 1));
    }

    [Fact]
    public void Pdf_IsWritten()
    {
        var path = Path.Combine(Path.GetTempPath(), $"csp-hakem-{Guid.NewGuid():N}.pdf");
        try
        {
            new ArbiterSheetPrinter(new[] { Sheet("15 Yaş Genel", 8, bye: true), Sheet("13 Yaş Kız", 2) }, "Turnuva", SheetLayout.Fit).SavePdf(path);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Empty_HasOneBlankPage()
        => Assert.Equal(1, new ArbiterSheetPrinter(Array.Empty<ArbiterSheet>(), "Turnuva", SheetLayout.Fit).PageCount);
}

/// <summary>Kayıtları yeni kapanmış formların bulunması (sahte TSF sitesiyle).</summary>
public class ClosedFormProbeTests
{
    private static string Closed(string title) =>
        $"<center><font color=maroon><b>{title}</b></font><br><br><b>Turnuva Kayıtları Sona Ermiştir. <br> Kayıt Bitiş Tarihi: 2026-10-23 18:00:00</b></center>" +
        "<font> Toplam Kayıt: 1 </font><br><table><tr><td>AD</td><td>SOYAD</td><td>İL</td><td>ELO</td><td>UKD</td><td>KATEGORİ</td></tr>" +
        "<tr><td>DENEME</td><td>KİŞİ</td><td>ISPARTA</td><td>0</td><td>0</td><td>Genel</td></tr></table>";

    private const string Foreign = "<font> Toplam Kayıt: 1 </font><table><tr><td>AD</td><td>SOYAD</td><td>İL</td></tr><tr><td>A</td><td>B</td><td>ISPARTA</td></tr></table>";

    [Fact]
    public async Task OnlyTitledClosedPages_AreReturned()
    {
        using var site = new LocalServer(req =>
        {
            var tid = req.QueryString["tid"];
            return tid switch
            {
                "357" => LocalServer.Text(Closed("Kapanan Turnuva 2026")),
                "355" => LocalServer.Text(Closed("Daha Eski Kapanan")),
                _ => LocalServer.Text(Foreign)               // yanlış/eski numara: başlıksız yabancı liste
            };
        });
        using var sites = new TsfSites();
        var pages = await sites.ListRecentlyClosedAsync(site.BaseUrl, aroundTid: 358, skip: new[] { 358 });
        Assert.Equal(new[] { 357, 355 }, pages.Select(p => p.Tid));
        Assert.All(pages, p => Assert.True(p.Closed));
        Assert.Equal("Kapanan Turnuva 2026", pages[0].Title);
        Assert.Equal(new DateTime(2026, 10, 23, 18, 0, 0), pages[0].ClosesAt);
    }

    [Fact]
    public async Task UnreachableSite_ReturnsEmpty()
    {
        using var sites = new TsfSites();
        var pages = await sites.ListRecentlyClosedAsync("http://localhost:1", aroundTid: 5, below: 2, above: 0);
        Assert.Empty(pages);
    }
}

/// <summary>Takım turnuvası hakem kağıdı: maç ara başlıkları.</summary>
public class ArbiterTeamTests
{
    private static Tournament Team(int matches, int boards)
    {
        var list = new List<Pairing>();
        int n = 0;
        for (int m = 1; m <= matches; m++)
            for (int b = 1; b <= boards; b++)
                list.Add(new Pairing(++n, new Player(n, $"OYUNCU {n}A"), new Player(n + 500, $"OYUNCU {n}B"),
                                     BoardLabel: $"{m}.{b}", WhiteTeam: $"TAKIM {m}A", BlackTeam: $"TAKIM {m}B"));
        return new Tournament("Takım", 2, list, System: TournamentSystem.TeamSwiss);
    }

    [Fact]
    public void TeamSheet_RendersWithMatchHeaders()
    {
        var p = new ArbiterSheetPrinter(new[] { new ArbiterSheet("Okul Takım", 2, null, Team(5, 4)) }, "Turnuva", SheetLayout.Fit);
        Assert.Equal(1, p.PageCount); // 20 masa + 5 maç başlığı tek sayfaya sığar
        using var bmp = p.RenderPage(0, 600);
        // Maç başlığı satırının açık yeşil zemini çizilmiş olmalı
        bool found = false;
        for (int y = 0; y < bmp.Height && !found; y += 2)
            if (bmp.GetPixel(300, y) is { R: 232, G: 240, B: 224 }) found = true;
        Assert.True(found, "maç ara başlığı çizilmedi");
    }

    [Fact]
    public void LongTeamSheet_SplitsAcrossPages()
    {
        var p = new ArbiterSheetPrinter(new[] { new ArbiterSheet("Okul Takım", 2, null, Team(12, 4)) }, "Turnuva", SheetLayout.Fit);
        Assert.True(p.PageCount >= 2);
        for (int i = 0; i < p.PageCount; i++) using (p.RenderPage(i, 300)) { }
    }
}
