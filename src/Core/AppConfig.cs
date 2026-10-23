using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Uygulama yapılandırması. config.json'dan okunur, son kullanılan değerleri hatırlamak için
/// geri yazılabilir. Tüm alanların makul varsayılanları vardır; config.json olmasa da çalışır.
/// </summary>
public sealed class AppConfig
{
    public TournamentConfig Tournament { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();

    /// <summary>chess-results entegrasyonu ve son seçili turnuva/kategori (kalıcı).</summary>
    public OnlineConfig Online { get; set; } = new();

    /// <summary>Hazır notasyon kağıdına bindirme (overlay) şablonu — ETKİN olan.</summary>
    public OverlayTemplate Overlay { get; set; } = new();

    /// <summary>Kayıtlı hazır kağıt şablonları kütüphanesi (kullanıcı birden çok ekleyebilir).</summary>
    public List<OverlayTemplate> Templates { get; set; } = new();

    /// <summary>
    /// Sisteme göre şablon: "Swiss" / "RoundRobin" / "Team" → şablon adı. Boş/eksik ise etkin
    /// şablon (<see cref="Overlay"/>) kullanılır. Böylece aynı turnuvada Berger kategorisi ya da
    /// takım maçları farklı kağıda basılabilir.
    /// </summary>
    public Dictionary<string, string> TemplateBySystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Yerleşik şablon setinin sürümü. Yeni sürümde eklenen varsayılan şablon mevcut config'e
    /// bir kez eklenip etkin yapılır (bkz. OverlayDefaults.EnsureDefaults).
    /// </summary>
    public int TemplateSetVersion { get; set; }

    /// <summary>Yönerge/rapor: kullanıcının yüklediği .docx şablonları (exe yanındaki reports klasöründe).</summary>
    public List<string> ReportTemplates { get; set; } = new();

    /// <summary>Yönerge/rapor sihirbazında verilen cevaplar (telefon, e-posta…); sonraki raporda önerilir.</summary>
    public Dictionary<string, string> ReportAnswers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Turnuva bazında son rapor/tutanak değerleri (etkinlik tnr → alan → değer); tutanağa aktarılır.</summary>
    public Dictionary<string, Dictionary<string, string>> ReportEventValues { get; set; } = new();

    /// <summary>TSF il sitesi adresini değiştirmek için (il → "https://…tsf.org.tr"); boşsa gömülü liste.</summary>
    public Dictionary<string, string> TsfSites { get; set; } = new();

    /// <summary>TSF kayıt listesinden turnuva başında alınan sporcu/il listesi (etkinlik tnr → kayıt).</summary>
    public Dictionary<string, EventRegistration> Registrations { get; set; } = new();

    /// <summary>Önceki turnuvalarda öğrenilen sporcu illeri (katlanmış ad → il); kayıt listesi yoksa önerilir.</summary>
    public Dictionary<string, string> PlayerProvinces { get; set; } = new();

    /// <summary>Kategori masa kartları: afiş/logo ve kategori renkleri (kategori tnr → "#RRGGBB").</summary>
    public CardConfig Cards { get; set; } = new();

    /// <summary>Hakem yaka kartları.</summary>
    public BadgeConfig Badges { get; set; } = new();

    /// <summary>Yazıcı: hedef yazıcı, tepsi, kopya, yazıcı tercihleri ve doğrudan yazdırma.</summary>
    public PrintConfig Printing { get; set; } = new();

    /// <summary>Pairing'in sistemine göre basılacak şablon (eşleme yoksa etkin şablon).</summary>
    public OverlayTemplate TemplateFor(TournamentSystem system)
    {
        if (TemplateBySystem.TryGetValue(system.TemplateKey(), out var name) && !string.IsNullOrWhiteSpace(name))
        {
            var t = Templates.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (t is not null && t.IsConfigured) return t;
        }
        return Overlay;
    }

    /// <summary>
    /// CSV/XLSX sütun başlığı eşlemesi. Her hedef alan için olası başlık adları (büyük/küçük harf duyarsız).
    /// Sürüme/dile göre değişen Swiss-Manager başlıklarını tek modele bağlar.
    /// </summary>
    public Dictionary<string, List<string>> Mapping { get; set; } = DefaultMapping();

    // ---- JSON ayarları ----
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Dosyadan yükle; yoksa veya bozuksa varsayılanları döndür (asla istisna fırlatmaz).</summary>
    public static AppConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppConfig();
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
            // Mapping boş geldiyse varsayılanlarla doldur ki parser çalışsın.
            if (cfg.Mapping is null || cfg.Mapping.Count == 0)
                cfg.Mapping = DefaultMapping();
            else
                MergeDefaults(cfg.Mapping);
            return cfg;
        }
        catch
        {
            return new AppConfig();
        }
    }

    /// <summary>Diske kaydet (hata olursa sessizce yutulur, çökme yok).</summary>
    public void Save(string path)
    {
        try
        {
            // Kalıcı seçimler sınırsız büyümesin: en eski kayıtları at.
            while (Online.Selections.Count > 400) Online.Selections.Remove(Online.Selections.Keys.First());
            while (ReportEventValues.Count > 40) ReportEventValues.Remove(ReportEventValues.Keys.First());
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch { /* config kaydı kritik değil */ }
    }

    /// <summary>Config'te tanımlı olmayan eşleme anahtarlarını varsayılanlardan tamamla.</summary>
    private static void MergeDefaults(Dictionary<string, List<string>> map)
    {
        foreach (var (k, v) in DefaultMapping())
            if (!map.ContainsKey(k)) map[k] = v;
    }

    /// <summary>Türkçe + İngilizce Swiss-Manager başlık adayları.</summary>
    public static Dictionary<string, List<string>> DefaultMapping() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["board"]            = new() { "Br.", "Br", "Masa", "Board", "Bo.", "Bo", "Table", "No" },
        ["whiteStartNo"]     = new() { "SNo", "SNr", "Sıra", "StartNo", "No1", "WhiteNo" },
        ["whiteName"]        = new() { "Beyaz", "White", "Name", "Ad Soyad", "İsim", "Player1", "WhiteName" },
        ["whiteTitle"]       = new() { "Unvan", "Title", "Ttl", "WhiteTitle" },
        ["whiteRating"]      = new() { "Rtg", "RtgI", "ELO", "Rating", "WRtg", "WhiteRating", "Elo1" },
        ["whiteFederation"]  = new() { "Fed", "Federation", "Ülke", "WhiteFed" },
        ["whiteClub"]        = new() { "Kulüp", "Club", "Takım", "WhiteClub" },
        ["blackStartNo"]     = new() { "SNo2", "No2", "BlackNo" },
        ["blackName"]        = new() { "Siyah", "Black", "Player2", "BlackName", "Rakip" },
        ["blackTitle"]       = new() { "Unvan2", "Title2", "BlackTitle" },
        ["blackRating"]      = new() { "Rtg2", "ELO2", "BRtg", "BlackRating", "Elo2", "RtgII" },
        ["blackFederation"]  = new() { "Fed2", "BlackFed" },
        ["blackClub"]        = new() { "Kulüp2", "Club2", "BlackClub" },
        ["result"]           = new() { "Sonuç", "Result", "Res.", "Res" }
    };
}

