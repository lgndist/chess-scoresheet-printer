using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.Tests;

/// <summary>"Önizle ve Düzenle": alanların renk koduyla işaretlenmesi (sayfada yerlerinin bulunması için).</summary>
public class EditorMarkTests
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

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

    private static XDocument Xml(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx));
        using var s = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(s);
    }

    /// <summary>Metni verilen koşunun rengi (yoksa null).</summary>
    private static string? ColorOf(XDocument x, string text)
        => x.Descendants(W + "r").FirstOrDefault(r => string.Concat(r.Descendants(W + "t").Select(t => t.Value)) == text)
            ?.Element(W + "rPr")?.Element(W + "color")?.Attribute(W + "val")?.Value;

    [Theory]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(511)]
    [InlineData(4095)]
    public void Color_RoundTrips_AndStaysNearBlackOrGray(int id)
    {
        foreach (var ph in new[] { false, true })
        {
            var hex = FieldMarker.ColorHex(id, ph);
            int r = Convert.ToInt32(hex[..2], 16), g = Convert.ToInt32(hex[2..4], 16), b = Convert.ToInt32(hex[4..], 16);
            Assert.True(ph ? r is >= 0x90 and <= 0x9F : r <= 15);   // gözle siyah / açık gri
            Assert.Equal((id, ph), FieldMarker.Decode(r, g, b));
        }
        Assert.Null(FieldMarker.Decode(0, 0, 0));        // belgenin kendi siyah yazısı
        Assert.Null(FieldMarker.Decode(200, 30, 30));    // renkli başlık
    }

    [Fact]
    public void Capacity_Overflow_IsUnmarked_NeverCollides()
    {
        var m = new FieldMarker(k => k);
        for (int i = 0; i < FieldMarker.MaxFields; i++) Assert.Equal(i + 1, m.IdOf("k" + i));
        Assert.Equal(0, m.IdOf("fazla"));                     // sınır aşıldı: kod yok
        Assert.Null(FieldMarker.Decode(0, 0, 0));                 // → hiçbir alana çözülmez
        Assert.Equal(FieldMarker.MaxFields, m.Keys.Count);
    }

    [Fact]
    public void Tokens_SplitIntoMarkedRuns_StaticTextKeepsFormat()
    {
        var docx = Docx("<w:p><w:r><w:rPr><w:b/><w:sz w:val=\"22\"/></w:rPr><w:t>İli: {{IL}} / İlçesi: {{ILCE}}.</w:t></w:r></w:p>");
        var data = new ReportData();
        data.Values["IL"] = "ISPARTA";
        data.Values["ILCE"] = "";
        var marker = new FieldMarker(k => $"[{k}]");
        var x = Xml(DocxTemplate.Fill(docx, data, marker));

        var texts = x.Descendants(W + "r").Select(r => string.Concat(r.Descendants(W + "t").Select(t => t.Value))).ToList();
        Assert.Equal(new[] { "İli: ", "ISPARTA", " / İlçesi: ", "[ILCE]", "." }, texts);
        Assert.Null(ColorOf(x, "İli: "));                                                    // sabit metin işaretsiz
        Assert.Equal(FieldMarker.ColorHex(marker.IdOf("IL"), false), ColorOf(x, "ISPARTA"));
        Assert.Equal(FieldMarker.ColorHex(marker.IdOf("ILCE"), true), ColorOf(x, "[ILCE]")); // boş → gri yer tutucu
        Assert.All(x.Descendants(W + "r"), r => Assert.NotNull(r.Element(W + "rPr")!.Element(W + "b"))); // kalınlık korunur

        // w:color şema sırasına uygun: w:b'den sonra, w:sz'den önce (Word sıraya duyarlı)
        var rPr = x.Descendants(W + "r").First(r => r.Descendants(W + "t").Any(t => t.Value == "ISPARTA")).Element(W + "rPr")!;
        Assert.Equal(new[] { "b", "color", "sz" }, rPr.Elements().Select(e => e.Name.LocalName));
        Assert.Equal(2, marker.Keys.Count);
    }

    [Fact]
    public void Bookmarks_ContentControls_LabelCells_AreMarked()
    {
        var docx = Docx(
            "<w:p><w:bookmarkStart w:id=\"1\" w:name=\"YER\"/><w:r><w:t>Eski Salon</w:t></w:r><w:bookmarkEnd w:id=\"1\"/></w:p>" +
            "<w:sdt><w:sdtPr><w:tag w:val=\"TELEFON\"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>…</w:t></w:r></w:p></w:sdtContent></w:sdt>" +
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>SİSTEM</w:t></w:r></w:p></w:tc><w:tc><w:p/></w:tc></w:tr></w:tbl>");
        var data = new ReportData();
        data.Values["YER"] = "Gençlik Merkezi";
        data.Values["TELEFON"] = "";
        data.Values["SISTEM"] = "İsviçre 6 Tur";
        var marker = new FieldMarker(k => $"[{k}]");
        var x = Xml(DocxTemplate.Fill(docx, data, marker));

        Assert.Equal(FieldMarker.ColorHex(marker.IdOf("YER"), false), ColorOf(x, "Gençlik Merkezi"));
        Assert.Equal(FieldMarker.ColorHex(marker.IdOf("TELEFON"), true), ColorOf(x, "[TELEFON]"));
        Assert.Equal(FieldMarker.ColorHex(marker.IdOf("SISTEM"), false), ColorOf(x, "İsviçre 6 Tur"));
        Assert.Null(ColorOf(x, "SİSTEM")); // etiket hücresi kilitli metin
    }

    [Fact]
    public void WithoutMarker_OutputHasNoColorsOrPlaceholders()
    {
        var docx = Docx("<w:p><w:r><w:t>İli: {{IL}} Tel: {{TELEFON}}</w:t></w:r></w:p>");
        var data = new ReportData();
        data.Values["IL"] = "ISPARTA";
        var x = Xml(DocxTemplate.Fill(docx, data));
        Assert.Empty(x.Descendants(W + "color"));
        Assert.Equal("İli: ISPARTA Tel: ", string.Concat(x.Descendants(W + "t").Select(t => t.Value)));
    }

    [Fact]
    public void LabelCells_HeaderFlaggedKunyeRowFilled_ListHeaderKept()
    {
        var docx = Docx(
            // Kullanıcının künyesi: ilk satır Word'de "başlık satırı" işaretli olabilir — yine doldurulmalı
            "<w:tbl><w:tr><w:trPr><w:tblHeader/></w:trPr><w:tc><w:p><w:r><w:t>İLİ</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>Eski</w:t></w:r></w:p></w:tc></w:tr></w:tbl>" +
            // Liste tablosu: başlıktaki bilinmeyen sütun adı (KULÜBÜ) il değeriyle ezilmemeli
            "<w:tbl><w:tr><w:tc><w:p><w:r><w:t>İLİ</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>KULÜBÜ</w:t></w:r></w:p></w:tc></w:tr>" +
            "<w:tr><w:tc><w:p><w:r><w:t>{{siralama.il}}</w:t></w:r></w:p></w:tc><w:tc><w:p><w:r><w:t>{{siralama.ad}}</w:t></w:r></w:p></w:tc></w:tr></w:tbl>");
        var data = new ReportData();
        data.Values["IL"] = "ISPARTA";
        data.Lists["siralama"] = new() { new() { ["il"] = "BURDUR", ["ad"] = "ALİ KAYA" } };
        var texts = Xml(DocxTemplate.Fill(docx, data)).Descendants(W + "t").Select(t => t.Value).Where(v => v.Length > 0).ToList();
        Assert.Equal(new[] { "İLİ", "ISPARTA", "İLİ", "KULÜBÜ", "BURDUR", "ALİ KAYA" }, texts);
    }
}
