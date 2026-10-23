using NotasyonOtomasyonu.Core;
using UglyToad.PdfPig;

namespace NotasyonOtomasyonu.Online;

/// <summary>Bir alanın PDF sayfasındaki bir satırlık kutusu (punto, sol üst köşe başlangıçlı).</summary>
public sealed record FieldBox(string Key, int Page, double X, double Y, double W, double H, bool Placeholder);

/// <summary>
/// Düzenleme modunda doldurulup Word ile PDF'e çevrilmiş belgede alanların yerini bulur: harflerin
/// renk kodu (<see cref="FieldMarker"/>) hangi alana ait olduklarını söyler; aynı satırdaki harfler
/// tek kutuda birleşir (alan birkaç satıra yayılırsa her satır ayrı kutu).
/// </summary>
public static class PdfFieldLocator
{
    public static List<FieldBox> Locate(string pdfPath, IReadOnlyDictionary<int, string> keys)
    {
        var boxes = new List<FieldBox>();
        using var doc = PdfDocument.Open(pdfPath);
        foreach (var page in doc.GetPages())
        {
            double ph = page.Height;
            var hits = new List<(int Id, bool Ph, double L, double R, double B, double T, double Base, double Size)>();
            foreach (var l in page.Letters)
            {
                if (l.Color is null || string.IsNullOrWhiteSpace(l.Value)) continue;
                var (r, g, b) = l.Color.ToRGBValues();
                var code = FieldMarker.Decode((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
                if (code is null || !keys.ContainsKey(code.Value.Id)) continue;
                var gr = l.BoundingBox;
                hits.Add((code.Value.Id, code.Value.Placeholder, gr.Left, gr.Right, gr.Bottom, gr.Top, l.StartBaseLine.Y, Math.Max(l.PointSize, 4)));
            }
            foreach (var byId in hits.GroupBy(h => h.Id))
            {
                // Satırlara ayır: taban çizgisi yarım punto içinde olanlar aynı satır.
                var lines = new List<List<(int Id, bool Ph, double L, double R, double B, double T, double Base, double Size)>>();
                foreach (var h in byId.OrderByDescending(h => h.Base).ThenBy(h => h.L))
                {
                    var line = lines.FirstOrDefault(ln => Math.Abs(ln[0].Base - h.Base) < h.Size * 0.5);
                    if (line is null) lines.Add(line = new());
                    line.Add(h);
                }
                foreach (var ln in lines)
                {
                    double size = ln.Max(h => h.Size);
                    double left = ln.Min(h => h.L), right = ln.Max(h => h.R);
                    // Yükseklik: yazı puntosuna göre (büyük/küçük harf farkı kutuyu oynatmasın).
                    double baseY = ln[0].Base, top = baseY + size * 0.8, bottom = baseY - size * 0.25;
                    boxes.Add(new FieldBox(keys[byId.Key], page.Number - 1, left, ph - top, right - left, top - bottom, ln[0].Ph));
                }
            }
        }
        return boxes;
    }
}
