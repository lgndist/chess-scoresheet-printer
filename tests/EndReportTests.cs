using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>Turnuva sonu raporu: final sıralamalarından katılım, il dağılımı, dereceye girenler ve sıralama.</summary>
public class EndReportTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private static ListTable Standings(params (string Rank, string Name, string Rtg, string Pts)[] rows)
        => new("5. Turdan Sonra Sıralama", new[] { "Sıra", "Bşl.No.", "İsim", "Rtg", "Puan", "EB2" },
               rows.Select(r => (IReadOnlyList<string>)new[] { r.Rank, "1", r.Name, r.Rtg, r.Pts, "10" }).ToList());

    private static EndReportData Sample()
    {
        var acik = Standings(("1", "DEMİRER, ELİF", "1442", "4"), ("2", "AVCI, MEHMET ARİF", "1412", "3,5"),
                             ("3", "BİLDİLER, DARAN ÇINAR", "1373", "3"), ("4", "AKTÜRK, RECEP", "0", "1"));
        var y8 = Standings(("1", "KAYA, ALİ", "1100", "5"), ("2", "ŞAHİN, VELİ", "0", "4"));
        var takim = Standings(("1", "Isparta GSK", "", "10"), ("2", "Burdur SK", "", "8"));
        var prov = new Dictionary<string, string> { ["DEMİRER, ELİF"] = "ISPARTA", ["AVCI, MEHMET ARİF"] = "ISPARTA", ["KAYA, ALİ"] = "BURDUR", ["ŞAHİN, VELİ"] = "ISPARTA" };
        return EndReport.Build(new[]
        {
            new CategoryStandings("Açık Kategori", acik, 5, 5, false),
            new CategoryStandings("8 Yaş ve Altı", y8, 4, 5, false),
            new CategoryStandings("Takım", takim, 3, 3, true),
            new CategoryStandings("Boş", null, 0, 5, false),
        }, n => prov.GetValueOrDefault(n), new DateTime(2026, 10, 4));
    }

    [Fact]
    public void Counts_And_Participation()
    {
        var d = Sample();
        Assert.Equal("6 sporcu, 2 takım", d.Values["SPORCU_SAYISI"]);
        Assert.Equal("2", d.Values["IL_SAYISI"]);          // ISPARTA, BURDUR (bilinmeyen sayılmaz)
        Assert.Equal("04.10.2026", d.Values["RAPOR_TARIHI"]);
        var k = d.Lists["katilim"];
        Assert.Equal(("Açık Kategori", "4", "5"), (k[0]["kategori"], k[0]["sporcu"], k[0]["tur"]));
        Assert.Equal("4/5", k[1]["tur"]);                  // turnuva bitmeden: oynanan/toplam
        Assert.Equal("2 takım", k[2]["sporcu"]);
        Assert.Equal("", k[3]["sporcu"]);                  // sıralaması alınamayan kategori boş kalır
    }

    [Fact]
    public void ProvinceDistribution_MostFirst_UnknownLast()
    {
        var il = Sample().Lists["il"];
        Assert.Equal(new[] { ("ISPARTA", "3"), ("BURDUR", "1"), (EndReport.UnknownProvince, "2") },
                     il.Select(r => (r["adi"], r["sayi"])));
    }

    [Fact]
    public void Podium_TopThreePerCategory_NamesFormatted()
    {
        var d = Sample().Lists["derece"];
        Assert.Equal(3 + 2 + 2, d.Count);
        Assert.Equal(("Açık Kategori", "1.", "ELİF DEMİRER", "ISPARTA", "4"), (d[0]["kategori"], d[0]["derece"], d[0]["ad"], d[0]["il"], d[0]["puan"]));
        Assert.Equal("Isparta GSK", d.First(r => r["kategori"] == "Takım")["ad"]);  // takım adı olduğu gibi
        var s = Sample().Lists["siralama"];
        Assert.Equal(8, s.Count);
        Assert.Equal("", s.First(r => r["ad"] == "RECEP AKTÜRK")["rtg"]);           // 0 rating basılmaz
    }

    [Fact]
    public void Template_FillsAllTables_NoTokensLeft_CategoryCellsMerged()
    {
        var tpl = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "turnuva_sonu_raporu_sablon.docx"));
        var a = DocxTemplate.Analyze(tpl);
        Assert.Equal(new[] { "katilim", "il", "derece", "siralama" }, a.Lists);
        Assert.Contains("SPORCU_SAYISI", a.Fields);
        Assert.Contains("DEGERLENDIRME", a.Fields);

        var data = new ReportData();
        foreach (var f in a.Fields) data.Values[f] = "X";
        foreach (var (k, rows) in Sample().Lists) data.Lists[k] = rows;
        var outp = DocxTemplate.Fill(tpl, data);
        using var zip = new ZipArchive(new MemoryStream(outp));
        string Part(string n) { using var r = new StreamReader(zip.GetEntry(n)!.Open()); return r.ReadToEnd(); }
        var xml = Part("word/document.xml");
        Assert.DoesNotMatch(@"\{\{", xml);
        Assert.Contains("TURNUVA SONU RAPORU", Part("word/header1.xml"));
        Assert.Contains("ELİF DEMİRER", xml);
        // Sütun başlıkları etiket sanılıp il değeriyle ezilmemeli ("İL | SPORCU SAYISI", "İLİ | PUAN", "İLİ | RTG")
        Assert.Equal(2, Regex.Matches(xml, "SPORCU SAYISI").Count);
        Assert.Contains(">PUAN<", xml);
        Assert.Contains(">RTG<", xml);

        // Dereceye girenler tablosunda aynı kategorinin 2. ve 3. satırı birleşik (vMerge devam, metin yok)
        var x = XDocument.Parse(xml);
        var podiumRows = x.Descendants(W + "tr").Where(tr => tr.Descendants(W + "t").Any(t => t.Value is "2." or "3.")).Take(2).ToList();
        foreach (var tr in podiumRows)
        {
            var first = tr.Elements(W + "tc").First();
            Assert.NotNull(first.Element(W + "tcPr")!.Element(W + "vMerge"));
            Assert.Null(first.Element(W + "tcPr")!.Element(W + "vMerge")!.Attribute(W + "val"));
            Assert.Equal("", string.Concat(first.Descendants(W + "t").Select(t => t.Value)));
        }
    }

    [Fact]
    public void TableCells_AreMarked_ForEditing()
    {
        var tpl = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "turnuva_sonu_raporu_sablon.docx"));
        var data = new ReportData();
        foreach (var (k, rows) in Sample().Lists) data.Lists[k] = rows;
        var marker = new FieldMarker(k => $"[{k}]");
        DocxTemplate.Fill(tpl, data, marker);
        var keys = marker.Keys.Values.ToHashSet();
        Assert.Contains("derece[0].ad", keys);
        Assert.Contains("il[2].sayi", keys);
        Assert.Contains("siralama[7].puan", keys);
        Assert.DoesNotContain("derece[1].kategori", keys); // birleşik hücrenin devamı işaretlenmez
    }
}
