namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Basılacak bir kategori kartı (adet kadar sayfa); <paramref name="Color"/> kategorinin tema rengi.</summary>
public sealed record CardSpec(string Category, Color Color, int Copies);

/// <summary>
/// A4 masa kartının ölçüleri (punto). Varsayılan yatay. Üstte turnuva afişi, resmi logolar (GSB + TSF il)
/// ya da turnuva adı; altta büyük kategori adı — stile göre beyaz zeminde tema renginde yazı ya da tema
/// renginde zeminde beyaz yazı. Üst bölüm içeriği kadar yer kaplar (en çok sayfanın %60'ı): içerik sayfanın
/// başından başlar, kalan yükseklik kategori bandına kalır (eskiden sabit %60'lık alanda dikey ortalandığı
/// için kağıdın üstünde boşluk kalıyordu). Önizleme, yazıcı ve PDF aynı hesabı kullanır.
/// </summary>
public sealed class CardLayout
{
    public const float A4Short = 595.28f, A4Long = 841.89f;
    public const float Margin = 28f;
    public const float MaxHeaderRatio = 0.60f, MinHeaderRatio = 0.22f;
    private const float NameMaxPt = 60;          // logosuz başlıkta turnuva adı
    private const float OfficialNameMaxPt = 40;  // logolar arasındaki turnuva adı
    private const float LogoGap = 18;

    public bool Landscape { get; }
    public float PageW => Landscape ? A4Long : A4Short;
    public float PageH => Landscape ? A4Short : A4Long;

    /// <summary>Üst bölümün (afiş/logo/ad) yüksekliği; altı kategori bandıdır.</summary>
    public float HeaderH { get; }

    public CardLayout(bool landscape, float? headerH = null)
    {
        Landscape = landscape;
        HeaderH = Math.Clamp(headerH ?? PageH * MaxHeaderRatio, PageH * MinHeaderRatio, PageH * MaxHeaderRatio);
    }

    /// <summary>Afiş bitişi: afişin alt kenarı (afiş yoksa başlık ile kategori bandının sınırı).</summary>
    public float SeamY { get; private set; }

    /// <summary>Üst bölümde afiş var mı.</summary>
    public bool HasPoster { get; private set; }

    /// <summary>Uygulanan logo yerleşimi (<see cref="CardBranding"/> kipleri).</summary>
    public string Mode { get; private set; } = CardBranding.HeaderSides;

    /// <summary>Kartın logoları; yazılar bunların üstüne binmeyecek biçimde yerleşir.</summary>
    public IReadOnlyList<LogoPlacement> Logos { get; private set; } = Array.Empty<LogoPlacement>();

    /// <summary>
    /// Üst bölümü içeriğine göre boyutlanan yerleşim: afiş oranına göre, başlıkta iki yanda logolarda
    /// sabit logo şeridi, yalnız turnuva adında adın kapladığı yükseklik kadar (logolar afiş bitişindeyse
    /// adın altında logoların yarısına yer bırakılır). Ardından logolar yerleştirilir.
    /// </summary>
    public static CardLayout For(bool landscape, SizeF? poster, bool fullWidth, CardBranding? branding, string eventName, string? font)
    {
        branding ??= CardBranding.None;
        var max = new CardLayout(landscape);
        bool hasPoster = poster is { Width: > 0, Height: > 0 };
        string mode = branding.EffectiveMode(hasPoster);
        CardLayout l;
        if (hasPoster)
        {
            var p = poster!.Value;
            float fullH = max.PageW * p.Height / p.Width;
            l = new CardLayout(landscape, fullWidth ? fullH : FitImage(p, max.LogoRect).Height + 2 * Margin);
            l.SeamY = fullWidth ? Math.Min(l.HeaderH, (l.HeaderH + fullH) / 2) : FitImage(p, l.LogoRect).Bottom;
        }
        else
        {
            if (branding.Any && mode == CardBranding.HeaderSides) // tek logoda logo üstte, ad altında: biraz daha yüksek
                l = new CardLayout(landscape, max.PageH * (landscape ? (branding.Single ? 0.42f : 0.34f) : 0.32f));
            else
            {
                var fit = Fit(UpperTr(eventName), max.LogoRect, NameMaxPt, font, 12, 4);
                float extra = branding.Any && mode is CardBranding.SeamCenter or CardBranding.SeamSides ? branding.BaseHeight(max) / 2 : 0;
                l = new CardLayout(landscape, fit.Lines.Count * fit.Pt * CardFonts.LineSpacing(font) + 2 * Margin + 8 + extra);
            }
            l.SeamY = l.HeaderH;
        }
        l.Mode = mode;
        l.HasPoster = hasPoster;
        if (branding.Any) l.Logos = branding.Place(l, mode);
        return l;
    }

    public RectangleF BandRect => new(0, HeaderH, PageW, PageH - HeaderH);
    public RectangleF LogoRect => new(Margin, Margin, PageW - 2 * Margin, HeaderH - 2 * Margin);

