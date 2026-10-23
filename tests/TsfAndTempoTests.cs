using System.IO.Compression;
using System.Text;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>TSF il siteleri, yönerge okuyucu, resmi süre önerici, yer imi/içerik denetimi, hakem listesi.</summary>
public class TsfAndTempoTests
{
    // ================= Tempo =================
    [Theory]
    [InlineData(90, 30, Tempo.Klasik)]
    [InlineData(45, 30, Tempo.Klasik)]   // 45 + 30 = 75 dk ≥ 60 → klasik
    [InlineData(35, 30, Tempo.Klasik)]   // 65 dk
    [InlineData(15, 10, Tempo.Hizli)]
    [InlineData(10, 5, Tempo.Hizli)]     // 15 dk
    [InlineData(10, 0, Tempo.Yildirim)]  // ≤ 10 dk
    [InlineData(3, 2, Tempo.Yildirim)]
    public void Tempo_FollowsFideDefinitions(int minutes, int inc, Tempo expected)
        => Assert.Equal(expected, TimeControls.Classify(minutes, inc));

    [Fact]
    public void Tempo_Presets_ParseAndFormat()
    {
        Assert.Equal((90, 30), TimeControls.Presets(Tempo.Klasik)[0]);
        Assert.Equal((3, 2), TimeControls.Presets(Tempo.Yildirim)[0]);
        Assert.Equal((35, 30), TimeControls.Parse("35'+30'' Eklemeli Tempo"));
        Assert.Equal((35, 30), TimeControls.Parse("35 dakika + hamle başına 30 saniye eklemeli tempo"));
        Assert.Equal((90, 30), TimeControls.Parse(TimeControls.Format(90, 30)));
        Assert.Equal((15, 0), TimeControls.Parse("15 DAKİKA"));
        Assert.Equal(Tempo.Klasik, TimeControls.FromLabel("Zaman kontrolü (Standart)"));
        Assert.Equal(Tempo.Hizli, TimeControls.FromLabel("Zaman kontrolü (Rapid)"));
        Assert.Equal(Tempo.Yildirim, TimeControls.FromLabel("Time control (Blitz)"));
        Assert.Equal("90'+30''", TimeControls.Short(90, 30));
    }

    // ================= TSF siteleri =================
    [Fact]
    public void TsfSites_All81Provinces_WithOverrides()
    {
        Assert.Equal(81, TsfSites.All.Count);
        Assert.Equal("https://isparta.tsf.org.tr", TsfSites.SiteFor("Isparta"));
        Assert.Equal("https://afyon.tsf.org.tr", TsfSites.SiteFor("Afyonkarahisar"));
        Assert.Equal("https://istanbul.tsf.org.tr", TsfSites.SiteFor("İstanbul"));
        Assert.Equal("https://ozel.example", TsfSites.SiteFor("Isparta", new Dictionary<string, string> { ["Isparta"] = "https://ozel.example/" }));
        Assert.Null(TsfSites.SiteFor(null));
        Assert.True(Provinces.List.Skip(1).All(p => TsfSites.SiteFor(p) is not null)); // her ilin sitesi var
    }

    [Fact]
    public void TsfSites_FindsYonergeLinks_SkipsRegulations()
    {
        const string html = """
            <a href="/images/ISPARTA_EKM_AYI_UKD_SATRAN_TURNUVASI_YNERGES.pdf">Tıklayınız</a>
            <a href="/images/2026/Il/Ankara_Eyll_Ay_UKD_Satran_Turnuvas.pdf">Ankara Eylül Ayı UKD Satranç Turnuvası</a>
            <a href="/images/tsf-tk-yarisma-yonergeleri-hazirlama-ve-uygulama-proseduru.pdf">Prosedür</a>
            <a href="/images/kayit_listesi.pdf">Kayıt listesi</a>
            <a href="https://tsf.org.tr/images/stories/mhk/kural.pdf">Kurallar</a>
            <a href="/images/yonerge.docx">Kupa Turnuvası Yönergesi</a>
            """;
        var docs = TsfSites.ParseDocuments(html, "https://isparta.tsf.org.tr");
        Assert.Equal(3, docs.Count);
        Assert.Equal("ISPARTA EKİM AYI UKD SATRANÇ TURNUVASI YÖNERGESİ", docs[0].Title); // dosya adından, harfler onarılmış
        Assert.Equal("https://isparta.tsf.org.tr/images/ISPARTA_EKM_AYI_UKD_SATRAN_TURNUVASI_YNERGES.pdf", docs[0].Url);
        Assert.True(docs[2].IsWord);
        Assert.True(TsfSites.MatchScore(docs[0], "Isparta Ekim Ayı UKD Satranç Turnuvası Açık Kategori") >= 0.5);
        Assert.True(TsfSites.MatchScore(docs[1], "Isparta Ekim Ayı UKD Satranç Turnuvası") < 0.5);
    }

