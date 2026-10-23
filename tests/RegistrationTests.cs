using System.Text;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>TSF kayıt listesi okuma ve chess-results sporcularıyla il eşleştirmesi.</summary>
public class RegistrationTests
{
    // Gerçek sayfanın yapısı: form başlığı "… KAYIT FORMU", gizli tid, "Toplam Kayıt", AD/SOYAD/İL/ELO/UKD/KATEGORİ tablosu.
    private static string FormPage(int tid, params string[][] rows)
    {
        var sb = new StringBuilder();
        sb.Append("<html><body><form name=kayitform><table><tr><td colspan=2>\n        Isparta 29 Ekim Cumhuriyet Bayramı Satranç Turnuvası KAYIT FORMU</td></tr>");
        sb.Append($"<input type=\"hidden\" name=\"tid\" value=\"{tid}\" /></table></form>");
        sb.Append($"<font> Toplam Kayıt: {rows.Length} </font><br><table width=650>");
        sb.Append("<tr><td>AD</td><td>SOYAD</td><td>İL</td><td>ELO</td><td>UKD</td><td>KATEGORİ</td></tr>");
        foreach (var r in rows)
            sb.Append("<tr>").Append(string.Concat(r.Select(c => $"<td width=130>{c} </td>"))).Append("</tr>");
        return sb.Append("</table></body></html>").ToString();
    }