    /// <summary>Bant içindeki yazı alanı (kenarlardan pay bırakılır; logoların üstüne binmez).</summary>
    public RectangleF BandTextRect
    {
        get
        {
            var b = BandRect;
            var box = new RectangleF(b.X + 36, b.Y + 22, b.Width - 72, b.Height - 44);
            return Centered(Avoid(box, Logos.Select(l => l.Rect)), box);
        }
    }

    private bool SideLogos => Mode == CardBranding.HeaderSides && Logos.Count > 0 && !HasPoster;
    private bool Stacked => !Landscape || Logos.Count == 1;

    /// <summary>Turnuva adının alanı (afiş yokken): logoların arası / altı ya da logolardan arta kalan yer.</summary>
    public RectangleF NameRect
    {
        get
        {
            var area = LogoRect;
            if (!SideLogos) return Centered(Avoid(area, Logos.Select(l => l.Rect)), area);
            if (Stacked) // dikey kartta logoların arası dar, tek logoda logo ortada: ad altlarında, tam genişlikte
                return RectangleF.FromLTRB(area.Left, Logos.Max(l => l.Rect.Bottom) + 8, area.Right, area.Bottom);
            float side = Logos.Max(l => l.Rect.Width) + LogoGap; // iki yandan eşit pay: ad sayfada ortalı kalsın
            return new RectangleF(area.X + side, area.Y, area.Width - 2 * side, area.Height);
        }
    }

    /// <summary>Üst alanın tamamı (kenardan kenara afiş için).</summary>
    public RectangleF HeaderRect => new(0, 0, PageW, HeaderH);

    /// <summary>En büyük afiş yuvasının en-boy oranı (kırpma aracı bu orana kilitlenir).</summary>
    public float LogoAspect => (PageW - 2 * Margin) / (PageH * MaxHeaderRatio - 2 * Margin);

    /// <summary>Tam genişlik modunda en büyük afiş alanının en-boy oranı.</summary>
    public float HeaderAspect => PageW / (PageH * MaxHeaderRatio);

    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");
    public static string UpperTr(string? s) => (s ?? "").ToUpper(Tr);

    /// <summary>Afiş yokken üst bölümdeki turnuva adı.</summary>
    public void DrawEventName(Graphics g, string eventName, Color color, string? font)
    {
        var box = NameRect;
        if (box.Width < 20 || box.Height < 10) return;
        float maxPt = SideLogos ? (Stacked ? 30 : OfficialNameMaxPt) : NameMaxPt;
        int lines = SideLogos ? (Stacked ? 2 : 3) : 4;
        DrawLines(g, Fit(UpperTr(eventName), box, maxPt, font, 9, lines), box, color, font);
    }

    /// <summary>
    /// Kutudan engellerin (logoların) kapladığı yeri çıkarır: her engel için kutunun engelin üstünde,
    /// altında, solunda ya da sağında kalan parçalarından en büyüğü seçilir.
    /// </summary>
    public static RectangleF Avoid(RectangleF box, IEnumerable<RectangleF> obstacles, float pad = 8)
    {
        foreach (var o0 in obstacles)
        {
            var o = RectangleF.Inflate(o0, pad, pad);
            if (!box.IntersectsWith(o)) continue;
            var parts = new[]
            {
                RectangleF.FromLTRB(box.Left, box.Top, box.Right, Math.Min(box.Bottom, o.Top)),
                RectangleF.FromLTRB(box.Left, Math.Max(box.Top, o.Bottom), box.Right, box.Bottom),
                RectangleF.FromLTRB(box.Left, box.Top, Math.Min(box.Right, o.Left), box.Bottom),
                RectangleF.FromLTRB(Math.Max(box.Left, o.Right), box.Top, box.Right, box.Bottom),
            };
            box = parts.Where(r => r.Width > 0 && r.Height > 0).OrderByDescending(r => r.Width * r.Height)
                       .DefaultIfEmpty(new RectangleF(box.X, box.Y, 0, 0)).First();
        }
        return box;
    }

    /// <summary>Yandan kırpılmış kutuyu (genişliğinin çoğu korunuyorsa) sayfa ortasına göre simetrik yapar.</summary>
    private RectangleF Centered(RectangleF box, RectangleF full)
    {
        if (box.Width >= full.Width - 0.5f || box.Width <= 0) return box;
        float cx = PageW / 2, hw = Math.Min(cx - box.Left, box.Right - cx);
        return hw * 2 >= box.Width * 0.6f ? new RectangleF(cx - hw, box.Y, hw * 2, box.Height) : box;
    }

