using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>TSF kayıt listesinin kategorileri/sayıları (masa kartı adedi) ve turnuva listesinin güncellik süzgeci.</summary>
public class RegistrationCategoryTests
{
    [Theory]
    [InlineData("12 Yaş ve Altı", "12 Yaş", true)]
    [InlineData("8 Yaş Kategorisi", "8 Yaş ve Altı", true)]
    [InlineData("10 Yaş ve Altı Kategorisi (2017 ve 2020 yılları arasında doğan sporcular)", "10 Yaş", true)]
    [InlineData("Açık Kategori", "Açık", true)]
    [InlineData("Açık Kategori (2020 ve önceki yıllarda doğan sporcular)", "Genel", true)]
    [InlineData("15 Yaş Genel", "15 Yaş Genel", true)]
    [InlineData("15 Yaş Kız", "15 Yaş Genel", false)]   // cinsiyet ayrı kategori
    [InlineData("15 Yaş Kız", "15 Yaş Kızlar", true)]
    [InlineData("13 Yaş Genel", "15 Yaş Genel", false)]
    [InlineData("A Kategorisi", "A", true)]
    [InlineData("A Kategorisi", "Açık Kategori", false)]
    [InlineData("12 Yaş ve Altı", "Açık Kategori", false)]
    public void Same_AgeGenderOpenLetter(string reg, string cr, bool expected)
        => Assert.Equal(expected, RegistrationCategories.Same(reg, cr));

    private static EventRegistration Reg(params (string Cat, int N)[] cats)
    {
        var r = new EventRegistration { Tids = { 359 }, Titles = { "Isparta Yıldızlar (13-18) İl Birinciliği Satranç Turnuvası 2026" } };
        int i = 0;
        foreach (var (cat, n) in cats)
            for (int k = 0; k < n; k++) r.Entries.Add(new PlayerRegistration { Given = $"AD{i}", Surname = $"SOYAD{i++}", Province = "ISPARTA", Category = cat, Tid = 359 });
        return r;
    }

    [Fact]
    public void Counts_And_Boards_PerCategory()
    {
        var reg = Reg(("13 Yaş Genel", 5), ("13 Yaş Kız", 1), ("17 Yaş Genel", 5), ("16 Yaş Genel", 2));
        var counts = RegistrationCategories.Counts(reg);
        Assert.Equal(new[] { ("13 Yaş Genel", 5), ("13 Yaş Kız", 1), ("17 Yaş Genel", 5), ("16 Yaş Genel", 2) }, counts.Select(c => (c.Name, c.Count)));
        Assert.Equal(2, counts[0].Boards);   // 5 sporcu → 2 masa + 1 BAY
        Assert.Equal(0, counts[1].Boards);   // tek sporcu: masa yok
        Assert.Equal(1, counts[3].Boards);
        Assert.Equal(5, RegistrationCategories.For(reg, "13 Yaş Genel")!.Count);
        Assert.Equal(1, RegistrationCategories.For(reg, "13 Yaş Kızlar")!.Count);
        Assert.Null(RegistrationCategories.For(reg, "14 Yaş Genel"));
        Assert.Null(RegistrationCategories.For(null, "13 Yaş Genel"));
    }

    [Fact]
    public void CategoryOf_FallsBackToFormTitle()
    {
        // Ankara gibi her kategoriyi ayrı form açan illerde KATEGORİ sütunu boş olabilir.
        var reg = new EventRegistration { Tids = { 1268 }, Titles = { "Ankara 29 Ekim Hızlı Satranç Turnuvası - 8 Yaş ve Altı Kategorisi" } };
        reg.Entries.Add(new PlayerRegistration { Given = "ALİ", Surname = "KAYA", Tid = 1268 });
        Assert.Equal("8 Yaş ve Altı Kategorisi", RegistrationCategories.CategoryOf(reg.Entries[0], reg));
    }

    [Theory]
    [InlineData("2026/10/17", "2026/10/18", true)]   // 5 gün önce bitti
    [InlineData("2026/10/10", "2026/10/11", true)]   // 12 gün önce
    [InlineData("2026/09/01", "2026/09/02", false)]  // eski: yalnız ad/numarayla
    [InlineData("2026/10/30", "2026/10/31", true)]   // yaklaşan
    [InlineData(null, null, true)]                   // tarihsiz (federasyon listesi): güncel sayılır
    public void IsRecent_TwoWeeks(string? start, string? end, bool expected)
    {
        DateTime? P(string? s) => s is null ? null : DateTime.ParseExact(s, "yyyy/MM/dd", null);
        Assert.Equal(expected, new TournamentRef(1, "x", P(start), P(end)).IsRecent(new DateTime(2026, 10, 23)));
    }

    [Fact]
    public void SearchResults_ReadStartAndEndDates()
    {
        var html = "<table><tr><td>1</td><td><a href=\"https://chess-results.com/tnr1520001.aspx?lan=8\">Isparta Ekim Ayı ELO Satranç Turnuvası</a></td>" +
                   "<td>TUR</td><td></td><td>4 Gün 18 Saat</td><td>2026/10/17</td><td>2026/10/18</td></tr></table>";
        var t = ChessResultsParser.ParseFederationList(html).Single();
        Assert.Equal(1520001, t.Tnr);
        Assert.Equal(new DateTime(2026, 10, 17), t.Start);
        Assert.Equal(new DateTime(2026, 10, 18), t.End);
    }
}