/// <summary>Kategori masa kartları ayarları (kalıcı).</summary>
public sealed class CardConfig
{
    /// <summary>Kartın üst bölümüne basılacak turnuva afişi/logosu (kırpılmış hâli; boş = turnuva adı yazılır).</summary>
    public string? LogoPath { get; set; }

    /// <summary>Afişin kırpılmamış asıl hâli (yeniden kırpmak için).</summary>
    public string? LogoSourcePath { get; set; }

    /// <summary>
    /// Kart stili: false = yazı tema renginde, zemin beyaz (varsayılan); true = alt bant tema renginde,
    /// yazı beyaz.
    /// </summary>
    public bool FilledBand { get; set; }

    /// <summary>Kategori tnr → tema rengi ("#RRGGBB"). Atanan renk kalıcıdır; aynı kategori hep aynı renkte basılır.</summary>
    public Dictionary<string, string> CategoryColors { get; set; } = new();

    /// <summary>Kart yazı tipi (gömülü: "Montserrat", "Oswald", "Inter", "DejaVu Sans").</summary>
    public string FontName { get; set; } = "Montserrat";

    /// <summary>Kart yönü: yatay (varsayılan) ya da dikey A4.</summary>
    public bool Landscape { get; set; } = true;

    /// <summary>Afiş üst alanın tüm genişliğine (kenardan kenara) yayılsın; oran korunur, taşan kısım kırpılır.</summary>
    public bool LogoFullWidth { get; set; }