    /// <summary>
    /// Afişi çizer. Normal: kenar boşluklu yuvaya oranı korunarak sığdırılır. Tam genişlik: sayfanın tüm
    /// genişliğine yayılır (oran korunur); yüksekliği alanı aşarsa ortadan kırpılır, kısaysa dikeyde ortalanır.
    /// </summary>
    public void DrawLogo(Graphics g, Image logo, bool fullWidth)
    {
        if (!fullWidth)
        {
            g.DrawImage(logo, FitImage(new SizeF(logo.Width, logo.Height), LogoRect));
            return;
        }
        var area = HeaderRect;
        float h = logo.Height * (area.Width / logo.Width);
        var dest = new RectangleF(area.X, area.Y + (area.Height - h) / 2, area.Width, h);
        var state = g.Save();
        g.SetClip(area);
        g.DrawImage(logo, dest);
        g.Restore(state);
    }

    /// <summary>Resmi oranını koruyarak kutuya ortalar.</summary>
    public static RectangleF FitImage(SizeF img, RectangleF box)
    {
        if (img.Width <= 0 || img.Height <= 0) return box;
        float s = Math.Min(box.Width / img.Width, box.Height / img.Height);
        float w = img.Width * s, h = img.Height * s;
        return new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Metni kutuya en büyük puntoda sığdırır (sözcük sınırında en çok <paramref name="maxLines"/> satır).
    /// </summary>
    public static (List<string> Lines, float Pt) Fit(string text, RectangleF box, float maxPt, string? font,
                                                      float minPt = 12, int maxLines = 3)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return (new List<string>(), maxPt);
        for (float pt = maxPt; pt >= minPt; pt -= Math.Max(0.5f, pt * 0.03f))
            if (TryWrap(words, box, pt, font, maxLines) is { } lines) return (lines, pt);
        // En küçük puntoda bile sığmıyorsa: kutuya sığacak kadar daha da küçült (taşmasın)
        for (float pt = minPt; pt >= 4; pt -= 0.5f)
            if (TryWrap(words, box, pt, font, maxLines + 2) is { } lines) return (lines, pt);
        return (Wrap(words, float.MaxValue, 4, font)!, 4);
    }

    /// <summary>
    /// Ortak punto: tüm metinlerin kutuya sığdığı en büyük punto (her kartta kategori adı aynı büyüklükte
    /// olsun; uzun bir ad diğerlerini de o boyuta indirir).
    /// </summary>
    public static float CommonPt(IEnumerable<string> texts, RectangleF box, float maxPt, string? font, float minPt = 12, int maxLines = 3)
    {
        float pt = maxPt;
        foreach (var t in texts.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct())
            pt = Math.Min(pt, Fit(t, box, maxPt, font, minPt, maxLines).Pt);
        return pt;
    }

    /// <summary>Metni verilen puntoda satırlara böler; o puntoda sığmıyorsa kendi en büyük puntosuna düşer.</summary>
    public static (List<string> Lines, float Pt) FitAt(string text, RectangleF box, float pt, string? font, int maxLines = 3)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return (new List<string>(), pt);
        return TryWrap(words, box, pt, font, maxLines) is { } lines ? (lines, pt) : Fit(text, box, pt, font, 4, maxLines);
    }

    /// <summary>Ölçüm ile çizim arasındaki küçük farklara karşı genişlik/yükseklikte pay bırakılır.</summary>
    private const float Safety = 0.95f;

    private static List<string>? TryWrap(string[] words, RectangleF box, float pt, string? font, int maxLines)
    {
        var lines = Wrap(words, box.Width * Safety, pt, font);
        if (lines is null || lines.Count > maxLines) return null;
        return lines.Count * pt * CardFonts.LineSpacing(font) <= box.Height * Safety ? lines : null;
    }

    private static List<string>? Wrap(string[] words, float width, float pt, string? font)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            if (CardFonts.MeasureWidth(font, w, pt) > width) return null; // tek sözcük sığmıyor
            var cand = cur.Length == 0 ? w : cur + " " + w;
            if (CardFonts.MeasureWidth(font, cand, pt) <= width) cur = cand;
            else { lines.Add(cur); cur = w; }
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    /// <summary>Satırları kutuda dikey ortalı, yatay ortalı çizer.</summary>
    public static void DrawLines(Graphics g, (List<string> Lines, float Pt) fit, RectangleF box, Color color, string? font)
    {
        using var f = CardFonts.Create(font, fit.Pt);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        float lh = fit.Pt * CardFonts.LineSpacing(font);
        float y = box.Y + (box.Height - lh * fit.Lines.Count) / 2;
        foreach (var line in fit.Lines)
        {
            g.DrawString(line, f, brush, new RectangleF(box.X - 50, y, box.Width + 100, lh), fmt);
            y += lh;
        }
    }

    /// <summary>Zemin rengine göre okunaklı yazı rengi (açık zeminde siyah, koyuda beyaz).</summary>
    public static Color TextColorFor(Color bg)
    {
        double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return lum > 0.6 ? Color.FromArgb(20, 20, 20) : Color.White;
    }

    public static Color ParseColor(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
