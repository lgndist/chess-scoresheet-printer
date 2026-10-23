namespace NotasyonOtomasyonu.Core;

/// <summary>Türkiye'nin 81 ili (alfabetik). İl seçimi turnuva adında geçen ile göre filtreler.</summary>
public static class Provinces
{
    public const string All = "(Tüm iller)";

    public static readonly string[] List =
    {
        All,
        "Adana", "Adıyaman", "Afyonkarahisar", "Ağrı", "Aksaray", "Amasya", "Ankara", "Antalya",
        "Ardahan", "Artvin", "Aydın", "Balıkesir", "Bartın", "Batman", "Bayburt", "Bilecik",
        "Bingöl", "Bitlis", "Bolu", "Burdur", "Bursa", "Çanakkale", "Çankırı", "Çorum",
        "Denizli", "Diyarbakır", "Düzce", "Edirne", "Elazığ", "Erzincan", "Erzurum", "Eskişehir",
        "Gaziantep", "Giresun", "Gümüşhane", "Hakkari", "Hatay", "Iğdır", "Isparta", "İstanbul",
        "İzmir", "Kahramanmaraş", "Karabük", "Karaman", "Kars", "Kastamonu", "Kayseri", "Kilis",
        "Kırıkkale", "Kırklareli", "Kırşehir", "Kocaeli", "Konya", "Kütahya", "Malatya", "Manisa",
        "Mardin", "Mersin", "Muğla", "Muş", "Nevşehir", "Niğde", "Ordu", "Osmaniye", "Rize",
        "Sakarya", "Samsun", "Siirt", "Sinop", "Sivas", "Şanlıurfa", "Şırnak", "Tekirdağ",
        "Tokat", "Trabzon", "Tunceli", "Uşak", "Van", "Yalova", "Yozgat", "Zonguldak"
    };

    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    // Kayıt listelerinde geçen eski/kısa adlar.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["afyon"] = "Afyonkarahisar", ["maras"] = "Kahramanmaraş", ["k maras"] = "Kahramanmaraş",
        ["urfa"] = "Şanlıurfa", ["antep"] = "Gaziantep", ["icel"] = "Mersin", ["izmit"] = "Kocaeli",
        ["adapazari"] = "Sakarya"
    };

    /// <summary>
    /// Serbest yazılmış il adını listedeki ada çevirir ("ISPARTA", "istanbul", "AFYON" → "Isparta",
    /// "İstanbul", "Afyonkarahisar"); tanınmazsa null.
    /// </summary>
    public static string? Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = Fold(name);
        foreach (var p in List)
            if (p != All && Fold(p) == key) return p;
        return Aliases.TryGetValue(key, out var a) ? a : null;
    }

    /// <summary>Kağıda basılacak biçim: Türkçe büyük harf ("ISPARTA", "İZMİR"); tanınmayan ad olduğu gibi büyütülür.</summary>
    public static string Print(string? name)
        => string.IsNullOrWhiteSpace(name) ? "" : (Normalize(name) ?? name.Trim()).ToUpper(Tr);

    // Dar kutularda (notasyonun İl kutusu) kullanılan yaygın kısa adlar.
    private static readonly Dictionary<string, string> Short = new()
    {
        ["Afyonkarahisar"] = "AFYON", ["Kahramanmaraş"] = "K.MARAŞ", ["Şanlıurfa"] = "Ş.URFA"
    };

    /// <summary>İlin yaygın kısa adı ("AFYONKARAHİSAR" → "AFYON"); kısa adı yoksa basılacak biçimi.</summary>
    public static string ShortForm(string? name)
        => Normalize(name) is { } p && Short.TryGetValue(p, out var s) ? s : Print(name);

    private static string Fold(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s.Trim())
            sb.Append(ch switch
            {
                'ç' or 'Ç' => 'c', 'ğ' or 'Ğ' => 'g', 'ı' or 'I' or 'İ' or 'i' => 'i',
                'ö' or 'Ö' => 'o', 'ş' or 'Ş' => 's', 'ü' or 'Ü' => 'u', '.' or '-' => ' ',
                _ => char.ToLowerInvariant(ch)
            });
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
