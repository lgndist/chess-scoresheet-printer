using NotasyonOtomasyonu.App.Branding;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Kartta bir logonun yeri (punto, sayfa koordinatı). <paramref name="Kind"/>: "gsb" ya da "tsf".</summary>
public sealed record LogoPlacement(string Kind, RectangleF Rect);

/// <summary>
/// Kartın resmi logoları ve yerleşimi: T.C. Gençlik ve Spor Bakanlığı (GSB) ve TSF il logosu (il adı
/// <paramref name="Province"/>; yoksa genel TSF logosu). <paramref name="Mode"/> hazır yerleşimlerden biri
/// (<see cref="Modes"/>) ya da "Custom": logolar kart tasarımı ekranında elle taşınmıştır (<paramref name="Spots"/>).
/// </summary>
public sealed record CardBranding(bool Gsb, bool Tsf, string? Province, string Mode = CardBranding.Auto, float Scale = 1f,
                                  bool Backing = true, IReadOnlyDictionary<string, LogoSpot>? Spots = null)
{
    public const string Auto = "Auto", HeaderSides = "HeaderSides", SeamCenter = "SeamCenter", SeamSides = "SeamSides",
                        TopCorners = "TopCorners", BottomCorners = "BottomCorners", Custom = "Custom";

    /// <summary>Hazır yerleşimler (tasarım ekranındaki sırayla) ve adları.</summary>
    public static readonly (string Mode, string Label)[] Modes =
    {
        (HeaderSides, "Başlıkta iki yanda"),
        (SeamCenter, "Afiş bitişinde, ortada"),
        (SeamSides, "Afiş bitişinde, iki yanda"),
        (TopCorners, "Üst köşelerde"),
        (BottomCorners, "Alt köşelerde"),
    };

    public static readonly CardBranding None = new(false, false, null);

    /// <summary>Tek logo varken anlamı aynı olan yerleşim (iki yanda → ortada); bu kip galeride gizlenir.</summary>
    public static bool SameAsCenterWhenSingle(string mode) => mode == SeamSides;

    public bool Any => Gsb || Tsf;

    /// <summary>Kayıtlı ayarlardan; il Ayarlar'dan gelir (<see cref="AppConfig.LogoProvince"/>; null = genel TSF logosu).</summary>
    public static CardBranding From(CardConfig c, string? province)
        => new(c.ShowGsbLogo, c.ShowTsfLogo, province, string.IsNullOrWhiteSpace(c.LogoLayout) ? Auto : c.LogoLayout,
               (float)Math.Clamp(c.LogoScale, 0.5, 1.6), c.LogoBacking, c.LogoSpots);

    /// <summary>"Auto"nun karşılığı: afiş yoksa başlıkta iki yanda, varsa afiş bitişinde ortada.</summary>
    public string EffectiveMode(bool hasPoster) => Mode == Auto ? (hasPoster ? SeamCenter : HeaderSides) : Mode;

    /// <summary>Yerleşimin adı; tek logoda iki yana yayılan kipler ortaya geldiği için adları da öyle.</summary>
    public static string Label(string mode, bool single = false) => mode switch
    {
        Custom => "Elle yerleştirilmiş",
        HeaderSides when single => "Başlıkta, ortada",
        SeamSides when single => "Afiş bitişinde, ortada",
        TopCorners when single => "Üstte, ortada",
        BottomCorners when single => "Altta, ortada",
        _ => Modes.FirstOrDefault(m => m.Mode == mode).Label ?? "Otomatik"
    };

    public bool Single => Gsb ^ Tsf;

    /// <summary>Logoları yükler (çağıran dispose eder); TSF logosu <paramref name="tsfPx"/> piksel kare.</summary>
    public (Image? Gsb, Image? Tsf) Load(int tsfPx)
        => (Gsb ? OfficialLogos.Gsb() : null, Tsf ? OfficialLogos.Tsf(Province, tsfPx) : null);

    public static string SpotKey(string kind, bool landscape) => kind + (landscape ? ":L" : ":P");

    /// <summary>Hazır yerleşimlerde logo yüksekliği (afiş bitişi / köşeler).</summary>
    public float BaseHeight(CardLayout l) => (l.Landscape ? 0.20f : 0.13f) * l.PageH * Scale;

    // ================= Yerleşim =================
    /// <summary>Logoların kart üzerindeki yerleri (soldan sağa: GSB, TSF).</summary>
    public List<LogoPlacement> Place(CardLayout l, string mode)
    {
        var kinds = new List<string>();
        if (Gsb) kinds.Add("gsb");
        if (Tsf) kinds.Add("tsf");
        var result = new List<LogoPlacement>();
        if (kinds.Count == 0) return result;
        float m = CardLayout.Margin, h = BaseHeight(l), gap = 14;
        float W(string k, float hh) => hh * OfficialLogos.Aspect(k);
        bool single = kinds.Count == 1;
        // Tek logo: iki yana yayılan kipler logoyu yatayda ortalar (ad, çizgi ve kategori de ortalı kalır).
        float X(string k, float ww, float left, float right) => single ? l.PageW / 2 - ww / 2 : k == "gsb" ? left : right - ww;

        switch (mode)
        {
            case HeaderSides:
            {
                var area = l.LogoRect;
                if (l.HasPoster && single) return Place(l, SeamCenter); // afişin ortasını kapatmasın
                float hh, y;
                if (l.HasPoster) // afişin iki yanında, olağan boyutta, afiş yüksekliğinde ortalı
                {
                    hh = Math.Min(h, l.SeamY - area.Y);
                    y = area.Y + (l.SeamY - area.Y - hh) / 2;
                }
                else
                {
                    // Dikey kartta ya da tek logoda ad logoların altına gelir; logo alanın üst kısmını kaplar.
                    bool stacked = !l.Landscape || single;
                    hh = Math.Min(area.Height, (stacked ? area.Height * 0.58f : area.Height) * Math.Min(Scale, 1f));
                    y = area.Y;
                }
                foreach (var k in kinds)
                    result.Add(new(k, new RectangleF(X(k, W(k, hh), area.X, area.Right), y, W(k, hh), hh)));
                break;
            }
            case SeamCenter:
            {
                float total = kinds.Sum(k => W(k, h)) + gap * (kinds.Count - 1);
                float x = l.PageW / 2 - total / 2;
                foreach (var k in kinds) { result.Add(new(k, new RectangleF(x, l.SeamY - h / 2, W(k, h), h))); x += W(k, h) + gap; }
                break;
            }
            case SeamSides:
                foreach (var k in kinds)
                    result.Add(new(k, new RectangleF(X(k, W(k, h), m, l.PageW - m), l.SeamY - h / 2, W(k, h), h)));
                break;
            case TopCorners:
            case BottomCorners:
            {
                float hh = h * 0.7f;
                float y = mode == TopCorners ? m * 0.6f : l.PageH - m * 0.6f - hh;
                foreach (var k in kinds)
                    result.Add(new(k, new RectangleF(X(k, W(k, hh), m * 0.6f, l.PageW - m * 0.6f), y, W(k, hh), hh)));
                break;
            }
            default: // Custom: kayıtlı yer; kaydı olmayan logo afiş bitişinde ortadaki yerine konur
            {
                var fallback = Place(l, SeamCenter);
                foreach (var k in kinds)
                {
                    if (Spots is not null && Spots.TryGetValue(SpotKey(k, l.Landscape), out var s) && s.H > 0)
                    {
                        float hh = (float)s.H * l.PageH, ww = W(k, hh);
                        result.Add(new(k, new RectangleF((float)s.X * l.PageW - ww / 2, (float)s.Y * l.PageH - hh / 2, ww, hh)));
                    }
                    else result.Add(fallback.First(p => p.Kind == k));
                }
                break;
            }
        }
        return result.Select(p => p with { Rect = ClampToPage(p.Rect, l) }).ToList();
    }

    private static RectangleF ClampToPage(RectangleF r, CardLayout l)
    {
        float w = Math.Min(r.Width, l.PageW), h = Math.Min(r.Height, l.PageH);
        return new RectangleF(Math.Clamp(r.X, 0, l.PageW - w), Math.Clamp(r.Y, 0, l.PageH - h), w, h);
    }

    // ================= Çizim =================
    /// <summary>Logoları çizer; <see cref="Backing"/> açıksa arkalarına beyaz daire.</summary>
    public void Draw(Graphics g, IEnumerable<LogoPlacement> logos, Image? gsb, Image? tsf)
    {
        foreach (var p in logos)
        {
            var img = p.Kind == "gsb" ? gsb : tsf;
            if (img is null) continue;
            if (Backing) DrawBacking(g, p);
            g.DrawImage(img, CardLayout.FitImage(new SizeF(img.Width, img.Height), p.Rect));
        }
    }

    /// <summary>İki logo da yuvarlak: arkalarına biraz taşan beyaz daire (GSB amblemi şeffaf zeminli, payı biraz geniş).</summary>
    private static void DrawBacking(Graphics g, LogoPlacement p)
    {
        using var white = new SolidBrush(Color.White);
        float pad = p.Rect.Height * (p.Kind == "gsb" ? 0.045f : 0.025f);
        g.FillEllipse(white, RectangleF.Inflate(p.Rect, pad, pad));
    }
}
