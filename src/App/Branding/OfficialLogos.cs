using System.Drawing.Drawing2D;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Branding;

/// <summary>
/// Resmi logolar: T.C. Gençlik ve Spor Bakanlığı logosu ve TSF il temsilciliği logosu. 81 il logosu
/// gömülmez (her biri ~750 KB): hepsi aynı TSF logosudur, yalnız alt yaydaki yazı ("1991" yerine il adı)
/// değişir. Gömülü taban logo (alt yazısı silinmiş) üzerine il adı, resmi logolardaki gibi Arial Bold
/// (Helvetica Bold ile aynı ölçüler) harflerle, tabanı dışa bakacak şekilde alt yaya çizilir.
/// </summary>
public static class OfficialLogos
{
    // Resmi il logolarından ölçülen yerleşim (logo çapı 1000 birim; merkez 500):
    // harf tabanı r = 467, büyük harf yüksekliği 62, harf aralığı ek boşluksuz, yazı 90°'de (altta) ortalı.
    private const float BaseRadius = 467f, CapHeight = 62f, MaxSpanDeg = 130f;
    private const string FontFamilyName = "Arial";
    private const string FoundingYear = "1991";

    private static readonly object Gate = new();
    private static readonly Dictionary<(string, int), Bitmap> Cache = new();
    private static Bitmap? _tsfBase, _gsb;

    /// <summary>T.C. Gençlik ve Spor Bakanlığı yuvarlak amblemi, yazısız (kopyası; çağıran dispose eder).</summary>
    public static Bitmap Gsb()
    {
        lock (Gate) return new Bitmap(_gsb ??= Load("gsb.png"));
    }

    /// <summary>GSB amblemi tam <paramref name="px"/> pikselde, kademeli küçültülmüş (ekranda keskin dursun).</summary>
    public static Bitmap Gsb(int px)
    {
        lock (Gate) return Downscale(_gsb ??= Load("gsb.png"), Math.Clamp(px, 16, 2400));
    }