    // ================= Yönerge okuyucu =================
    private const string LeftColumn =
        "1. TURNUVA BİLGİLERİ\n" +
        "1.1. Örnekil GSİM & Örnekil Satranç İl Temsilciliği tarafından,\n" +
        "Örnekil ili, Merkez ilçesinde, 03-04.10.2026 tarihleri\n" +
        "arasında, Gençlik Merkezi Satranç Salonu'nda, İsviçre sistem ile, 35\n" +
        "dakika 30 saniye eklemeli zaman temposu kullanılarak\n" +
        "Turnuva düzenlenecektir. Turnuvaya başvurular 02.10.2026 Saat\n" +
        "18:00’den önce tamamlanmalıdır.\n" +
        "2.3. Başvurular ornekil.tsf.org.tr adresinde yer alan\n" +
        "4. TURNUVA PROGRAMI\n" +
        "09.00-09.30 Kayıt Kontrol Başlangıç-Bitiş\n" +
        "09.30 Teknik Toplantı\n" +
        "09.50 1. Tur Eşleştirmesinin İlanı";
    private const string RightColumn =
        "5. İLETİŞİM\n" +
        "5.1. Yarışma Direktörü: Ayşe Yılmaz\n" +
        "Telefon Numarası: +90 555 123 45 67\n" +
        "E-posta: ornekiltsf.org.tr\n" +
        "6. KATEGORİLER";

    [Fact]
    public void Extract_ReadsTsfStandardYonerge()
    {
        var d = YonergeExtractor.Extract("ÖRNEKİL EKİM AYI SATRANÇ TURNUVASI YÖNERGESİ\n" + LeftColumn + "\n" + RightColumn);
        Assert.Equal("ÖRNEKİL EKİM AYI SATRANÇ TURNUVASI", d["TURNUVA_ADI"]);
        Assert.Equal("Örnekil GSİM & Örnekil Satranç İl Temsilciliği", d["ORGANIZASYON"]);
        Assert.Equal("Örnekil", d["IL"]);
        Assert.Equal("Merkez", d["ILCE"]);
        Assert.Equal("03-04.10.2026", d["TARIH_ARALIGI"]);
        Assert.Equal("Gençlik Merkezi Satranç Salonu", d["YER"]);
        Assert.Equal("BİREYSEL İSVİÇRE SİSTEMİ", d["SISTEM"]);
        Assert.Equal("35 DAKİKA + HAMLE BAŞINA 30 SANİYE EKLEMELİ TEMPO", d["DUSUNME_SURESI"]);
        Assert.Equal("02.10.2026 18.00", d["SON_BASVURU"]);
        Assert.Equal("https://ornekil.tsf.org.tr", d["BASVURU_ADRESI"]);
        Assert.Equal("Ayşe Yılmaz", d["DIREKTOR"]);
        Assert.Equal("+90 555 123 45 67", d["TELEFON"]);
        Assert.Equal("ornekil@tsf.org.tr", d["EPOSTA"]);              // PDF'te düşen "@" onarılır
        Assert.Equal("09.00-09.30", d["PROGRAM_KAYIT"]);
        Assert.Equal("09.30", d["PROGRAM_TEKNIK"]);
        Assert.Equal("09.50", d["PROGRAM_ILAN"]);
    }

