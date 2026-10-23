using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NotasyonOtomasyonu.Core;

/// <summary>Bir rapor/yönerge belgesine yazılacak veriler.</summary>
public sealed class ReportData
{
    /// <summary>Tekil alanlar: "IL", "YER", "TARIH_ARALIGI" … (büyük/küçük harf duyarsız).</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Kategori tablosu satırları: "adi", "kriteri".</summary>
    public List<Dictionary<string, string>> Categories { get; } = new();
    /// <summary>Program tablosu satırları: "tarih", "saat", "etkinlik".</summary>
    public List<Dictionary<string, string>> Program { get; } = new();
    /// <summary>
    /// Diğer tablolar: önek → satırlar ({{il.adi}} → Lists["il"]). Satırda "kategori" sütunu varsa
    /// aynı kategorinin ardışık satırlarında o hücre dikey birleştirilir.
    /// </summary>
    public Dictionary<string, List<Dictionary<string, string>>> Lists { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Belgede bulunan doldurulabilir yerler.</summary>
public sealed record TemplateAnalysis(
    IReadOnlyList<string> Fields,          // {{ALAN}} işaretleri ve etiketten tanınan alanlar
    bool HasCategoryTable,                 // {{kategori.…}} satırı
    bool HasProgramTable,                  // {{program.…}} satırı
    IReadOnlyList<string> LabelFields)     // işaretsiz, "İLİ | …" gibi etiketten tanınanlar
{
    /// <summary>Etiketli hücrelerde belgede zaten yazan değerler (kullanıcının kendi yönergesi).</summary>
    public IReadOnlyDictionary<string, string> Existing { get; init; } = new Dictionary<string, string>();
    /// <summary>Kategori/program dışındaki tablo önekleri ({{il.adi}} → "il").</summary>
    public IReadOnlyList<string> Lists { get; init; } = Array.Empty<string>();
}

/// <summary>
/// .docx şablon motoru (Word gerekmez; belge XML'i doğrudan işlenir).
///  • <c>{{ALAN}}</c> işaretleri doldurulur. Word işareti birkaç parçaya bölmüş olsa da paragraf
///    birleştirilerek tanınır.
///  • <c>{{kategori.adi}}</c> / <c>{{kategori.kriteri}}</c> içeren tablo satırı her kategori için,
///    <c>{{program.tarih}}</c> / <c>{{program.saat}}</c> / <c>{{program.etkinlik}}</c> içeren satır her
///    program kalemi için çoğaltılır; aynı günün tarih hücreleri dikey birleştirilir.
///  • İşaret içermeyen belgelerde "İLİ", "YERİ", "SİSTEM", "DÜŞÜNME SÜRESİ" gibi etiket hücrelerinin
///    yanındaki hücre bilinen değerle doldurulur (kullanıcının kendi yönergesi).
/// </summary>
public static class DocxTemplate
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly Regex Token = new(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}");

    /// <summary>Etiket (katlanmış yazım) → alan. Etiket hücresinin sağındaki hücre doldurulur.</summary>
    public static readonly IReadOnlyDictionary<string, string> LabelMap = new Dictionary<string, string>
    {
        ["turnuva adi"] = "TURNUVA_ADI", ["ili"] = "IL", ["il"] = "IL", ["ilcesi"] = "ILCE", ["ilce"] = "ILCE",
        ["baslama-bitis tarihi"] = "TARIH_ARALIGI", ["baslama - bitis tarihi"] = "TARIH_ARALIGI",
        ["tarih"] = "TARIH_ARALIGI", ["tarihi"] = "TARIH_ARALIGI", ["turnuva tarihi"] = "TARIH_ARALIGI",
        ["yeri"] = "YER", ["yer"] = "YER", ["son basvuru tarihi"] = "SON_BASVURU",
        ["sistem"] = "SISTEM", ["sistemi"] = "SISTEM", ["dusunme suresi"] = "DUSUNME_SURESI",
        ["organizasyon"] = "ORGANIZASYON", ["turnuva direktoru"] = "DIREKTOR", ["yarisma direktoru"] = "DIREKTOR",
        ["bashakem"] = "BASHAKEM", ["bas hakem"] = "BASHAKEM", ["hakemler"] = "HAKEMLER", ["hakem"] = "HAKEMLER",
        ["tur sayisi"] = "TUR_SAYISI",
    };

    /// <summary>Tablo başlıklarında geçen, değer olmayan sözcükler.</summary>
    private static readonly HashSet<string> HeaderWords = new()
    {
        "saat", "program", "kategori adi", "kategori kriteri", "kategori", "tur", "sira", "no",
        "sporcu sayisi", "sayi", "puan", "rtg", "elo", "ukd", "rating", "ad soyad", "derece", "oynanan tur", "il", "ili"
    };

    private static readonly Regex ListToken = new(@"\{\{\s*[A-Za-z0-9_]+\.");

    private static IEnumerable<string> Parts(ZipArchive zip)
        => zip.Entries.Select(e => e.FullName)
              .Where(n => n == "word/document.xml" || Regex.IsMatch(n, @"^word/(header|footer)\d*\.xml$"));

    public static TemplateAnalysis Analyze(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var fields = new List<string>();
        var labels = new List<string>();
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool cats = false, prog = false;
        var lists = new List<string>();
        foreach (var name in Parts(zip))
        {
            var x = Load(zip, name);
            NormalizeParagraphs(x);
            foreach (var t in x.Descendants(W + "t"))
                foreach (Match m in Token.Matches(t.Value))
                {
                    var key = m.Groups[1].Value;
                    if (key.StartsWith("kategori.", StringComparison.OrdinalIgnoreCase)) cats = true;
                    else if (key.StartsWith("program.", StringComparison.OrdinalIgnoreCase)) prog = true;
                    else if (key.Contains('.')) { var pre = key[..key.IndexOf('.')]; if (!lists.Contains(pre, StringComparer.OrdinalIgnoreCase)) lists.Add(pre); }
                    else if (!fields.Contains(key, StringComparer.OrdinalIgnoreCase)) fields.Add(key);
                }
            // Word yer imleri ve içerik denetimleri (adı/etiketi alan anahtarı ya da bilinen etiket)
            foreach (var (key, current) in Bookmarks(x).Select(b => (b.Key, b.Text)).Concat(ContentControls(x).Select(c => (c.Key, c.Text))))
            {
                if (!fields.Contains(key, StringComparer.OrdinalIgnoreCase)) fields.Add(key);
                if (!existing.ContainsKey(key) && Regex.IsMatch(current, @"[\p{L}\d]{2,}")) existing[key] = current;
            }
            foreach (var (key, cell) in LabelCells(x))
            {
                if (!labels.Contains(key) && !fields.Contains(key, StringComparer.OrdinalIgnoreCase)) labels.Add(key);
                var v = CellText(cell);
                if (!existing.ContainsKey(key) && Regex.IsMatch(v, @"[\p{L}\d]{2,}")) existing[key] = v; // "…", "-" değer sayılmaz
            }
        }
        return new TemplateAnalysis(fields.Concat(labels).ToList(), cats, prog, labels) { Existing = existing, Lists = lists };
    }

    /// <param name="marker">Düzenleme önizlemesi: alanlar renk koduyla işaretlenir, boşlar yer tutucuyla görünür (bkz. <see cref="FieldMarker"/>).</param>
    public static byte[] Fill(byte[] docx, ReportData data, FieldMarker? marker = null)
    {
        var input = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var output = new MemoryStream();
        using (var outZip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var parts = Parts(input).ToHashSet();
            foreach (var entry in input.Entries)
            {
                var target = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var ws = target.Open();
                if (parts.Contains(entry.FullName))
                {
                    var x = Load(input, entry.FullName);
                    NormalizeParagraphs(x);
                    // Etiketli hücreler satırlar çoğaltılmadan ÖNCE bulunur: liste tablosu başlıkları ("İL | SPORCU SAYISI")
                    // alttaki {{il.…}} satırından tanınıp atlanabilsin.
                    var labelCells = LabelCells(x).ToList();
                    ExpandRows(x, "kategori.", data.Categories, mergeDateKey: null, marker);
                    ExpandRows(x, "program.", data.Program, mergeDateKey: "tarih", marker);
                    foreach (var (prefix, rows) in data.Lists)
                        ExpandRows(x, prefix + ".", rows, rows.Any(r => r.ContainsKey("kategori")) ? "kategori" : null, marker);
                    ReplaceTokens(x, data.Values, marker);
                    FillBookmarks(x, data.Values, marker);
                    FillContentControls(x, data.Values, marker);
                    FillLabelCells(labelCells, data.Values, marker);
                    using var w = new StreamWriter(ws, new UTF8Encoding(false));
                    x.Save(w, SaveOptions.DisableFormatting);
                }
                else
                {
                    using var rs = entry.Open();
                    rs.CopyTo(ws);
                }
            }
        }
        input.Dispose();
        return output.ToArray();
    }

    private static XDocument Load(ZipArchive zip, string name)
    {
        using var s = zip.GetEntry(name)!.Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    /// <summary>Bir paragrafta parçalara bölünmüş {{…}} işaretini tek metin düğümünde birleştirir.</summary>
    private static void NormalizeParagraphs(XDocument x)
    {
        foreach (var p in x.Descendants(W + "p").ToList())
        {
            var ts = p.Descendants(W + "t").ToList();
            if (ts.Count < 2) continue;
            var all = string.Concat(ts.Select(t => t.Value));
            if (!all.Contains("{{")) continue;
            bool split = Token.Matches(all).Cast<Match>().Any(m => !ts.Any(t => t.Value.Contains(m.Value)));
            if (!split && !Regex.IsMatch(all, @"\{\{[^}]*$|^[^{]*\}\}")) continue;
            ts[0].Value = all;
            Preserve(ts[0]);
            foreach (var t in ts.Skip(1)) t.Value = "";
        }
    }

    private static void Preserve(XElement t) => t.SetAttributeValue(XNamespace.Xml + "space", "preserve");

    /// <summary>
    /// "{{prefix…}}" içeren tablo satırını her öğe için çoğaltır. <paramref name="mergeDateKey"/> sütunu
    /// ardışık satırlarda aynıysa (program günü, kategori) hücre dikey birleştirilir. Düzenleme modunda
    /// her hücre "önek[satır].sütun" anahtarıyla işaretlenir (ör. "program[2].saat").
    /// </summary>
    private static void ExpandRows(XDocument x, string prefix, List<Dictionary<string, string>> items, string? mergeDateKey, FieldMarker? marker = null)
    {
        string list = prefix.TrimEnd('.');
        foreach (var row in x.Descendants(W + "tr").ToList())
        {
            var text = string.Concat(row.Descendants(W + "t").Select(t => t.Value));
            if (!text.Contains("{{" + prefix, StringComparison.OrdinalIgnoreCase)) continue;

            string? prevDate = null;
            XElement anchor = row;
            for (int index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var clone = new XElement(row);
                string? date = mergeDateKey is not null && item.TryGetValue(mergeDateKey, out var d) ? d : null;
                bool continueDay = date is not null && date == prevDate;
                foreach (var tc in clone.Elements(W + "tc"))
                {
                    var cellText = string.Concat(tc.Descendants(W + "t").Select(t => t.Value));
                    bool isDateCell = mergeDateKey is not null && cellText.Contains("{{" + prefix + mergeDateKey, StringComparison.OrdinalIgnoreCase);
                    if (isDateCell)
                    {
                        var vm = tc.Element(W + "tcPr")?.Element(W + "vMerge");
                        if (vm is not null)
                        {
                            if (continueDay) vm.SetAttributeValue(W + "val", null);
                            else vm.SetAttributeValue(W + "val", "restart");
                        }
                    }
                    foreach (var t in tc.Descendants(W + "t").ToList())
                    {
                        if (marker is not null && t.Parent?.Name == W + "r" && t.Value.Contains("{{"))
                        {
                            int rowNo = index;
                            bool merged = isDateCell && continueDay;
                            SplitMarked(t, key =>
                            {
                                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return ("{{" + key + "}}", null);
                                if (merged) return ("", null); // birleşik hücrenin devamı
                                var col = key[prefix.Length..];
                                var v = item.TryGetValue(col, out var s) ? s : "";
                                var mark = $"{list}[{rowNo}].{col}";
                                bool empty = string.IsNullOrWhiteSpace(v);
                                return (empty ? marker.Placeholder(mark) : v, FieldMarker.ColorHex(marker.IdOf(mark), empty));
                            });
                            continue;
                        }
                        t.Value = Token.Replace(t.Value, m =>
                        {
                            var key = m.Groups[1].Value;
                            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return m.Value;
                            if (isDateCell && continueDay) return "";
                            return item.TryGetValue(key[prefix.Length..], out var v) ? v : "";
                        });
                    }
                }
                anchor.AddAfterSelf(clone);
                anchor = clone;
                prevDate = date;
            }
            row.Remove();
        }
    }

    private static void ReplaceTokens(XDocument x, IReadOnlyDictionary<string, string> values, FieldMarker? marker)
    {
        foreach (var t in x.Descendants(W + "t").ToList())
        {
            if (!t.Value.Contains("{{")) continue;
            if (marker is not null && t.Parent?.Name == W + "r")
            {
                SplitMarked(t, key => Display(key, values, marker)!.Value);
                continue;
            }
            t.Value = Token.Replace(t.Value, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
            Preserve(t);
        }
    }

    /// <summary>
    /// Düzenleme modu: "İli: {{IL}} / {{ILCE}}" metnini koşulara böler; her alanın değeri (boşsa yer
    /// tutucu) kendi renk koduyla ayrı koşuda, sabit metin özgün biçimiyle kalır.
    /// </summary>
    private static void SplitMarked(XElement t, Func<string, (string Text, string? Color)> resolve)
    {
        var run = t.Parent!;
        var rPr = run.Element(W + "rPr");
        var text = t.Value;
        var runs = new List<XElement>();
        int pos = 0;
        foreach (Match m in Token.Matches(text))
        {
            if (m.Index > pos) runs.Add(MakeRun(rPr, text[pos..m.Index], null));
            var (shown, color) = resolve(m.Groups[1].Value);
            runs.Add(MakeRun(rPr, shown, color));
            pos = m.Index + m.Length;
        }
        if (pos < text.Length) runs.Add(MakeRun(rPr, text[pos..], null));
        run.AddAfterSelf(runs);
        t.Remove();
        if (!run.Elements().Any(e => e.Name != W + "rPr")) run.Remove();
    }

    private static XElement MakeRun(XElement? rPr, string text, string? color)
    {
        var r = new XElement(W + "r", rPr is null ? null : new XElement(rPr));
        var t = new XElement(W + "t", text);
        Preserve(t);
        r.Add(t);
        if (color is not null) SetColor(r, color);
        return r;
    }

    /// <summary>Alanın gösterilecek yazısı ve rengi; işaretleyici yoksa boş alan atlanır (null).</summary>
    private static (string Text, string? Color)? Display(string key, IReadOnlyDictionary<string, string> values, FieldMarker? marker)
    {
        var v = values.TryGetValue(key, out var s) ? s : "";
        bool empty = string.IsNullOrWhiteSpace(v);
        if (marker is null) return empty ? null : (v, null);
        return (empty ? marker.Placeholder(key) : v, FieldMarker.ColorHex(marker.IdOf(key), empty));
    }

    // w:rPr çocuklarının şema sırası: w:color bunlardan sonra gelmeli (Word sıraya duyarlı).
    private static readonly HashSet<string> BeforeColor = new()
    {
        "rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline",
        "shadow", "emboss", "imprint", "noProof", "snapToGrid", "vanish", "webHidden"
    };

    /// <summary>Koşunun yazı rengini ayarlar (rPr içinde şema sırasına uygun yere).</summary>
    private static void SetColor(XElement run, string hex)
    {
        var rPr = run.Element(W + "rPr");
        if (rPr is null) { rPr = new XElement(W + "rPr"); run.AddFirst(rPr); }
        rPr.Element(W + "color")?.Remove();
        var color = new XElement(W + "color", new XAttribute(W + "val", hex));
        var after = rPr.Elements().LastOrDefault(e => BeforeColor.Contains(e.Name.LocalName));
        if (after is null) rPr.AddFirst(color); else after.AddAfterSelf(color);
    }

    private static void ColorRuns(IEnumerable<XElement> runs, string? color)
    {
        if (color is null) return;
        foreach (var r in runs) SetColor(r, color);
    }

    // ================= Yer imleri / içerik denetimleri =================

    /// <summary>
    /// Yer imi ya da içerik denetimi adından alan anahtarı: "TURNUVA_ADI", "turnuva_adi" → TURNUVA_ADI;
    /// "İli", "Başhakem" gibi etiketler etiket tablosundan; Word'ün iç yer imleri ("_GoBack") yok sayılır.
    /// </summary>
    public static string? KeyFor(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('_')) return null;
        var f = Fold(name.Replace('_', ' ')).Trim(' ', ':');
        if (LabelMap.TryGetValue(f, out var k)) return k;
        var key = Regex.Replace(Fold(name).ToUpperInvariant(), @"[^A-Z0-9]+", "_").Trim('_');
        return key.Length == 0 ? null : key;
    }

    /// <summary>Yer imleri: (anahtar, başlangıç, aynı paragraftaki koşular, şu anki metin).</summary>
    private static IEnumerable<(string Key, XElement Start, List<XElement> Runs, string Text)> Bookmarks(XDocument x)
    {
        foreach (var b in x.Descendants(W + "bookmarkStart").ToList())
        {
            var key = KeyFor((string?)b.Attribute(W + "name"));
            if (key is null) continue;
            var id = (string?)b.Attribute(W + "id");
            var runs = new List<XElement>();
            foreach (var e in b.ElementsAfterSelf())
            {
                if (e.Name == W + "bookmarkEnd" && (string?)e.Attribute(W + "id") == id) break;
                if (e.Name == W + "r") runs.Add(e);
            }
            yield return (key, b, runs, string.Concat(runs.Descendants(W + "t").Select(t => t.Value)).Trim());
        }
    }

    private static void FillBookmarks(XDocument x, IReadOnlyDictionary<string, string> values, FieldMarker? marker)
    {
        foreach (var (key, start, runs, _) in Bookmarks(x).ToList())
        {
            if (Display(key, values, marker) is not var (v, color)) continue;
            var ts = runs.SelectMany(r => r.Elements(W + "t")).ToList();
            if (ts.Count > 0)
            {
                ts[0].Value = v; Preserve(ts[0]);
                foreach (var t in ts.Skip(1)) t.Value = "";
                ColorRuns(runs, color);
                continue;
            }
            // Boş (nokta) yer imi: önceki koşunun biçimiyle yeni koşu eklenir.
            var rPr = start.ElementsBeforeSelf(W + "r").LastOrDefault()?.Element(W + "rPr");
            start.AddAfterSelf(MakeRun(rPr, v, color));
        }
    }

    /// <summary>İçerik denetimleri (w:sdt): etiketi (tag) ya da başlığı (alias) alan anahtarı olanlar.</summary>
    private static IEnumerable<(string Key, XElement Sdt, string Text)> ContentControls(XDocument x)
    {
        foreach (var sdt in x.Descendants(W + "sdt").ToList())
        {
            var pr = sdt.Element(W + "sdtPr");
            var key = KeyFor((string?)pr?.Element(W + "tag")?.Attribute(W + "val"))
                      ?? KeyFor((string?)pr?.Element(W + "alias")?.Attribute(W + "val"));
            if (key is null) continue;
            bool placeholder = pr?.Element(W + "showingPlcHdr") is not null;
            var text = placeholder ? "" : string.Concat(sdt.Element(W + "sdtContent")?.Descendants(W + "t").Select(t => t.Value) ?? Array.Empty<string>()).Trim();
            yield return (key, sdt, text);
        }
    }

    private static void FillContentControls(XDocument x, IReadOnlyDictionary<string, string> values, FieldMarker? marker)
    {
        foreach (var (key, sdt, _) in ContentControls(x).ToList())
        {
            if (Display(key, values, marker) is not var (v, color)) continue;
            var content = sdt.Element(W + "sdtContent");
            if (content is null) continue;
            sdt.Element(W + "sdtPr")?.Element(W + "showingPlcHdr")?.Remove(); // artık yer tutucu metin değil
            foreach (var style in content.Descendants(W + "rStyle").Where(s => ((string?)s.Attribute(W + "val") ?? "").Contains("Placeholder", StringComparison.OrdinalIgnoreCase)).ToList())
                style.Remove();
            var ts = content.Descendants(W + "t").ToList();
            if (ts.Count > 0)
            {
                ts[0].Value = v; Preserve(ts[0]);
                foreach (var t in ts.Skip(1)) t.Value = "";
                ColorRuns(content.Descendants(W + "r"), color);
                continue;
            }
            var run = MakeRun(null, v, color);
            var p = content.Descendants(W + "p").FirstOrDefault();
            if (p is not null) p.Add(run); else content.Add(run);
        }
    }

    /// <summary>Tablolarda "etiket | değer" çiftleri: etiket bilinen bir alansa (alan, değer hücresi).</summary>
    private static IEnumerable<(string Key, XElement ValueCell)> LabelCells(XDocument x)
    {
        foreach (var row in x.Descendants(W + "tr"))
        {
            // Liste tablosunun başlığı: alttaki satır {{il.…}} gibi liste işaretleri taşıyor.
            if (row.ElementsAfterSelf(W + "tr").FirstOrDefault() is { } below &&
                ListToken.IsMatch(string.Concat(below.Descendants(W + "t").Select(t => t.Value)))) continue;
            var cells = row.Elements(W + "tc").ToList();
            for (int i = 0; i + 1 < cells.Count; i++)
            {
                var label = Fold(CellText(cells[i])).Trim(' ', ':', ' ');
                if (!LabelMap.TryGetValue(label, out var key)) continue;
                var next = CellText(cells[i + 1]);
                if (next.Contains("{{")) continue; // işaretli şablon: işaret doldurur
                // Tablo başlık satırı ("TARİH | SAAT | PROGRAM"): yan hücre de başlıksa değer değildir.
                var nextFold = Fold(next).Trim(' ', ':');
                if (LabelMap.ContainsKey(nextFold) || HeaderWords.Contains(nextFold)) continue;
                yield return (key, cells[i + 1]);
            }
        }
    }

    private static void FillLabelCells(List<(string Key, XElement ValueCell)> labelCells, IReadOnlyDictionary<string, string> values, FieldMarker? marker)
    {
        foreach (var (key, cell) in labelCells)
        {
            if (Display(key, values, marker) is not var (v, color)) continue;
            var ts = cell.Descendants(W + "t").ToList();
            if (ts.Count == 0)
            {
                var p = cell.Element(W + "p") ?? new XElement(W + "p");
                if (p.Parent is null) cell.Add(p);
                p.Add(MakeRun(null, v, color));
                continue;
            }
            ts[0].Value = v; Preserve(ts[0]);
            foreach (var t in ts.Skip(1)) t.Value = "";
            ColorRuns(cell.Descendants(W + "r"), color);
        }
    }

    private static string CellText(XElement tc) => string.Concat(tc.Descendants(W + "t").Select(t => t.Value)).Trim();

    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(ch switch
            {
                'ç' or 'Ç' => 'c', 'ğ' or 'Ğ' => 'g', 'ı' or 'I' or 'İ' or 'i' => 'i',
                'ö' or 'Ö' => 'o', 'ş' or 'Ş' => 's', 'ü' or 'Ü' => 'u', _ => char.ToLowerInvariant(ch)
            });
        return Regex.Replace(sb.ToString(), @"\s+", " ");
    }
}