    [Fact]
    public void ParsePage_ReadsRows()
    {
        var html = FormPage(356,
            new[] { "İSMAİL MERT", "ODABAŞ", "YALOVA", "1932", "1732", "Açık Kategori" },
            new[] { "FARUK ARDA", "ALBAYRAK", "ISPARTA", "0", "1449", "12 Yaş ve Altı" });
        var page = TsfRegistrations.ParsePage(html, 356)!;
        Assert.Equal("Isparta 29 Ekim Cumhuriyet Bayramı Satranç Turnuvası", page.Title);
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Entries.Count);
        var e = page.Entries[1];
        Assert.Equal(("FARUK ARDA", "ALBAYRAK", "ISPARTA"), (e.Given, e.Surname, e.Province));
        Assert.Null(e.Elo);          // 0 = yok
        Assert.Equal(1449, e.Ukd);
        Assert.Equal("12 Yaş ve Altı", e.Category);
    }

    [Fact]
    public void ParsePage_RejectsForeignList()
    {
        // Kapanmış/yanlış numara: site başlıksız, başka bir listeyi gösterir.
        var foreign = "<html><font>Toplam Kayıt: 1</font><table><tr><td>AD</td><td>SOYAD</td><td>İL</td></tr>" +
                      "<tr><td>OSMAN</td><td>KESKİN</td><td>ISPARTA</td></tr></table></html>";
        Assert.Null(TsfRegistrations.ParsePage(foreign, 340));
        // Başka formun sayfası (numara tutmuyor).
        Assert.Null(TsfRegistrations.ParsePage(FormPage(356), 355));
    }

    [Fact]
    public void ParsePage_ReadsClosedForm()
    {
        // Kayıt süresi bitince: form ve gizli numara yok; başlık, "Turnuva Kayıtları Sona Ermiştir" ve aynı liste var.
        var closed = "<div class=\"component-content\"><br><br><center><font color=maroon><b>Örnek İl Birinciliği Satranç Turnuvası 2026</b></font>" +
                     "<br><br><b>Turnuva Kayıtları Sona Ermiştir. <br> Kayıt Bitiş Tarihi: 2026-10-23 18:00:00</b></center><br>" +
                     "<font style=\"color:maroon\"> Toplam Kayıt: 2 </font><br><table width=650>" +
                     "<tr><td>AD</td><td>SOYAD</td><td>İL</td><td>ELO</td><td>UKD</td><td>KATEGORİ</td></tr>" +
                     "<tr><td>DENEME</td><td>BİR</td><td>ISPARTA</td><td>1500</td><td>1400</td><td>15 Yaş Genel</td></tr>" +
                     "<tr><td>ÖRNEK</td><td>İKİ</td><td>BURDUR</td><td>0</td><td>0</td><td>13 Yaş Kız</td></tr></table></div>";
        var page = TsfRegistrations.ParsePage(closed, 359)!;
        Assert.True(page.Closed);
        Assert.Equal("Örnek İl Birinciliği Satranç Turnuvası 2026", page.Title);
        Assert.Equal(new DateTime(2026, 10, 23, 18, 0, 0), page.ClosesAt);
        Assert.Equal(2, page.Entries.Count);
        Assert.Equal("13 Yaş Kız", page.Entries[1].Category);
        Assert.False(TsfRegistrations.ParsePage(FormPage(356, new[] { "A", "B", "ISPARTA", "0", "0", "Genel" }), 356)!.Closed);
    }

    [Fact]
    public void ParseOptions_ListAndSingleForm()
    {
        var list = "<div><a href=\"/turnuva-kayit-form?layout=girisform&amp;tid=1268\" target=\"_blank\">Ankara 29 Ekim - 8 Yaş</a>" +
                   "<a href=\"/turnuva-kayit-form?layout=girisform&amp;tid=1269\">Ankara 29 Ekim - 10 Yaş</a>" +
                   "<a href=\"/2022-12-13-08-11-05\">TURNUVA KAYIT İPTAL</a></div>";
        var opts = TsfRegistrations.ParseOptions(list);
        Assert.Equal(new[] { 1268, 1269 }, opts.Select(o => o.Tid));
        Assert.Equal("Ankara 29 Ekim - 8 Yaş", opts[0].Title);

        var single = TsfRegistrations.ParseOptions(FormPage(381));
        Assert.Single(single);
        Assert.Equal(381, single[0].Tid);
    }

    [Theory]
    [InlineData("https://www.isparta.tsf.org.tr/turnuva-kayit-form?layout=girisform&tid=356", 356)]
    [InlineData("356", 356)]
    [InlineData("abc", null)]
    public void ParseTid(string input, int? tid) => Assert.Equal(tid, TsfRegistrations.ParseTid(input));

    [Fact]
    public void SiteOf_OnlyTsfHosts()
    {
        Assert.Equal("https://www.isparta.tsf.org.tr", TsfRegistrations.SiteOf("https://www.isparta.tsf.org.tr/turnuva-kayit-form?tid=1"));
        Assert.Null(TsfRegistrations.SiteOf("https://example.com/x"));
    }

    [Fact]
    public void Decode_MixedUtf8AndWindows1254()
    {
        // "İSMAİL" UTF-8, "Lütfen" ve "Kaydı" Windows-1254 (sayfanın sabit metinleri).
        var bytes = Encoding.UTF8.GetBytes("İSMAİL ").Concat(new byte[] { 0x4C, 0xFC, 0x74, 0x66, 0x65, 0x6E, 0x20, 0x4B, 0x61, 0x79, 0x64, 0xFD }).ToArray();
        Assert.Equal("İSMAİL Lütfen Kaydı", TsfRegistrations.Decode(bytes));
    }

    // ---------------- eşleştirme ----------------
    private static PlayerRegistration R(string given, string surname, string il, int? ukd = null, string cat = "Açık Kategori")
        => new() { Given = given, Surname = surname, Province = il, Ukd = ukd, Category = cat };

    private static ProvinceMatch One(string crName, int? rating, string? cat, params PlayerRegistration[] regs)
        => ProvinceMatcher.MatchAll(new[] { new RosterPlayer(crName, rating, cat) }, regs)[0];

    [Fact]
    public void Exact_IgnoresTurkishLettersAndOrder()
    {
        var m = One("CETINKAYA, AYSE NUR", 1500, null, R("AYŞE NUR", "ÇETİNKAYA", "ISPARTA"));
        Assert.Equal(MatchKind.Exact, m.Kind);
        Assert.Equal("ISPARTA", m.Province);
        // Virgülsüz "Ad Soyad" (dosyadan gelen liste) da tutar.
        Assert.Equal(MatchKind.Exact, One("Ayşe Nur Çetinkaya", null, null, R("AYŞE NUR", "ÇETİNKAYA", "ISPARTA")).Kind);
    }

    [Fact]
    public void Siblings_SameSurnameDifferentName_NotMatched()
    {
        var m = One("ŞAHİN, ALPER", 1692, null, R("İLKER", "ŞAHİN", "BURDUR", 1595));
        Assert.Equal(MatchKind.NotFound, m.Kind);
        Assert.Null(m.Province);
    }

    [Fact]
    public void MissingMiddleName_IsProbable()
    {
        var m = One("AVCI, MEHMET", 1412, null, R("MEHMET ARİF", "AVCI", "ISPARTA", 1412));
        Assert.Equal(MatchKind.Probable, m.Kind);
        Assert.Equal("ISPARTA", m.Province);
    }

    [Fact]
    public void OneLetterTypo_IsProbable()
        => Assert.Equal(MatchKind.Probable, One("YILMAZ, MUHAMET", null, null, R("MUHAMMET", "YILMAZ", "ANTALYA")).Kind);

    [Fact]
    public void SameName_DifferentProvinces_RatingDecides()
    {
        var a = R("MEHMET", "YILDIZ", "ISPARTA", 1374);
        var b = R("MEHMET", "YILDIZ", "ANTALYA", 1100);
        Assert.Equal("ISPARTA", One("YILDIZ, MEHMET", 1374, null, a, b).Province);
        // Puan da tutmuyorsa: belirsiz, il yazılmaz.
        var m = One("YILDIZ, MEHMET", 1250, null, a, b);
        Assert.Equal(MatchKind.Ambiguous, m.Kind);
        Assert.Null(m.Province);
    }

    [Fact]
    public void SameName_SameProvince_StillFilled()
    {
        var m = One("YILDIZ, MEHMET", null, null, R("MEHMET", "YILDIZ", "ISPARTA"), R("MEHMET", "YILDIZ", "ISPARTA"));
        Assert.Equal("ISPARTA", m.Province);
    }

    [Fact]
    public void SameName_AgeCategoryDecides()
    {
        var kid = R("ELİF", "KAYA", "BURDUR", cat: "10 Yaş ve Altı Kategorisi (2017 ve 2020 yılları arasında doğan sporcular)");
        var adult = R("ELİF", "KAYA", "ISPARTA", cat: "Açık Kategori");
        Assert.Equal("BURDUR", One("KAYA, ELİF", null, "10 Yaş", kid, adult).Province);
    }

    [Fact]
    public void EachRegistrationUsedOnce_BestFirst()
    {
        // Tam eşleşen sporcu kaydı alır; ikinci adı eksik olan aynı kayda bağlanmaz.
        var players = new[] { new RosterPlayer("AVCI, MEHMET ARİF", 1412), new RosterPlayer("AVCI, MEHMET", 1000) };
        var res = ProvinceMatcher.MatchAll(players, new[] { R("MEHMET ARİF", "AVCI", "ISPARTA", 1412) });
        Assert.Equal(MatchKind.Exact, res[0].Kind);
        Assert.Equal(MatchKind.NotFound, res[1].Kind);
    }

    [Fact]
    public void PersonKey_OrderAndCommaInsensitive()
        => Assert.Equal(ProvinceMatcher.PersonKey("ÇETİNKAYA, AYŞE NUR"), ProvinceMatcher.PersonKey("Ayşe Nur Çetinkaya"));

    [Theory]
    [InlineData("ISPARTA", "ISPARTA")]
    [InlineData("istanbul", "İSTANBUL")]
    [InlineData("AFYON", "AFYONKARAHİSAR")]
    [InlineData("IGDIR", "IĞDIR")]
    [InlineData("K.MARAŞ", "KAHRAMANMARAŞ")]
    [InlineData("Lefkoşa", "LEFKOŞA")]
    public void Provinces_Print(string input, string expected) => Assert.Equal(expected, Provinces.Print(input));

    [Theory]
    [InlineData("12 Yaş ve Altı", 12)]
    [InlineData("U10", 10)]
    [InlineData("Açık Kategori", null)]
    public void Age(string cat, int? age) => Assert.Equal(age, ProvinceMatcher.Age(cat));

    [Theory]
    [InlineData("AFYONKARAHİSAR", "AFYON")]
    [InlineData("Kahramanmaraş", "K.MARAŞ")]
    [InlineData("ISPARTA", "ISPARTA")]   // kısa adı olmayan il olduğu gibi
    public void Provinces_ShortForm(string input, string expected) => Assert.Equal(expected, Provinces.ShortForm(input));

    // ---------------- form ↔ turnuva eşleştirmesi ----------------
    [Theory]
    [InlineData("Isparta 29 Ekim Cumhuriyet Bayramı Satranç Turnuvası", "Isparta 29 Ekim Cumhuriyet Bayramı Satranç Turnuvası Açık Kategori", 1.0)]
    [InlineData("Isparta 29 Ekim Cumhuriyet Bayramı Satranç Turnuvası", "Isparta Ekim Ayı UKD Satranç Turnuvası", 0.0)]   // il + ay ortak, yine de farklı
    [InlineData("Ankara 29 Ekim Cumhuriyet Bayramı Hızlı Satranç Turnuvası - 8 Yaş ve Altı Kategorisi", "Ankara 29 Ekim Cumhuriyet Bayramı Hızlı Satranç Turnuvası", 1.0)]
    public void TitleScore_IgnoresProvinceMonthAndCategoryWords(string form, string ev, double expected)
        => Assert.Equal(expected, RegistrationMatch.TitleScore(form, ev), 2);

    [Fact]
    public void IsLikely_PlayersDecide_TitleOnlyWithoutRoster()
    {
        Assert.True(RegistrationMatch.IsLikely(0.0, 30, 44, 40));    // kayıtlıların çoğu listede
        Assert.False(RegistrationMatch.IsLikely(0.0, 5, 44, 10));    // aynı yerel sporcular, başka turnuva (Ekim UKD)
        Assert.True(RegistrationMatch.IsLikely(1.0, 6, 44, 9));      // ad birebir + birkaç sporcu ortak
        Assert.False(RegistrationMatch.IsLikely(1.0, 0, 44, 9));     // ad aynı ama hiç ortak sporcu yok
        Assert.True(RegistrationMatch.IsLikely(0.6, 0, 0, 0));       // sporcu listesi yok: ada bakılır
        Assert.False(RegistrationMatch.IsLikely(0.3, 0, 0, 0));
    }

    [Fact]
    public void Overlap_CountsRegistrantsInRoster()
    {
        var roster = new[] { new RosterPlayer("AVCI, MEHMET ARİF", 1412), new RosterPlayer("DEMİRER, ELİF"), new RosterPlayer("KAYA, ALİ") };
        var regs = new[] { R("MEHMET ARİF", "AVCI", "ISPARTA"), R("ELİF", "DEMİRER", "ISPARTA"), R("VELİ", "ŞAHİN", "BURDUR") };
        Assert.Equal(2, RegistrationMatch.Overlap(roster, regs));
        Assert.Equal(0, RegistrationMatch.Overlap(Array.Empty<RosterPlayer>(), regs));
    }
}