    /// <summary>Yazıcıda birbirinden net ayrılan temel renkler (otomatik atama sırası).</summary>
    public static readonly string[] Palette =
    {
        "#D32F2F", "#1565C0", "#2E7D32", "#F9A825", "#6A1B9A", "#EF6C00",
        "#212121", "#00838F", "#AD1457", "#5D4037", "#827717", "#283593"
    };

    /// <summary>Kategorinin rengi: kayıtlıysa o; değilse bu etkinlikte kullanılmamış ilk palet rengi atanır.</summary>
    public string ColorFor(int categoryTnr, IEnumerable<int> eventCategoryTnrs)
    {
        var key = categoryTnr.ToString();
        if (CategoryColors.TryGetValue(key, out var c)) return c;
        var used = eventCategoryTnrs.Select(t => CategoryColors.GetValueOrDefault(t.ToString()))
                                    .Where(x => x is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pick = Palette.FirstOrDefault(p => !used.Contains(p)) ?? Palette[CategoryColors.Count % Palette.Length];
        CategoryColors[key] = pick;
        return pick;
    }
}

public sealed class TournamentConfig
{
    public string Name { get; set; } = "Turnuva Adı";
    /// <summary>Notasyona basılan TEK tarih (örn. "14.06.2026").</summary>
    public string? Date { get; set; }
    /// <summary>Turnuvanın ham tarih ifadesi/aralığı (tarih seçimi listesi için).</summary>
    public string? DateRange { get; set; }
    /// <summary>UI'da son seçilen tur no'yu hatırlamak için.</summary>
    public int LastRound { get; set; } = 1;

    /// <summary>
    /// Kullanıcı ad/tarih bilgisini elle değiştirdi mi? true ise senkronda çekilen
    /// veriyle ezilmez. Farklı turnuva seçilince false'a döner (çekilen veri kullanılsın).
    /// </summary>
    public bool ManualOverride { get; set; }
}

/// <summary>
/// chess-results.com ayarları ve kullanıcının son seçimi. Tümü kalıcıdır; kullanıcı
/// başka turnuva/kategori seçene kadar burada saklı kalır.
/// </summary>
public sealed class OnlineConfig
{
    /// <summary>Ülke sabit (Türkiye = TUR federasyonu).</summary>
    public string Country { get; set; } = "Türkiye";
    public string Federation { get; set; } = "TUR";

    /// <summary>Arama kutusunda en son yazılan metin.</summary>
    public string CityFilter { get; set; } = "";

    /// <summary>Seçili il (filtre). "(Tüm iller)" = filtre yok.</summary>
    /// <summary>
    /// Varsayılan il (Ayarlar → Bölge). Program açılınca ana ekranda bu il seçili gelir ve en güncel
    /// turnuvası açılır; "(Tüm iller)" = seçilmedi (ana ekranda il kutusu boş gelir).
    /// </summary>
    public string Province { get; set; } = "(Tüm iller)";

    // Son seçili turnuva (etkinlik) ve kategori
    public int? SelectedTnr { get; set; }
    public string? SelectedTournamentName { get; set; }
    public int? SelectedCategoryTnr { get; set; }
    public string? SelectedCategoryName { get; set; }

    /// <summary>Son kullanılan veri kaynağı: "Dosya" veya "Online".</summary>
    public string Source { get; set; } = "Online";