    /// <summary>
    /// Uygulama logosu: beyaz daire içinde yeşil at (♞) — yazı tipindeki glif vektör olarak tam
    /// <paramref name="px"/> pikselde çizilir, hiçbir DPI'da bulanıklaşmaz.
    /// </summary>
    public static Bitmap AppLogo(int px)
    {
        px = Math.Clamp(px, 16, 1024);
        var bmp = new Bitmap(px, px, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using (var white = new SolidBrush(Color.White)) g.FillEllipse(white, 0.5f, 0.5f, px - 1.5f, px - 1.5f);
        using var family = FontFamily.Families.FirstOrDefault(f => f.Name == "Segoe UI Symbol") ?? new FontFamily("Segoe UI");
        using var path = new GraphicsPath();
        path.AddString("\u265E", family, (int)FontStyle.Regular, px, PointF.Empty, StringFormat.GenericTypographic);
        var b = path.GetBounds();
        float s = px * 0.66f / Math.Max(b.Width, b.Height);
        using var m = new Matrix();
        m.Translate(px / 2f, px / 2f);
        m.Scale(s, s);
        m.Translate(-(b.X + b.Width / 2), -(b.Y + b.Height / 2));
        path.Transform(m);
        using var green = new SolidBrush(Color.FromArgb(95, 122, 70));
        g.FillPath(green, path);
        return bmp;
    }

    /// <summary>
    /// Büyük resmi yarıya yarıya küçültüp son adımı yüksek kaliteyle yapar: tek seferde 20–40 kat
    /// küçültmede GDI+ ince çizgileri bulanıklaştırıp tırtıklı bırakıyordu.
    /// </summary>
    private static Bitmap Downscale(Image src, int px)
    {
        Image cur = src;
        bool owned = false;
        while (cur.Width / 2 >= px * 2)
        {
            var half = new Bitmap(cur.Width / 2, cur.Height / 2, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(half))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(cur, 0, 0, half.Width, half.Height);
            }
            if (owned) cur.Dispose();
            cur = half;
            owned = true;
        }
        int h = (int)Math.Round(px * (double)cur.Height / cur.Width);
        var outBmp = new Bitmap(px, Math.Max(1, h), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(outBmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            using var attrs = new System.Drawing.Imaging.ImageAttributes();
            attrs.SetWrapMode(WrapMode.TileFlipXY); // kenarda yarı saydam hale olmasın
            g.DrawImage(cur, new Rectangle(0, 0, outBmp.Width, outBmp.Height), 0, 0, cur.Width, cur.Height, GraphicsUnit.Pixel, attrs);
        }
        if (owned) cur.Dispose();
        return outBmp;
    }

    /// <summary>
    /// TSF il logosu (<paramref name="px"/>×<paramref name="px"/>, saydam zemin; kopyası, çağıran dispose eder).
    /// İl tanınmazsa ya da boşsa genel TSF logosu (alt yazısı "1991").
    /// </summary>
    public static Bitmap Tsf(string? province, int px = 600)
    {
        var text = LogoText(province);
        px = Math.Clamp(px, 32, 2400);
        lock (Gate)
        {
            if (!Cache.TryGetValue((text, px), out var bmp))
            {
                if (Cache.Count > 24) { foreach (var b in Cache.Values) b.Dispose(); Cache.Clear(); }
                Cache[(text, px)] = bmp = Render(text, px);
            }
            return new Bitmap(bmp);
        }
    }

    /// <summary>Logonun en/boy oranı (ikisi de yuvarlak: GSB yazısız amblemi ve TSF logosu → 1).</summary>
    public static float Aspect(string kind)
    {
        if (kind != "gsb") return 1f;
        lock (Gate) { var b = _gsb ??= Load("gsb.png"); return (float)b.Width / b.Height; }
    }

    /// <summary>Logonun alt yayına yazılacak metin: il adı Türkçe büyük harfle; il yoksa kuruluş yılı.</summary>
    public static string LogoText(string? province)
        => Provinces.Normalize(province) is { } p ? Provinces.Print(p) : FoundingYear;

    /// <summary>Logo başlığı (araç ipucu vb.): "TSF Isparta İl Temsilciliği" / "Türkiye Satranç Federasyonu".</summary>
    public static string Caption(string? province)
        => Provinces.Normalize(province) is { } p ? $"TSF {p} İl Temsilciliği" : "Türkiye Satranç Federasyonu";

    private static Bitmap Render(string text, int px)
    {
        Bitmap bmp;
        lock (Gate) bmp = Downscale(_tsfBase ??= Load("tsf_base.png"), px); // taban kademeli küçültülür
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        float k = px / 1000f;
        DrawArcText(g, text, new PointF(px / 2f, px / 2f), BaseRadius * k, CapHeight * k);
        return bmp;
    }

    /// <summary>
    /// Metni dairenin alt yayına çizer: soldan sağa okunur, harflerin tabanı dışa (r = <paramref name="baseRadius"/>),
    /// üstü merkeze bakar. Harf aralıkları yay boyunca harfin orta yüksekliğinde ölçülür.
    /// </summary>
    internal static void DrawArcText(Graphics g, string text, PointF center, float baseRadius, float capHeight)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var family = new FontFamily(FontFamilyName);
        const FontStyle style = FontStyle.Bold;
        // Arial'da büyük harf yüksekliği ≈ 0,716 em: em boyutu büyük harf yüksekliğinden bulunur.
        float em = capHeight / CapRatio(family, style);
        float ascent = em * family.GetCellAscent(style) / family.GetEmHeight(style);
        using var font = new Font(family, em, style, GraphicsUnit.Pixel);
        using var fmt = (StringFormat)StringFormat.GenericTypographic.Clone();
        fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

        var adv = text.Select(ch => g.MeasureString(ch.ToString(), font, PointF.Empty, fmt).Width).ToArray();
        float mid = baseRadius - capHeight / 2;
        float total = adv.Sum();
        // Çok uzun ad (olağan illerde olmaz): yayın taşmaması için orantılı küçült.
        float span = total / mid * 180f / MathF.PI;
        if (span > MaxSpanDeg)
        {
            float s = MaxSpanDeg / span;
            DrawArcText(g, text, center, baseRadius, capHeight * s);
            return;
        }

        using var brush = new SolidBrush(Color.FromArgb(29, 29, 27));
        float pos = -total / 2;
        for (int i = 0; i < text.Length; i++)
        {
            float at = pos + adv[i] / 2;
            float theta = 90f - at / mid * 180f / MathF.PI; // soldan sağa açı azalır (y aşağı)
            float rad = theta * MathF.PI / 180f;
            using var path = new GraphicsPath();
            path.AddString(text[i].ToString(), family, (int)style, em, PointF.Empty, fmt);
            using var m = new Matrix();
            m.Translate(center.X + baseRadius * MathF.Cos(rad), center.Y + baseRadius * MathF.Sin(rad));
            m.Rotate(theta - 90f);
            m.Translate(-adv[i] / 2, -ascent);
            path.Transform(m);
            g.FillPath(brush, path);
            pos += adv[i];
        }
    }

    /// <summary>Yazı tipinin büyük harf yüksekliği / em oranı ("H" glifinin ölçülen yüksekliği).</summary>
    private static float CapRatio(FontFamily family, FontStyle style)
    {
        using var p = new GraphicsPath();
        p.AddString("H", family, (int)style, 1000f, PointF.Empty, StringFormat.GenericTypographic);
        var h = p.GetBounds().Height / 1000f;
        return h > 0.3f && h < 1f ? h : 0.716f;
    }

    private static Bitmap Load(string name)
    {
        using var s = typeof(OfficialLogos).Assembly.GetManifestResourceStream("NotasyonOtomasyonu.App.logos." + name)
                      ?? throw new InvalidOperationException("Gömülü logo bulunamadı: " + name);
        using var img = Image.FromStream(s);
        return new Bitmap(img);
    }
}