    [Theory]
    [InlineData("aydintsf.org.tr", "aydin@tsf.org.tr")]
    [InlineData("selmayakut591gmail.com", "selmayakut591@gmail.com")]
    [InlineData("a.b@gsb.org.tr", "a.b@gsb.org.tr")]
    public void FixEmail_RestoresAt(string raw, string expected) => Assert.Equal(expected, YonergeExtractor.FixEmail(raw));

    /// <summary>İki sütunlu (TSF düzeni) gerçek bir PDF: sütunlar karışmadan okunmalı.</summary>
    [Fact]
    public void PdfText_ReadsTwoColumnsSeparately()
    {
        NotasyonOtomasyonu.Render.QuestPdfRenderer.EnsureInitialized();
        var pdf = Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20);
            page.DefaultTextStyle(t => t.FontFamily("DejaVu Sans").FontSize(9));
            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text("ÖRNEKİL EKİM AYI SATRANÇ TURNUVASI YÖNERGESİ").Bold().FontSize(12);
                col.Item().PaddingTop(10).Row(row =>
                {
                    row.RelativeItem().PaddingRight(14).Text(LeftColumn);
                    row.RelativeItem().PaddingLeft(14).Text(RightColumn);
                });
            });
        })).GeneratePdf();

        var text = YonergeExtractor.PdfText(pdf);
       
        Assert.Equal("ÖRNEKİL EKİM AYI SATRANÇ TURNUVASI YÖNERGESİ", YonergeExtractor.Title(text));
        var d = YonergeExtractor.Extract(text);
        Assert.Equal("Gençlik Merkezi Satranç Salonu", d["YER"]);       // sağ sütun karışmadı
        Assert.Equal("Ayşe Yılmaz", d["DIREKTOR"]);
        Assert.Equal("+90 555 123 45 67", d["TELEFON"]);
        Assert.Equal("02.10.2026 18.00", d["SON_BASVURU"]);
    }

    // ================= Kaynak önceliği =================
    private static ReportDraft Draft(ReportSources sources, string templateFile = "turnuva_yonergesi_sablon.docx")
    {
        var info = new Dictionary<string, string>
        {
            ["Turnuva direktoru"] = "Durmus, Ramazan 6333532",
            ["Başhakem"] = "NA Kutdemir, Mehmet 34570535",
            ["Başhakem Yardımcısı"] = "FA Deneme, Kişi",
            ["Zaman kontrolü (Rapid)"] = "",
            ["Yer"] = "Gençlik Merkezi",
            ["Tarih"] = "2026/10/03 - 2026/10/04",
            ["Organizatör(ler)"] = "Örnekil GSİM",
        };
        var cats = new List<(CategoryRef, int, TournamentSystem)> { (new CategoryRef(1, "Açık", true), 5, TournamentSystem.Swiss) };
        var tpl = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", templateFile));
        return ReportBuilder.Build(info, new List<(int, DateTime?, string)>(), cats, "Örnekil Ekim Turnuvası", "Isparta",
                                   DocxTemplate.Analyze(tpl), sources);
    }

    private static ReportField F(ReportDraft d, string key) => d.Fields.Single(f => f.Key == key);

    [Fact]
    public void Sources_SameEventYonerge_FillsDeadline_AndFixesSpelling()
    {
        var yon = new Dictionary<string, string> { ["SON_BASVURU"] = "02.10.2026 18.00", ["DIREKTOR"] = "Ramazan Durmuş", ["TELEFON"] = "+90 555" };
        var d = Draft(new ReportSources { Yonerge = yon, YonergeSameEvent = true });
        Assert.Equal("02.10.2026 18.00", F(d, "SON_BASVURU").Value);
        Assert.Equal("TSF yönergesi", F(d, "SON_BASVURU").Origin);
        Assert.Equal("Ramazan Durmuş", F(d, "DIREKTOR").Value);       // chess-results "DURMUS" → yönergedeki yazım
        Assert.Equal("NA Mehmet KUTDEMİR", F(d, "BASHAKEM").Value);    // "Başhakem Yardımcısı" ile karışmadı
        Assert.Equal("Örnekil GSİM", F(d, "ORGANIZASYON").Value);      // chess-results "Organizatör(ler)"
        Assert.Equal(Tempo.Hizli, d.Tempo);
        Assert.Equal("15 DAKİKA + HAMLE BAŞINA 10 SANİYE EKLEMELİ TEMPO", F(d, "DUSUNME_SURESI").Value); // tempoya göre resmi öneri
    }

    [Fact]
    public void Sources_OtherEventYonerge_OnlyStableFields_EventMemoryWins()
    {
        var yon = new Dictionary<string, string> { ["SON_BASVURU"] = "01.01.2020 17.00", ["TELEFON"] = "+90 555", ["PROGRAM_TEKNIK"] = "09.30" };
        var d = Draft(new ReportSources
        {
            Yonerge = yon, YonergeSameEvent = false,
            EventMemory = new Dictionary<string, string> { ["TELEFON"] = "+90 999" }
        });
        Assert.Equal(ReportFieldSource.Missing, F(d, "SON_BASVURU").Source); // başka turnuvanın tarihi önerilmez
        Assert.Equal("+90 999", F(d, "TELEFON").Value);                     // bu turnuvada girilen kazanır
        Assert.Equal("bu turnuvada girilen", F(d, "TELEFON").Origin);
        Assert.Equal("09.30", F(d, "PROGRAM_TEKNIK").Value);
    }

    [Fact]
    public void Tutanak_Template_GetsMeetingFieldsAndFills()
    {
        var d = Draft(new ReportSources { Yonerge = new Dictionary<string, string> { ["PROGRAM_TEKNIK"] = "09.30" }, YonergeSameEvent = true },
                      "teknik_toplanti_tutanagi_sablon.docx");
        Assert.Equal("03.10.2026", F(d, "TOPLANTI_TARIHI").Value);
        Assert.Equal("09.30", F(d, "TOPLANTI_SAATI").Value);   // programdaki teknik toplantı saati
        Assert.Equal("Gençlik Merkezi", F(d, "TOPLANTI_YERI").Value);
        Assert.True(d.HasCategoryTable && d.HasProgramTable);

        var tpl = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "teknik_toplanti_tutanagi_sablon.docx"));
        var outDoc = DocxTemplate.Fill(tpl, d.ToReportData());
        using var zip = new ZipArchive(new MemoryStream(outDoc));
        using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var xml = r.ReadToEnd();
        Assert.Contains("NA Mehmet KUTDEMİR", xml);
        Assert.DoesNotContain("{{TOPLANTI", xml);
        using var s = new StreamReader(zip.GetEntry("word/settings.xml")!.Open());
        Assert.DoesNotContain("documentProtection", s.ReadToEnd()); // tutanak düzenlenebilir
    }

    // ================= Yer imi / içerik denetimi =================
    private static byte[] Docx(string body)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("word/document.xml").Open(), new UTF8Encoding(false));
            w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>"
                    + body + "</w:body></w:document>");
        }
        return ms.ToArray();
    }

    private static string Xml(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var r = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        return r.ReadToEnd();
    }

    [Fact]
    public void Bookmarks_AndContentControls_AreFilled_FormattingKept()
    {
        var docx = Docx(
            // dolu yer imi (eski değer + biçim), boş (nokta) yer imi, Word'ün iç yer imi
            "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"YER\"/><w:r><w:rPr><w:b/></w:rPr><w:t>Eski Salon</w:t></w:r><w:bookmarkEnd w:id=\"1\"/></w:p>" +
            "<w:p><w:r><w:rPr><w:i/></w:rPr><w:t>Tel: </w:t></w:r><w:bookmarkStart w:id=\"2\" w:name=\"telefon\"/><w:bookmarkEnd w:id=\"2\"/></w:p>" +
            "<w:p><w:bookmarkStart w:id=\"3\" w:name=\"_GoBack\"/><w:bookmarkEnd w:id=\"3\"/></w:p>" +
            // içerik denetimleri: etiket ile, başlık (alias) ile ve yer tutucu metinli
            "<w:sdt><w:sdtPr><w:tag w:val=\"TURNUVA_ADI\"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Ad</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:p><w:sdt><w:sdtPr><w:alias w:val=\"Başhakem\"/><w:showingPlcHdr/></w:sdtPr><w:sdtContent><w:r><w:rPr><w:rStyle w:val=\"PlaceholderText\"/></w:rPr><w:t>Metin girmek için tıklayın</w:t></w:r></w:sdtContent></w:sdt></w:p>");

        var a = DocxTemplate.Analyze(docx);
        Assert.Contains("YER", a.Fields);
        Assert.Contains("TELEFON", a.Fields);
        Assert.Contains("TURNUVA_ADI", a.Fields);
        Assert.Contains("BASHAKEM", a.Fields);
        Assert.DoesNotContain(a.Fields, f => f.Contains("GOBACK"));
        Assert.Equal("Eski Salon", a.Existing["YER"]);
        Assert.False(a.Existing.ContainsKey("BASHAKEM"));       // yer tutucu metin değer sayılmaz

        var data = new ReportData();
        data.Values["YER"] = "Yeni Salon";
        data.Values["TELEFON"] = "+90 555";
        data.Values["TURNUVA_ADI"] = "EKİM KUPASI";
        data.Values["BASHAKEM"] = "IA Ali VELİ";
        var xml = Xml(DocxTemplate.Fill(docx, data));
        Assert.Contains("<w:b />", xml);                          // dolu yer iminin kalın biçimi korunur
        Assert.Contains("Yeni Salon", xml);
        Assert.DoesNotContain("Eski Salon", xml);
        Assert.Contains("+90 555", xml);
        Assert.Contains("<w:i />", xml);                          // boş yer imine önceki koşunun biçimi
        Assert.Contains("EKİM KUPASI", xml);
        Assert.Contains("IA Ali VELİ", xml);
        Assert.DoesNotContain("showingPlcHdr", xml);
        Assert.DoesNotContain("PlaceholderText", xml);
    }

    // ================= Hakemler =================
    [Fact]
    public void Arbiters_FromInfo_WithRolesAndGrades()
    {
        var info = new Dictionary<string, string>
        {
            ["Turnuva direktoru"] = "Duygu Nur Sapaz; Rümeysa Kafalı",
            ["Başhakem"] = "FA İbrahim Yılmaz",
            ["Başhakem Yardımcısı"] = "NA Adem Seyfullah Koçoğlu",
            ["Hakem"] = "NA Haluk Terlemez; Bayar, Umut Can; IA Kutdemir, Mehmet 34570535",
        };
        var list = Arbiters.FromInfo(info);
        Assert.Equal(7, list.Count);
        Assert.Equal(new ArbiterEntry("İbrahim YILMAZ", "Başhakem", "FA"), list[0]);
        Assert.Equal(new ArbiterEntry("Adem Seyfullah KOÇOĞLU", "Başhakem Yardımcısı", "NA"), list[1]);
        Assert.Equal(new ArbiterEntry("Umut Can BAYAR", "Hakem", null), list[3]);
        Assert.Equal(new ArbiterEntry("Mehmet KUTDEMİR", "Hakem", "IA"), list[4]);
        Assert.Equal("Turnuva Direktörü", list[^1].Role);
        Assert.Equal(Arbiters.Uluslararasi, Arbiters.GradeFromTitle("IA"));
        Assert.Equal(Arbiters.Fide, Arbiters.GradeFromTitle("FA"));
        Assert.Equal(Arbiters.Ulusal, Arbiters.GradeFromTitle("NA"));
        Assert.Null(Arbiters.GradeFromTitle(null));
        Assert.Equal(Arbiters.Grades.Length, Arbiters.Grades.Select(Arbiters.ColorFor).Distinct().Count()); // her derece ayrı renk
    }

    [Theory]
    [InlineData("Başhakem", true, "Başhakem")]
    [InlineData("BAŞHAKEM", true, "Başhakem")]
    [InlineData("Başhakem Yardımcısı", true, "Hakem")]
    [InlineData("Turnuva Direktörü", true, "Hakem")]
    [InlineData("Saha Hakemi", true, "Hakem")]
    [InlineData(null, true, "Hakem")]
    [InlineData("Başhakem Yardımcısı", false, "Başhakem Yardımcısı")]
    public void Arbiters_PrintedRole_SimpleOrDetailed(string? role, bool simple, string expected)
        => Assert.Equal(expected, Arbiters.PrintedRole(role, simple));
}