    /// <summary>
    /// "kategoriTnr:tur" → işareti KALDIRILMIŞ masalar. Seçimler baskıdan ve program kapanıp
    /// açıldıktan sonra da korunur (en son 400 kategori/tur tutulur).
    /// </summary>
    public Dictionary<string, List<int>> Selections { get; set; } = new();
}

public sealed class LayoutConfig
{
    /// <summary>Sayfa başına kağıt: 1 veya 2.</summary>
    public int PerPage { get; set; } = 2;
    /// <summary>Toplam hamle kapasitesi (2 bloğa bölünür).</summary>
    public int MoveRows { get; set; } = 60;
    public bool ShowRefereeSignature { get; set; } = true;
    /// <summary>BAY masaları için kağıt basılsın mı?</summary>
    public bool PrintByeSheets { get; set; } = true;

    /// <summary>Her masa için nüsha sayısı (varsayılan 4: iki oyuncuya ikişer).</summary>
    public int CopiesPerBoard { get; set; } = DefaultCopies;

    public const int DefaultCopies = 4;

    /// <summary>Ana penceredeki işlem kaydı açık mı (varsayılan kapalı: yalnız başlık satırı görünür).</summary>
    public bool LogExpanded { get; set; }

    /// <summary>Sayfa boyutu: "A4" veya "A5". Çıktılar genelde A5.</summary>
    public string PageSize { get; set; } = "A5";

    /// <summary>
    /// Yazıcı kaydırma düzeltmesi (mm). Her yazıcı hazır kağıdı biraz farklı konumda çeker;
    /// tüm yazılar bu kadar sağa (+X) / aşağı (+Y) kaydırılarak basılır. Şablonu bozmadan hizalar.
    /// </summary>
    public double PrintOffsetXmm { get; set; }
    public double PrintOffsetYmm { get; set; }

    /// <summary>
    /// Basılmayacak (hariç tutulacak) masa numaraları. "3,7,12-15" gibi. Boş = hepsini bas.
    /// Kullanıcı "Hariç Tut" ekranından seçer; kalıcıdır.
    /// </summary>
    public string ExcludedBoards { get; set; } = "";
}

/// <summary>Hakem yaka kartı ayarları.</summary>
public sealed class BadgeConfig
{
    /// <summary>Kartın sol üstündeki resmi logo (boş = gömülü TSF/uygulama logosu yok, yalnız yazı).</summary>
    public string? LogoPath { get; set; }

    /// <summary>Kart ölçüsü: "85x54" (kredi kartı), "90x60" ya da büyük dikey "95x135" milimetre.</summary>
    public string Size { get; set; } = "85x54";

    /// <summary>Görev sade yazılsın: başhakem "Başhakem", diğer herkes "Hakem" (kapalıysa tablodaki görev aynen).</summary>
    public bool SimpleRoles { get; set; } = true;

    /// <summary>Kesim çizgileri ve köşe işaretleri basılsın mı.</summary>
    public bool CropMarks { get; set; } = true;

    /// <summary>Hakem adı (katlanmış) → vesikalık fotoğraf yolu.</summary>
    public Dictionary<string, string> Photos { get; set; } = new();

    /// <summary>Hakem adı (katlanmış) → elle seçilen unvan (chess-results'ta olmayan TSF dereceleri için).</summary>
    public Dictionary<string, string> Grades { get; set; } = new();
}

/// <summary>Yazıcı ayarları: hedef, tepsi, kopya, yazıcının kendi tercihleri (sessiz mod vb.) ve doğrudan yazdırma.</summary>
public sealed class PrintConfig
{
    /// <summary>Hedef yazıcı (boş = Windows varsayılan yazıcısı).</summary>
    public string? PrinterName { get; set; }

    /// <summary>Kağıt kaynağı / tepsi adı (boş = yazıcının otomatik seçimi).</summary>
    public string? PaperSource { get; set; }

    /// <summary>Her baskı işinin kopya sayısı (notasyon nüshası bundan ayrıdır).</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Doğrudan yazdır: önizleme ve yazdırma penceresi açılmadan hedef yazıcıya gönderilir.</summary>
    public bool Silent { get; set; }

    /// <summary>
    /// Yazıcı adı → yazıcının kendi tercihleri (DEVMODE, base64): sessiz mod, kalite, tepsi gibi üreticiye
    /// özel ayarlar. Ayarlar → "Yazıcı tercihleri…" ile seçilir, her baskıda uygulanır.
    /// </summary>
    public Dictionary<string, string> DevModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
