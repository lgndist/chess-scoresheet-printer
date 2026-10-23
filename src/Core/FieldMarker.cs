namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Düzenleme önizlemesi için alan işaretleyici. Belge "düzenleme modunda" doldurulurken her alanın
/// yazısı kendine özgü, gözle ayırt edilemeyen bir renkle boyanır (siyaha çok yakın: kanal başına
/// 0–15). Word'ün ürettiği PDF'te harf renklerinden hangi yazının hangi alana ait olduğu ve sayfadaki
/// yeri bulunur. Boş alanlar gri yer tutucuyla ("[Telefon]") gösterilir; bunlar da açık gri
/// tonlarında (0x90–0x9F) kodlanır. En çok 4095 alan/hücre işaretlenir (fazlası düzenlenemez, ama
/// asla başka bir hücreyle karışmaz). Basılan son belge bu modda üretilmez.
/// </summary>
public sealed class FieldMarker
{
    public const int MaxFields = 4095;
    private const int PlaceholderBase = 0x90;

    private readonly Func<string, string> _placeholder;
    private readonly Dictionary<string, int> _ids = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="placeholder">Boş alan için gösterilecek yazı (ör. anahtar → "[Telefon]").</param>
    public FieldMarker(Func<string, string> placeholder) => _placeholder = placeholder;

    /// <summary>Kod → alan anahtarı.</summary>
    public IReadOnlyDictionary<int, string> Keys => _ids.ToDictionary(kv => kv.Value, kv => kv.Key);

    /// <summary>Alanın kodu; kapasite dolduysa 0 (işaretsiz: saf siyah/gri, hiçbir alana çözülmez).</summary>
    public int IdOf(string key)
    {
        if (_ids.TryGetValue(key, out var id)) return id;
        if (_ids.Count >= MaxFields) return 0;
        id = _ids.Count + 1;
        _ids[key] = id;
        return id;
    }

    public string Placeholder(string key) => _placeholder(key);

    /// <summary>Alan kodunun rengi ("RRGGBB"). Dolu alan siyaha, yer tutucu açık griye yakın.</summary>
    public static string ColorHex(int id, bool placeholder)
    {
        int r = (id >> 8) & 15, g = (id >> 4) & 15, b = id & 15, k = placeholder ? PlaceholderBase : 0;
        return $"{k + r:X2}{k + g:X2}{k + b:X2}";
    }

    /// <summary>Harf renginden alan kodu (işaretsiz yazı → null).</summary>
    public static (int Id, bool Placeholder)? Decode(int r, int g, int b)
    {
        if (r is >= 0 and <= 15 && g is >= 0 and <= 15 && b is >= 0 and <= 15)
        {
            int id = (r << 8) | (g << 4) | b;
            return id == 0 ? null : (id, false); // saf siyah (0,0,0) belgenin kendi yazısıdır
        }
        int k = PlaceholderBase;
        if (r - k is >= 0 and <= 15 && g - k is >= 0 and <= 15 && b - k is >= 0 and <= 15)
        {
            int id = ((r - k) << 8) | ((g - k) << 4) | (b - k);
            return id == 0 ? null : (id, true);
        }
        return null;
    }
}
